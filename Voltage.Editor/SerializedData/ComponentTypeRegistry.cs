using Voltage.Data;
using Voltage.DeferredLighting;
using Voltage.Sprites;
using System;
using System.Collections.Generic;
using Voltage;

namespace Voltage.Editor.SerializedData
{
	//TODO: Remove once we implement auto generation of Component Data
    public static class ComponentDataTypeRegistrator
    {
        public static readonly Dictionary<string, Type> DataTypes = new()
        {
            #region Voltage Components

            // Lights
            { typeof(DirLight.DirLightComponentData).FullName, typeof(DirLight.DirLightComponentData) },
            { typeof(PointLight.PointLightComponentData).FullName, typeof(PointLight.PointLightComponentData) },
            { typeof(SpotLight.SpotLightComponentData).FullName, typeof(SpotLight.SpotLightComponentData) },
            { typeof(AreaLight.AreaLightComponentData).FullName, typeof(AreaLight.AreaLightComponentData) },
            
            // Camera
            { typeof(CameraShake.CameraShakeComponentData).FullName, typeof(CameraShake.CameraShakeComponentData) },

            // Physics - Colliders
            { typeof(Collider.ColliderComponentData).FullName, typeof(Collider.ColliderComponentData) },
            { typeof(ArcadeRigidbody.ArcadeRigidbodyComponentData).FullName, typeof(ArcadeRigidbody.ArcadeRigidbodyComponentData) },

            // Renderables
            { typeof(SpriteRenderer.SpriteRendererComponentData).FullName, typeof(SpriteRenderer.SpriteRendererComponentData) },
            { typeof(TiledMapRenderer.TiledMapRendererComponentData).FullName, typeof(TiledMapRenderer.TiledMapRendererComponentData) },
            { typeof(SpriteAnimator.SpriteAnimatorComponentData).FullName, typeof(SpriteAnimator.SpriteAnimatorComponentData) },

            // Entity
            { typeof(EntityData).FullName, typeof(EntityData) },

            // Prefab Data
            { typeof(PrefabData).FullName, typeof(PrefabData) },

            #endregion

            // #region Jolt Components
            // { typeof(PlayerData.PlayerDataComponentData).FullName, typeof(PlayerData.PlayerDataComponentData) },
            // { typeof(PlatformData.PlatformDataComponentData).FullName, typeof(PlatformData.PlatformDataComponentData) },
            // { typeof(ParallaxSprite.ParallaxSpriteComponentData).FullName, typeof(ParallaxSprite.ParallaxSpriteComponentData) },
			// { typeof(CameraFollow.CameraFollowComponentData).FullName, typeof(CameraFollow.CameraFollowComponentData) },
            // #endregion
        };
    }
}
