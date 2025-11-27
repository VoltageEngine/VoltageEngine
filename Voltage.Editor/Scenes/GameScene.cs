using Microsoft.Xna.Framework;
using Voltage.Data;
using Voltage.ECS;
using Voltage.Sprites;
using Voltage.Tiled;
using Voltage.Utils.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Voltage.DeferredLighting;
using Voltage.Textures;
using Voltage.Editor.Inspectors;
using Voltage.Editor.FilePickers;
using Voltage.Editor.SerializedData;
using Voltage.Editor.UndoActions;
using Voltage.Editor.Utils;
using Voltage.Utils;

namespace Voltage.Editor.Scenes;

public abstract class GameScene : Scene
{
    public TmxMap TiledMap;
    public Entity TiledMapEntity;// Create a dedicated TiledMapEntity to hold TiledMap
    public List<Entity> TmxMapEntities = new List<Entity>(); // Colliders + Images
    public DeferredLightingRenderer DeferredRenderer;
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    private Dictionary<string, SceneData.SceneEntityData> sceneEntitiesByName;

    // Everything under 100 (light layer) is render layer ( -99 to 99 inclusive)
    private static readonly int[] AllRenderLayers = Enumerable.Range(-99, 199).ToArray();


    public override void Initialize()
    {
        base.Initialize();

        EntityTypeRegistrator.RegisterAllEntityTypes();
        sceneEntitiesByName = new Dictionary<string, SceneData.SceneEntityData>();
        
        // Resolution settings
        SetDesignResolution(512, 288, SceneResolutionPolicy.BestFit);
        LoadSceneData();

        // //FMOD 
        // FmodStudio = new FmodStudio(false);
        // //  Load the master bank first
        // FmodStudio.LoadBank("Content/FmodAudio/Build/Desktop/Master.bank", null);
        // //  Load the master strings bank so that event, vca, bus, etc paths work correctly
        // FmodStudio.LoadBank("Content/FmodAudio/Build/Desktop/Master.strings.bank", null);
        // //FmodStudio.PlayMusic("event:/MainTheme", true);
    }

    public override void Begin()
    {
        base.Begin();

        SceneGraphWindow.OnTmxFileSelected += CreateTiledMap;
        SceneGraphWindow.OnAsepriteImageSelected += LoadAsepriteImages;//TODO: Fix the rendering layer assignment
    
        OnFinishedAddingEntities += LoadSceneEntitiesData;
        EntityFactoryRegistry.OnEntityCreated += CreateEntityWithParams;
        EntityFactoryRegistry.OnDataLoadingStarted += DataLoader.LoadPredefinedEntityData;
    }

    public override void Update()
    {
        base.Update();
    }

    public override void End()
    {
        base.End();

        SceneGraphWindow.OnTmxFileSelected -= CreateTiledMap;
        SceneGraphWindow.OnAsepriteImageSelected -= LoadAsepriteImages;

        OnFinishedAddingEntities -= LoadSceneEntitiesData;
        EntityFactoryRegistry.OnEntityCreated -= CreateEntityWithParams;
        EntityFactoryRegistry.OnDataLoadingStarted -= DataLoader.LoadPredefinedEntityData;
    }

    #region Entity Registration and Creation
    /// <summary>
    /// Whenever an entity is created, this method is called to assign initial parameters to it.
    /// </summary>
    /// <param name="entity"></param>
    public void CreateEntityWithParams(Entity entity)
    {
		AddEntity(entity);
    }

    protected void CreateEntity(string entityType, out Entity entity, string customName = null)
    {
        Entity newEntity = null; // must be assigned default value
        if (EntityFactoryRegistry.TryCreate(entityType, out newEntity))
        {
            if (customName != null)
                newEntity.Name = GetUniqueEntityName(customName, newEntity);
            else
                newEntity.Name = GetUniqueEntityName(entityType, newEntity);

            CreateEntityWithParams(newEntity);
        }
        entity = newEntity;
    }
    #endregion

    #region Load Data Functions
    //Creates the SceneData object
    protected virtual void LoadSceneData()
    {
        var sceneJsonPath = $"Content/Data/Scene/{GetType().Name}.json";

        // Create default SceneData and save it
        if (!File.Exists(sceneJsonPath))
        {
            SceneData = new SceneData();
            var json = Voltage.Persistence.Json.ToJson(SceneData, true);
            Directory.CreateDirectory(Path.GetDirectoryName(sceneJsonPath)!);
            File.WriteAllText(sceneJsonPath, json);
        }
        else
        {
            SceneData = DataLoader.LoadSceneData(this);
        }

        if (SceneData == null)
            throw new NullReferenceException(
                "SceneData is NULL. You need to create the JSON file for this scene first!");
    }

    //Assigns Transform components to each object in the scene
    protected virtual void LoadSceneEntitiesData()
    {
        sceneEntitiesByName = new(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < SceneData.Entities.Count; i++)
            sceneEntitiesByName[SceneData.Entities[i].Name] = SceneData.Entities[i];

        // Track entities that need parent assignment
        var entitiesNeedingParents = new List<Entity>();

        // HardCoded entities (already in the scene)
        for (var i = 0; i < Entities.Count; i++)
        {
            if (Entities[i].Type != Entity.InstanceType.HardCoded)
                continue;

            if (sceneEntitiesByName.TryGetValue(Entities[i].Name, out var sceneEntityData))
            {
                DataLoader.LoadPredefinedEntityData(Entities[i], sceneEntityData);
                
                // Check if this entity needs parent assignment later
                if (!string.IsNullOrEmpty(Entities[i].GetData<string>("_PendingParentName")))
                    entitiesNeedingParents.Add(Entities[i]);
            }
        }

        // Dynamic & Prefab entities (to be created now)
        foreach (var sceneEntity in SceneData.Entities)
        {
            if (string.IsNullOrEmpty(sceneEntity.EntityType))
            {
                Debug.Log(Debug.LogType.Error, $"EntityType is null or empty for entity: {sceneEntity.Name}");
                continue;
            }

            if (sceneEntity.InstanceType == Entity.InstanceType.HardCoded)
                continue;

            CreateEntity(sceneEntity.EntityType, out var entity);

            if(entity == null)
                continue;

            entity.Type = sceneEntity.InstanceType;
            DataLoader.LoadPredefinedEntityData(entity, sceneEntity);

            // Check if this entity needs parent assignment later
            if (!string.IsNullOrEmpty(entity.GetData<string>("_PendingParentName")))
                entitiesNeedingParents.Add(entity);
        }

        AssignParentRelationships(entitiesNeedingParents);
    }

    /// <summary>
    /// Assigns parent relationships to entities after all entities have been loaded
    /// </summary>
    private void AssignParentRelationships(List<Entity> entitiesNeedingParents)
    {
        foreach (var entity in entitiesNeedingParents)
        {
            var parentName = entity.GetData<string>("_PendingParentName");
            if (string.IsNullOrEmpty(parentName))
                continue;

            var parentEntity = FindEntity(parentName);
            if (parentEntity != null)
            {
                var savedLocalPosition = entity.GetData<Vector2>("_PendingLocalPosition");
                var savedLocalRotation = entity.GetData<float>("_PendingLocalRotation");
                var savedLocalScale = entity.GetData<Vector2>("_PendingLocalScale");

                // Set parent FIRST
                entity.Transform.SetParent(parentEntity.Transform);

                // THEN set the local transform values (not world values)
                entity.Transform.SetLocalPosition(savedLocalPosition);
                entity.Transform.SetLocalRotation(savedLocalRotation);
                entity.Transform.SetLocalScale(savedLocalScale);
            }
            else
            {
                Debug.Log(Debug.LogType.Error,
                    $"Could not find parent entity '{parentName}' for entity '{entity.Name}'");
            }

            // Clean up the temporary data
            entity.RemoveData("_PendingParentName");
            entity.RemoveData("_PendingLocalPosition");
            entity.RemoveData("_PendingLocalRotation");
            entity.RemoveData("_PendingLocalScale");
        }
    }

    #endregion

    #region EDITOR TOOLS

    /// <summary>
    /// Creates colliders and images from a Tiled map file
    /// </summary>
    protected void CreateTiledMap(TmxFilePicker.TmxSelection tiledMapSelection)
    {
        var oldTmxEntities = new List<Entity>(TmxMapEntities);
        var oldTmxFileName = SceneData?.TiledMapFileName ?? "";
        var oldTiledMapEntity = TiledMapEntity;

        CreateEntity("TiledMapEntity", out TiledMapEntity, "TiledMap");
        TiledMapEntity.Transform.Position = Vector2.Zero;
        TiledMapEntity.Type = Entity.InstanceType.Dynamic;

        if (TmxMapEntities.Count > 0)
        {
            foreach (var entity in TmxMapEntities)
            {
                if (SceneData?.Entities != null)
                {
                    SceneData.Entities.RemoveAll(e => e.Name == entity.Name);
                }
                entity.Destroy();
            }

            TmxMapEntities.Clear();
            TmxMapEntities = new List<Entity>();
        }

        var newEntities = new List<Entity> { TiledMapEntity };

        try
        {
            var tiledMapPath = tiledMapSelection.FilePath;
            TiledMap = Content.LoadTiledMap(tiledMapPath);
            TiledMapEntity.Position = Camera.Position;

            if (tiledMapSelection.LoadColliders)
                LoadLevelColliders();

            if (tiledMapSelection.ImageMode == TmxFilePicker.ImageLoadMode.BakedLayers)
            {
                var renderer = new TiledMapRenderer(TiledMap, null, false);
                renderer.RenderLayer = tiledMapSelection.LayerToRenderTo;

                var rendererData = renderer.Data as TiledMapRenderer.TiledMapRendererComponentData;
                if (rendererData != null)
                {
                    rendererData.TiledMapPath = tiledMapPath;
                    renderer.Data = rendererData;
                }

                TiledMapEntity.ReplaceComponent(renderer);
            }

            if (tiledMapSelection.ImageMode == TmxFilePicker.ImageLoadMode.SeparateLayers)
            {
                foreach (var image in TiledMap.ImageLayers)
                {
                    CreateEntity("SpriteEntity", out var spriteEntity, image.Name);

                    var worldPosition = TiledMap.ToWorldPosition(new Vector2(image.OffsetX, image.OffsetY));
                    spriteEntity.Transform.SetParent(TiledMapEntity.Transform);
                    var localPosition = Vector2.Transform(worldPosition, TiledMapEntity.Transform.WorldToLocalTransform);
                    spriteEntity.Transform.SetLocalPosition(localPosition);

                    var spriteRenderer = spriteEntity.GetComponent<SpriteRenderer>();
                    if (spriteRenderer == null)
                    {
                        spriteRenderer = spriteEntity.AddComponent(new SpriteRenderer());
                    }

                    spriteRenderer.SetSprite(new Sprite(image.Image.Texture));

                    var componentData = spriteRenderer.Data as SpriteRenderer.SpriteRendererComponentData;
                    if (componentData != null)
                    {
                        componentData.SetTiledData(image.Name);
                        componentData.TextureFilePath = tiledMapPath;
                    }

                    TmxMapEntities.Add(spriteEntity);
                    newEntities.Add(spriteEntity);
                }
            }

            newEntities.AddRange(TmxMapEntities.Where(e => e.GetComponent<BoxCollider>() != null));
            SceneData.TiledMapFileName = tiledMapPath;

            EditorChangeTracker.PushUndo(
                new TmxLoadUndoAction(
                    this,
                    newEntities,
                    TiledMapEntity,
                    tiledMapPath,
                    oldTmxFileName,
                    oldTmxEntities,
                    $"Load TMX: {Path.GetFileName(tiledMapPath)}"
                ),
                TiledMapEntity,
                $"Load TMX: {Path.GetFileName(tiledMapPath)}"
            );
        }
        catch (Exception ex)
        {
            NotificationSystem.ShowTimedNotification($"Failed to load TMX file {tiledMapSelection.FilePath}: {ex.Message}");

            foreach (var entity in newEntities)
            {
                if (entity != null && entity.Scene == this)
                {
                    entity.Destroy();
                }
            }

            TmxMapEntities = oldTmxEntities;
            TiledMapEntity = oldTiledMapEntity;
            if (SceneData != null)
            {
                SceneData.TiledMapFileName = oldTmxFileName;
            }

            throw;
        }
    }

	//TODO: Add option for selecting layers in the Editor 
    private void LoadLevelColliders()
    {
        // Create level colliders
        var levelLedges = TiledMap.GetObjectGroup("Ledges").Objects;
        var levelColliders = TiledMap.GetObjectGroup("Colliders").Objects;
        // CreateLevelColliders(levelColliders.ToList(), (int)PhysicsLayers.Ground, "Collider(NonLedge)-");
        // CreateLevelColliders(levelLedges.ToList(), (int)PhysicsLayers.Ledge, "Collider(Ledge)-");
    }

    /// <summary>
    /// Loads Aseprite images and creates sprite entities for each selected layer
    /// </summary>
    private void LoadAsepriteImages(AsepriteFilePicker.AsepriteSelection selection)
    {
        if (selection == null || string.IsNullOrEmpty(selection.FilePath))
        {
            NotificationSystem.ShowTimedNotification("Invalid Aseprite selection");
            return;
        }

        try
        {
            var asepriteFile = Content.LoadAsepriteFile(selection.FilePath);

            // Extract file name without extension and path (e.g. "MySprite.ase" -> "MySprite")
            var fileName = Path.GetFileNameWithoutExtension(selection.FilePath);

            // Parent Entity
            CreateEntity("Entity", out var parentEntity, fileName);
            parentEntity.Transform.Position = Camera.Transform.Position;
            parentEntity.Type = Entity.InstanceType.Dynamic;

            var createdEntities = new List<Entity> { parentEntity };

            // If no specific layers selected, use visible layers based on showHiddenLayers setting
            var layersToLoad = selection.LayerNames != null && selection.LayerNames.Count > 0
                ? selection.LayerNames
                : asepriteFile.Layers
                    .Where(l => selection.ShowHiddenLayers || l.IsVisible)
                    .Select(l => l.Name)
                    .ToList();

            int totalLayers = asepriteFile.Layers.Count;

            foreach (var layerName in layersToLoad)
            {
                // Find the matching layer in the Aseprite file
                var asepriteLayer = asepriteFile.Layers.FirstOrDefault(layer => layer.Name == layerName);
                if (asepriteLayer == null)
                {
                    NotificationSystem.ShowTimedNotification($"Layer '{layerName}' not found in Aseprite file");
                    continue;
                }

                CreateEntity("SpriteEntity", out var spriteEntity, $"{layerName}");
                spriteEntity.Type = Entity.InstanceType.Dynamic;

                spriteEntity.Transform.SetParent(parentEntity.Transform);
                var layerTexture = asepriteFile.GetTextureFromLayers(layerName);

                // Calculate local position based on layer offset (if available)
                // Aseprite layers don't have direct offsets, so we use (0,0) or can be adjusted manually
                var localPosition = Vector2.Zero;
                spriteEntity.Transform.SetLocalPosition(localPosition);

                var spriteRenderer = spriteEntity.GetComponent<SpriteRenderer>();
                if (spriteRenderer == null)
                {
                    spriteRenderer = spriteEntity.AddComponent(new SpriteRenderer());
                }

                // Calculate render layer based on Aseprite layer index
                // Lower indices in Aseprite = background layers (should render first)
                // Higher indices in Aseprite = foreground layers (should render last)
                int layerIndex = asepriteFile.Layers.IndexOf(asepriteLayer);

                // Map Aseprite layer index to render layer
                // Background layers get lower render layers, foreground get higher
                // We'll map them to a range within RenderOrder.Entities
                int renderLayer = AsepriteUtils.CalculateRenderLayerFromAsepriteIndex(layerIndex, totalLayers, selection.MinRenderingLayer, selection.MaxRenderingLayer);

                spriteRenderer.SetRenderLayer(renderLayer);
                spriteRenderer.SetSprite(new Sprite(layerTexture));

                var componentData = spriteRenderer.Data as SpriteRenderer.SpriteRendererComponentData;
                if (componentData != null)
                {
                    componentData.SetAsepriteData(layerName, 0, true, false);
                    componentData.TextureFilePath = selection.FilePath;
                }

                createdEntities.Add(spriteEntity);
            }

            EditorChangeTracker.PushUndo(
                new AsepriteLoadUndoAction(
                    this,
                    createdEntities,
                    parentEntity,
                    selection.FilePath,
                    layersToLoad,
                    $"Load Aseprite: {fileName}"
                ),
                parentEntity,
                $"Load Aseprite: {fileName}"
            );

            NotificationSystem.ShowTimedNotification(
                $"Successfully loaded {createdEntities.Count - 1} layer(s) from {fileName}"
            );
        }
        catch (Exception ex)
        {
            NotificationSystem.ShowTimedNotification($"Failed to load Aseprite file: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates colliders from Tiled objects. Supports Box (rectangle), Circle (ellipse), and Polygon shaped colliders.
    /// </summary>
    protected void CreateLevelColliders(List<TmxObject> colliders, int layerMask, string name)
    {
        for (var i = 0; i < colliders.Count; i++)
        {
            var tmxObject = colliders[i];
            var baseName = $"{name}{i + 1}";
            Entity collisionEntity = null;

            //TODO: Fix the wrong offset positions for polygons
            if (tmxObject.ObjectType == TmxObjectType.Polygon &&
                tmxObject.Points != null &&
                tmxObject.Points.Length > 2)
            {
                CreateEntity("PolygonColliderEntity", out collisionEntity, baseName);
                collisionEntity.Transform.SetParent(TiledMapEntity.Transform);
                collisionEntity.Transform.SetLocalPosition(new Vector2(tmxObject.X, tmxObject.Y));

                var polygonCollider = new PolygonCollider(tmxObject.Points);
                polygonCollider.PhysicsLayer = layerMask;
                collisionEntity.ReplaceComponent(polygonCollider);

                // Add the calculated local offset to get correct final position
                var finalPosition = new Vector2(tmxObject.X - tmxObject.Width, tmxObject.Y - tmxObject.Height / 2f);
                collisionEntity.Transform.SetLocalPosition(finalPosition);
            }
            else if (tmxObject.ObjectType == TmxObjectType.Ellipse)
            {
                CreateEntity("CircleColliderEntity", out collisionEntity, baseName);

                collisionEntity.Type = Entity.InstanceType.Dynamic;
                collisionEntity.Transform.SetParent(TiledMapEntity.Transform);

                var centerX = tmxObject.X + tmxObject.Width * 0.5f;
                var centerY = tmxObject.Y + tmxObject.Height * 0.5f;
                collisionEntity.Transform.SetLocalPosition(new Vector2(centerX, centerY));

                // Use the average of width/height as radius for non-circular ellipses
                var radius = (tmxObject.Width + tmxObject.Height) * 0.25f;
                var circleCollider = new CircleCollider(radius);
                circleCollider.PhysicsLayer = layerMask;
                collisionEntity.ReplaceComponent(circleCollider);
            }
            else if (tmxObject.ObjectType == TmxObjectType.Basic) // Rectangle
            {
                CreateEntity("BoxColliderEntity", out collisionEntity, baseName);

                collisionEntity.Type = Entity.InstanceType.Dynamic;
                collisionEntity.Transform.SetParent(TiledMapEntity.Transform);

                var centerX = tmxObject.X + tmxObject.Width * 0.5f;
                var centerY = tmxObject.Y + tmxObject.Height * 0.5f;
                collisionEntity.Transform.SetLocalPosition(new Vector2(centerX, centerY));

                var boxCollider = new BoxCollider(tmxObject.Width, tmxObject.Height);
                boxCollider.PhysicsLayer = layerMask;
                collisionEntity.ReplaceComponent(boxCollider);
            }

            if (collisionEntity != null)
            {
                TmxMapEntities.Add(collisionEntity);
            }
        }
    }

    #endregion
}