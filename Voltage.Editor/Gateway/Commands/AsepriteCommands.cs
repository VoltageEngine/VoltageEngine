using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Voltage.Editor.Assets;
using Voltage.Editor.ProjectFile;
using Voltage.Editor.Aseprite;
using Voltage.Gateway;

namespace Voltage.Editor.Gateway.Commands;

/// <summary>All bundled Aseprite commands, plus installation and connection management.</summary>
internal static class AsepriteCommands
{
	public static void Register(GatewayCommandTable table, AsepriteBridge bridge)
	{
		table.Add("aseprite.status", "Voltage's live Aseprite connection and extension status.", (_, _) => bridge.Status()).ReadOnly();
		table.Add("aseprite.connect", "Start the authenticated loopback bridge; the extension reconnects automatically.", (_, _) => { bridge.Start(); return bridge.Status(); }).Unsafe();
		table.Add("aseprite.disconnect", "Stop Voltage's Aseprite bridge.", (_, _) => { bridge.Stop(); return bridge.Status(); }).Unsafe();
		table.Add("aseprite.install", "Install or update Voltage's bundled Aseprite extension in the user's extension directory. Restart Aseprite afterwards.", (_, _) => bridge.Install()).Unsafe();
		table.Add("aseprite.guide", "Pixel art and animation workflow guidance from the bundled MIT-licensed package.", (_, _) => File.ReadAllText(Path.Combine(AsepriteBridge.Resources, "pixel-art-guide.md"))).ReadOnly();
		table.Add("aseprite.save_to_project", "Save a copy of the active live sprite under Content, refresh its GUID, and optionally create an animated sprite entity.", (args, _) =>
		{
			var project = ProjectManager.Instance.CurrentProject ?? throw new GatewayException("no project loaded");
			var path = GatewayPaths.RequireInside(Path.Combine(project.ContentsFolder, args.Require("path")), project.ContentsFolder, "path");
			if (Path.GetExtension(path).ToLowerInvariant() is not (".aseprite" or ".ase")) throw new GatewayException("path must end in .aseprite or .ase");
			if (File.Exists(path) && !args.Bool("overwrite")) throw new GatewayException("target exists; specify overwrite=true to replace it");
			var create = args.Bool("entity");
			if (create && !Core.IsEditMode) throw new GatewayException("entity creation requires Edit mode");
			var position = new Vector2(args.Float("x"), args.Float("y"));
			var scene = Core.Scene;
			if (create && scene == null) throw new GatewayException("no scene loaded");
			return SaveToProject(bridge, path, args.Has("voltage_sprite_id") ? args.Int("voltage_sprite_id") : (int?)null, () =>
			{
				if (!ReferenceEquals(project, ProjectManager.Instance.CurrentProject)) throw new GatewayException("project changed while saving; file saved but not imported");
				AssetDatabase.Instance.Refresh();
				var reference = AssetDatabase.Instance.GetReference(path);
				object entity = null;
				if (create)
				{
					if (!ReferenceEquals(scene, Core.Scene)) throw new GatewayException("file saved; scene changed before entity creation");
					if (!Core.IsEditMode) throw new GatewayException("file saved; return to Edit mode before adding an entity");
					entity = EntityCommands.Detail(DropHandlers.DropAsepriteAnimated(reference, position));
				}
				return new { path, guid = reference.Guid, entity };
			});
		}, P.Str("path", "Path relative to Content", required: true), P.Bool("overwrite", "Replace an existing file", false),
			P.Bool("entity", "Add an animated sprite entity in Edit mode", false), P.Float("x"), P.Float("y"),
			P.Int("voltage_sprite_id", "Optional stable sprite ID from list_open_sprites")).Unsafe();
		using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AsepriteBridge.Resources, "tools.json")));
		foreach (var entry in document.RootElement.EnumerateArray())
		{
			var name = entry.GetProperty("name").GetString();
			var schemaNode = JsonNode.Parse(entry.GetProperty("schema").GetRawText());
			schemaNode["properties"]["voltage_sprite_id"] = new JsonObject { ["type"] = "number", ["description"] = "Optional stable sprite ID from list_open_sprites; target that document for this command." };
			var schema = JsonSerializer.SerializeToElement(schemaNode);
			var parameters = schema.GetProperty("properties").EnumerateObject().Select(member =>
				new GatewayParam(member.Name, member.Value.GetProperty("type").GetString(),
					member.Value.TryGetProperty("description", out var description) ? description.GetString() : null,
					Required: schema.GetProperty("required").EnumerateArray().Any(item => item.GetString() == member.Name))).ToArray();
			var command = table.Add("aseprite." + name, entry.GetProperty("description").GetString(), (args, _) =>
			{
				var value = args.Value.ValueKind == JsonValueKind.Undefined ? JsonSerializer.SerializeToElement(new { }) : args.Value;
				AsepriteSchema.Validate(schema, value);
				return bridge.Call(name, value);
			}, parameters).WithInputSchema(schema);
			if (IsReadOnly(name)) command.ReadOnly();
			else command.Unsafe();
			if (name.StartsWith("delete_", StringComparison.Ordinal) || name is "close_sprite" or "flatten_sprite" or "clear_image" or "clear_cel" or "execute_script") command.Destructive();
		}
	}

	private static async Task<object> SaveToProject(AsepriteBridge bridge, string path, int? spriteId, Func<object> import)
	{
		var parameters = new JsonObject { ["path"] = path, ["rename"] = false };
		if (spriteId.HasValue) parameters["voltage_sprite_id"] = spriteId.Value;
		await bridge.Call("save_sprite", JsonSerializer.SerializeToElement(parameters));
		if (!File.Exists(path)) throw new GatewayException("Aseprite reported success but the saved file is missing");
		return await bridge.OnMainThread(import);
	}

	private static bool IsReadOnly(string name) => name.StartsWith("get_", StringComparison.Ordinal) ||
		name.StartsWith("list_", StringComparison.Ordinal) || name.StartsWith("compare_", StringComparison.Ordinal) ||
		name is "find_unused_colors" or "validate_animation";
}
