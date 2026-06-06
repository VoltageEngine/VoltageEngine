using System;
using System.Collections.Generic;
using Voltage.Persistence;

namespace Voltage.Serialization
{
	/// <summary>
	/// Registry for AOT-safe ComponentData deserializers.
	/// Source-generated [ModuleInitializer] methods register a deserializer
	/// for each ComponentData subclass. At runtime, TryAssignComponentData
	/// checks this registry before falling back to reflection-based Json.FromJson.
	/// </summary>
	public static class ComponentDataAotDeserializer
	{
		private static readonly Dictionary<string, Func<string, ComponentData>> _deserializers = new();

		/// <summary>
		/// Registers an AOT-safe deserializer for a ComponentData type.
		/// Called by source-generated [ModuleInitializer] methods.
		/// </summary>
		/// <param name="dataTypeFullName">The FullName of the ComponentData subclass</param>
		/// <param name="deserializer">A function that takes JSON string and returns the deserialized ComponentData</param>
		public static void Register(string dataTypeFullName, Func<string, ComponentData> deserializer)
		{
			_deserializers[dataTypeFullName] = deserializer;
		}

		/// <summary>
		/// Tries to deserialize a ComponentData from JSON using a registered AOT-safe deserializer.
		/// Returns null if no deserializer is registered for the given type name.
		/// </summary>
		public static ComponentData TryDeserialize(string dataTypeFullName, string json)
		{
			if (dataTypeFullName != null && _deserializers.TryGetValue(dataTypeFullName, out var deserializer))
				return deserializer(json);
			return null;
		}

		public static bool IsRegistered(string dataTypeFullName)
		{
			return dataTypeFullName != null && _deserializers.ContainsKey(dataTypeFullName);
		}

		public static IReadOnlyCollection<string> RegisteredTypeNames => _deserializers.Keys;
	}
}