using System;
using System.Linq;
using System.Reflection;
using Voltage.Data;
using Voltage.Serialization.Registries;
using Voltage.Sprites;

namespace Voltage.Serialization;

/// <summary>
/// Registers engine-level component factories and ComponentData deserializers.
///
/// Game-project components are handled by source-generated [ModuleInitializer]
/// methods. Engine components live in Voltage.dll and the source generator
/// never sees them, so they are discovered here via a one-time reflection scan.
///
/// This is safe under NativeAOT because TrimmerRoots.xml preserves the entire
/// Voltage assembly (preserve="all").
/// </summary>
///
///
// TODO: Only manual registry for components currently works and RegisterEngineComponents() doesn't seem to work (investigate why)
public static class ComponentDataSerializationBootstrap
{
	private static bool _inited;

	public static void EnsureInitialized()
	{
		if (_inited)
			return;
		_inited = true;

		// Legacy numeric type-id registry
		ComponentDataSerializationRegistry.Register(1, typeof(EntityData));
		ComponentDataSerializationRegistry.Register(2, typeof(PrefabData));

		// Engine component factories
		// The source generator only processes the game project's .cs files, so engine
		// components must be registered here for NativeAOT published builds.
		ComponentAotFactory.Register("Voltage.Camera", () => new Camera());
		ComponentAotFactory.Register("Voltage.Sprites.SpriteRenderer", () => new SpriteRenderer());

		// Audio components
		ComponentAotFactory.Register("Voltage.Audio.AudioSourceComponent", () => new Audio.AudioSourceComponent());
		ComponentAotFactory.Register("Voltage.Audio.AudioListenerComponent", () => new Audio.AudioListenerComponent());
		ComponentAotFactory.Register("Voltage.Audio.MusicZoneComponent", () => new Audio.MusicZoneComponent());

		// Engine component data AOT deserializers
		// These use reflection-safe Voltage.Persistence.Json since engine ComponentData
		// types are preserved by TrimmerRoots.xml (the Voltage assembly is fully preserved).
		ComponentDataAotDeserializer.Register(
			"Voltage.Camera+CameraComponentData",
			json => (ComponentData)Persistence.Json.FromJson<Camera.CameraComponentData>(json));

		ComponentDataAotDeserializer.Register(
			"Voltage.Sprites.SpriteRenderer+SpriteRendererComponentData",
			json => (ComponentData)Persistence.Json.FromJson<SpriteRenderer.SpriteRendererComponentData>(json));

		ComponentDataAotDeserializer.Register(
			"Voltage.Audio.AudioSourceComponent+AudioSourceComponentData",
			json => (ComponentData)Persistence.Json.FromJson<Audio.AudioSourceComponent.AudioSourceComponentData>(json));

		ComponentDataAotDeserializer.Register(
			"Voltage.Audio.AudioListenerComponent+ListenerComponentData",
			json => (ComponentData)Persistence.Json.FromJson<Audio.AudioListenerComponent.ListenerComponentData>(json));

		ComponentDataAotDeserializer.Register(
			"Voltage.Audio.MusicZoneComponent+MusicZoneComponentData",
			json => (ComponentData)Persistence.Json.FromJson<Audio.MusicZoneComponent.MusicZoneComponentData>(json));

		RegisterEngineComponents();
	}

	/// <summary>
	/// Scans the Voltage engine assembly for all concrete Component subclasses
	/// and registers their factory + ComponentData deserializer automatically.
	/// Runs once at startup. No manual entries needed when new engine components
	/// are added — just ensure TrimmerRoots.xml preserves the Voltage assembly.
	/// </summary>
	private static void RegisterEngineComponents()
	{
		var engineAssembly = typeof(Component).Assembly;
		var componentBaseType = typeof(Component);
		var componentDataBaseType = typeof(ComponentData);

		foreach (var type in engineAssembly.GetTypes())
		{
			// Skip abstract/interface/generic types — we need concrete instantiable components
			if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
				continue;

			if (!componentBaseType.IsAssignableFrom(type))
				continue;

			// Must have a public parameterless constructor
			if (type.GetConstructor(Type.EmptyTypes) == null)
				continue;

			var fullName = type.FullName;
			if (string.IsNullOrEmpty(fullName) || ComponentAotFactory.IsRegistered(fullName))
				continue;

			// Register the component factory
			var capturedType = type;
			ComponentAotFactory.Register(fullName, () => (Component)Activator.CreateInstance(capturedType));

			// Find and register the ComponentData deserializer if the component
			// overrides the Data property with a concrete ComponentData subclass.
			var dataProp = type.GetProperty("Data",
				BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

			if (dataProp == null || !dataProp.CanRead)
				continue;

			// The getter return type tells us the concrete ComponentData subclass
			var dataType = dataProp.PropertyType;
			if (dataType == componentDataBaseType || !componentDataBaseType.IsAssignableFrom(dataType))
				continue;

			var dataFullName = dataType.FullName;
			if (string.IsNullOrEmpty(dataFullName) || ComponentDataAotDeserializer.IsRegistered(dataFullName))
				continue;

			var capturedDataType = dataType;
			ComponentDataAotDeserializer.Register(dataFullName,
				json => (ComponentData)Persistence.Json.FromJson(json, capturedDataType));
		}
	}
}