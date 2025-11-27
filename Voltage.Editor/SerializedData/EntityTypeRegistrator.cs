using Voltage.ECS;

namespace Voltage.Editor.SerializedData
{
    public static class EntityTypeRegistrator
    {
        /// <summary>
        /// Add all the entity types to the registry.
        /// </summary>
        public static void RegisterAllEntityTypes()
        {
            void RegisterEntityType<T>() where T : Entity, new()
            {
                EntityFactoryRegistry.Register(typeof(T).Name, () => new T());
            }

            // Generic
            RegisterEntityType<Entity>();
            // RegisterEntityType<CameraBoundsEntity>();
            //
            // // Physics
            // RegisterEntityType<BoxColliderEntity>();
            // RegisterEntityType<PolygonColliderEntity>();
            // RegisterEntityType<CircleColliderEntity>();
            //
            // RegisterEntityType<SpriteEntity>();
            // RegisterEntityType<ParallaxSpriteEntity>();
            // RegisterEntityType<TiledMapEntity>();
            // RegisterEntityType<AnimatedEntity>();
            //
            // // PlayerEntity 
            // RegisterEntityType<PlayerEntity>();
            //
            // // Interactables
            // RegisterEntityType<PlatformEntity>();
            //
            // // Enemies
            // RegisterEntityType<TestDummyEntity>();
            //
            // // Lights
            // RegisterEntityType<DirectionalLightEntity>();
            // RegisterEntityType<PointLightEntity>();
            // RegisterEntityType<SpotLightEntity>();
            // RegisterEntityType<AreaLightEntity>();
        }
    }
}
