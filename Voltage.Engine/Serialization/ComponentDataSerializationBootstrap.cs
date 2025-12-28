using Voltage.Data;
using Voltage.DeferredLighting;
using Voltage.Sprites;

namespace Voltage.Serialization;

public static class ComponentDataSerializationBootstrap
{
	private static bool _inited;

	public static void EnsureInitialized()
	{
		if (_inited)
			return;
		_inited = true;

		// NOTE: This is a temporary bootstrap. Next step is source-generation.
		ComponentDataSerializationRegistry.Register(1, typeof(DirLight.DirLightComponentData));
		ComponentDataSerializationRegistry.Register(2, typeof(PointLight.PointLightComponentData));
		ComponentDataSerializationRegistry.Register(3, typeof(SpotLight.SpotLightComponentData));
		ComponentDataSerializationRegistry.Register(4, typeof(AreaLight.AreaLightComponentData));
		ComponentDataSerializationRegistry.Register(10, typeof(CameraShake.CameraShakeComponentData));
		ComponentDataSerializationRegistry.Register(20, typeof(Collider.ColliderComponentData));
		ComponentDataSerializationRegistry.Register(21, typeof(ArcadeRigidbody.ArcadeRigidbodyComponentData));
		ComponentDataSerializationRegistry.Register(30, typeof(SpriteRenderer.SpriteRendererComponentData));
		ComponentDataSerializationRegistry.Register(31, typeof(TiledMapRenderer.TiledMapRendererComponentData));
		ComponentDataSerializationRegistry.Register(32, typeof(SpriteAnimator.SpriteAnimatorComponentData));
		ComponentDataSerializationRegistry.Register(100, typeof(EntityData));
		ComponentDataSerializationRegistry.Register(101, typeof(PrefabData));
	}
}
