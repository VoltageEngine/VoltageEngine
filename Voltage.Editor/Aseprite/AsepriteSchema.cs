using System;
using System.Linq;
using System.Text.Json;
using Voltage.Gateway;

namespace Voltage.Editor.Aseprite;

/// <summary>Checks the complete nested schemas for Aseprite tool registrations.</summary>
public static class AsepriteSchema
{
	public static void Validate(JsonElement schema, JsonElement value, string path = "params")
	{
		var type = schema.GetProperty("type").GetString();
		var valid = type switch
		{
			"object" => value.ValueKind == JsonValueKind.Object,
			"array" => value.ValueKind == JsonValueKind.Array,
			"string" => value.ValueKind == JsonValueKind.String,
			"number" => value.ValueKind == JsonValueKind.Number && double.IsFinite(value.GetDouble()),
			"boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
			_ => false
		};
		if (!valid) throw new GatewayException($"{path} must be {type}");
		if (schema.TryGetProperty("enum", out var allowed) && !allowed.EnumerateArray().Any(item => item.GetRawText() == value.GetRawText()))
			throw new GatewayException($"{path} must be one of {allowed.GetRawText()}");
		if (type == "number")
		{
			var number = value.GetDouble();
			if (schema.TryGetProperty("minimum", out var min) && number < min.GetDouble() || schema.TryGetProperty("maximum", out var max) && number > max.GetDouble())
				throw new GatewayException($"{path} is out of range");
		}
		if (type == "object")
		{
			var properties = schema.GetProperty("properties");
			foreach (var required in schema.GetProperty("required").EnumerateArray())
				if (!value.TryGetProperty(required.GetString(), out _)) throw new GatewayException($"missing {path}.{required.GetString()}");
			foreach (var member in value.EnumerateObject())
			{
				if (!properties.TryGetProperty(member.Name, out var nested)) throw new GatewayException($"unknown {path}.{member.Name}");
				Validate(nested, member.Value, path + "." + member.Name);
			}
		}
		if (type == "array")
		{
			if (value.GetArrayLength() > 100000) throw new GatewayException($"{path} exceeds 100000 elements");
			var i = 0;
			foreach (var item in value.EnumerateArray()) Validate(schema.GetProperty("items"), item, $"{path}[{i++}]");
		}
	}
}
