using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Voltage.Data;
using Voltage.DeferredLighting;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.Extensions;
using Voltage.Editor.FilePickers;
using Voltage.Editor.ProjectFile;
using Voltage.Editor.SerializedData;
using Voltage.Editor.Undo.AssetActions;
using Voltage.Editor.Undo.Core;
using Voltage.Editor.Utils;
using Voltage.Editor.Windows;
using Voltage.Sprites;
using Voltage.Textures;
using Voltage.Tiled;
using Voltage.Utils;
using Voltage.Utils.Extensions;

namespace Voltage.Editor.SceneFile;

public class GameScene : SceneComponent
{
    public TmxMap TiledMap;
    public Entity TiledMapEntity;// Create a dedicated TiledMapEntity to hold TiledMap
    public List<Entity> TmxMapEntities = new List<Entity>(); // Colliders + Images
    public DeferredLightingRenderer DeferredRenderer;
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    private Dictionary<string, SceneData.SceneEntityData> sceneEntitiesByName;

    public ProjectSettings Project;

	//TODO: Make this accessible in Editor (and not hardcoded)
	// Everything under 100 (light layer) is render layer ( -99 to 99 inclusive)
	private static readonly int[] AllRenderLayers = Enumerable.Range(-99, 199).ToArray();


    public GameScene()
    {
	    Debug.Warn("Game Scene component was added!");
	    
        sceneEntitiesByName = new Dictionary<string, SceneData.SceneEntityData>();

        var projectManager = Core.GetGlobalManager<ProjectManager>();
        
		if (projectManager?.HasActiveProject == true)
        {
            var designRes = projectManager.CurrentProject.Settings.DesignResolution;
            Scene.SetDesignResolution(
                designRes.Width, 
                designRes.Height, 
                designRes.ResolutionPolicy,
                designRes.HorizontalBleed,
                designRes.VerticalBleed
            );

            Project = projectManager.CurrentProject.Settings;
        }
		else
        {
            Scene.SetDesignResolution(1280, 720, Scene.SceneResolutionPolicy.BestFit);
        }

        LoadSceneData();

        // //FMOD 
        // FmodStudio = new FmodStudio(false);
        // //  Load the master bank first
        // FmodStudio.LoadBank("Content/FmodAudio/Build/Desktop/Master.bank", null);
        // //  Load the master strings bank so that event, vca, bus, etc paths work correctly
        // FmodStudio.LoadBank("Content/FmodAudio/Build/Desktop/Master.strings.bank", null);
        // //FmodStudio.PlayMusic("event:/MainTheme", true);
    }

    public override void OnEnabled()
    {
	    base.OnEnabled();
	    SceneGraphWindow.OnTmxFileSelected += CreateTiledMap;
	    SceneGraphWindow.OnAsepriteImageSelected += LoadAsepriteImages;//TODO: Fix the rendering layer assignment
    
	    Scene.OnFinishedAddingEntities += LoadSceneEntitiesData;
    }

    public override void Update()
    {
        base.Update();
    }

    public override void OnDisabled()
    {
	    base.OnDisabled();
	    SceneGraphWindow.OnTmxFileSelected -= CreateTiledMap;
	    SceneGraphWindow.OnAsepriteImageSelected -= LoadAsepriteImages;

	    Scene.OnFinishedAddingEntities -= LoadSceneEntitiesData;
    }

    #region Entity Registration and Creation
    /// <summary>
    /// Creates a simple entity and adds it to the scene.
    /// </summary>
    protected void CreateEntity(out Entity entity, string customName = null)
    {
        var newEntity = new Entity(customName ?? "Entity");
        newEntity.Name = Scene.GetUniqueEntityName(newEntity.Name, newEntity);
        Scene.AddEntity(newEntity);
        entity = newEntity;
    }
    #endregion

    #region Load Data Functions
    //Creates the SceneData object
    protected virtual void LoadSceneData()
    {
        var sceneJsonPath = $"{ProjectManager.Instance.CurrentProject.ScenesFolder}/{GetType().Name}.vscene";

        // Create default SceneData and save it
        if (!File.Exists(sceneJsonPath))
        {
            Scene.SceneData = new SceneData();
            var json = Voltage.Persistence.Json.ToJson(Scene.SceneData, true);
            Directory.CreateDirectory(Path.GetDirectoryName(sceneJsonPath)!);
            File.WriteAllText(sceneJsonPath, json);
        }
        else
        {
	        Scene.SceneData = DataManager.Instance.LoadSceneData(ProjectManager.Instance.CurrentProject.ScenesFolder);
        }

        if (Scene.SceneData == null)
            throw new NullReferenceException(
                "SceneData is NULL. You need to create the JSON file for this scene first!");
    }

    //Assigns Transform components to each object in the scene
    protected virtual void LoadSceneEntitiesData()
    {
        sceneEntitiesByName = new(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < Scene.SceneData.Entities.Count; i++)
            sceneEntitiesByName[Scene.SceneData.Entities[i].Name] = Scene.SceneData.Entities[i];

        // Track entities that need parent assignment
        var entitiesNeedingParents = new List<Entity>();

        // NonSerialized entities (already in the scene)
        for (var i = 0; i < Scene.Entities.Count; i++)
        {
            if (Scene.Entities[i].Type != Entity.InstanceType.NonSerialized)
                continue;

            if (sceneEntitiesByName.TryGetValue(Scene.Entities[i].Name, out var sceneEntityData))
            {
				DataManager.Instance.LoadPredefinedEntityData(Scene.Entities[i], sceneEntityData);
                
                // Check if this entity needs parent assignment later
                if (!string.IsNullOrEmpty(Scene.Entities[i].GetData<string>("_PendingParentName")))
                    entitiesNeedingParents.Add(Scene.Entities[i]);
            }
        }

        // Serialized & SerializedPrefab entities (to be created now)
        foreach (var sceneEntity in Scene.SceneData.Entities)
        {
            if (sceneEntity.InstanceType == Entity.InstanceType.NonSerialized)
                continue;

            // Create entity directly
            var entity = new Entity(sceneEntity.Name);
            entity.Type = sceneEntity.InstanceType;
            Scene.AddEntity(entity);
            
            DataManager.Instance.LoadPredefinedEntityData(entity, sceneEntity);

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

            var parentEntity = Scene.FindEntity(parentName);
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
                Debug.Error($"Could not find parent entity '{parentName}' for entity '{entity.Name}'");
            }

            // Clean up the temporary data
            entity.RemoveData("_PendingParentName");
            entity.RemoveData("_PendingLocalPosition");
            entity.RemoveData("_PendingLocalRotation");
            entity.RemoveData("_PendingLocalScale");
        }
    }

	#endregion

	#region Editor Tools: Tiled Map & Aseprite Loading

	/// <summary>
	/// Creates colliders and images from a Tiled map file
	/// </summary>
	protected void CreateTiledMap(TmxFilePicker.TmxSelection tiledMapSelection)
    {
        var oldTmxEntities = new List<Entity>(TmxMapEntities);
        var oldTmxFileName = Scene.SceneData?.TiledMapFileName ?? "";
        var oldTiledMapEntity = TiledMapEntity;

        CreateEntity(out TiledMapEntity, "TiledMap");
        TiledMapEntity.Transform.Position = Vector2.Zero;
        TiledMapEntity.Type = Entity.InstanceType.Serialized;

        if (TmxMapEntities.Count > 0)
        {
            foreach (var entity in TmxMapEntities)
            {
                if (Scene.SceneData?.Entities != null)
                {
	                Scene.SceneData.Entities.RemoveAll(e => e.Name == entity.Name);
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
            TiledMap = Scene.Content.LoadTiledMap(tiledMapPath);
            TiledMapEntity.Position = Scene.Camera.Position;

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
                    CreateEntity(out var spriteEntity, image.Name);

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
            Scene.SceneData.TiledMapFileName = tiledMapPath;

            EditorChangeTracker.PushUndo(
                new TmxLoadUndoAction(
	                Scene,
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
            EditorDebug.Error($"Failed to load TMX file {tiledMapSelection.FilePath}: {ex.Message}");

            foreach (var entity in newEntities)
            {
                if (entity != null && entity.Scene == Scene)
                {
                    entity.Destroy();
                }
            }

            TmxMapEntities = oldTmxEntities;
            TiledMapEntity = oldTiledMapEntity;
            if (Scene.SceneData != null)
            {
	            Scene.SceneData.TiledMapFileName = oldTmxFileName;
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
            var asepriteFile = Scene.Content.LoadAsepriteFile(selection.FilePath);

            // Extract file name without extension and path (e.g. "MySprite.ase" -> "MySprite")
            var fileName = Path.GetFileNameWithoutExtension(selection.FilePath);

            // Parent Entity
            CreateEntity(out var parentEntity, fileName);
            parentEntity.Transform.Position = Scene.Camera.Transform.Position;
            parentEntity.Type = Entity.InstanceType.Serialized;

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
                    EditorDebug.Log($"Layer '{layerName}' not found in Aseprite file");
                    continue;
                }

                CreateEntity(out var spriteEntity, $"{layerName}");
                spriteEntity.Type = Entity.InstanceType.Serialized;

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
	                Scene,
                    createdEntities,
                    parentEntity,
                    selection.FilePath,
                    layersToLoad,
                    $"Load Aseprite: {fileName}"
                ),
                parentEntity,
                $"Load Aseprite: {fileName}"
            );

            EditorDebug.Log(
                $"Successfully loaded {createdEntities.Count - 1} layer(s) from {fileName}"
            );
        }
        catch (Exception ex)
        {
            EditorDebug.Log($"Failed to load Aseprite file: {ex.Message}");
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
                CreateEntity(out collisionEntity, baseName);
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
                CreateEntity(out collisionEntity, baseName);

                collisionEntity.Type = Entity.InstanceType.Serialized;
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
                CreateEntity(out collisionEntity, baseName);

                collisionEntity.Type = Entity.InstanceType.Serialized;
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