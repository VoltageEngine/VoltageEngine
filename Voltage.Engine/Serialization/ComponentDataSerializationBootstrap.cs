using Voltage.Data;
using Voltage.Sprites;

namespace Voltage.Serialization;

/// <summary>
/// Registers the few serialization entries the source generator cannot emit.
///
/// <para><c>Voltage.SourceGenerators</c> now runs on the engine assembly, so component factories and
/// auto-generated deserializers register themselves via per-class <c>[ModuleInitializer]</c>s — not here.</para>
///
/// <para>What stays manual:
/// <list type="bullet">
///   <item>The legacy numeric type-id map (<see cref="ComponentDataSerializationRegistry"/>) — no generator equivalent.</item>
///   <item>Deserializers for the engine's <b>manual-Data-override</b> components (Camera, SpriteRenderer,
///     AudioListener): their <c>Data</c> returns the base <see cref="ComponentData"/> type, so the generator
///     can't see their concrete data type and emits no deserializer. Other manual-override components fall
///     back to reflection <c>Json.FromJson</c> (AOT-safe via TrimmerRoots).</item>
/// </list></para>
/// </summary>
public static class ComponentDataSerializationBootstrap
{
	private static bool _inited;

	public static void EnsureInitialized()
	{
		if (_inited)
			return;
		_inited = true;

		// Legacy numeric type-id registry — no source-generator equivalent.
		ComponentDataSerializationRegistry.Register(1, typeof(EntityData));
		ComponentDataSerializationRegistry.Register(2, typeof(PrefabData));

		// Manual-Data-override components: their Data returns the base ComponentData type, so the generator
		// emits no token-reader for their concrete data type — register here (reflection Json.FromJson;
		// AOT-safe via TrimmerRoots preserve="all").
		ComponentDataAotDeserializer.Register(
			"Voltage.Camera+CameraComponentData",
			json => (ComponentData)Persistence.Json.FromJson<Camera.CameraComponentData>(json));

		ComponentDataAotDeserializer.Register(
			"Voltage.Sprites.SpriteRenderer+SpriteRendererComponentData",
			json => (ComponentData)Persistence.Json.FromJson<SpriteRenderer.SpriteRendererComponentData>(json));

		ComponentDataAotDeserializer.Register(
			"Voltage.Audio.AudioListenerComponent+ListenerComponentData",
			json => (ComponentData)Persistence.Json.FromJson<Audio.AudioListenerComponent.ListenerComponentData>(json));
	}
}
