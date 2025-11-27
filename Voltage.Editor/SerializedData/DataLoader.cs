using Voltage.Data;
using Voltage.Persistence;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using Microsoft.Xna.Framework;
using Voltage.ECS;
using Voltage.Utils.Extensions;
using PrefabData = Voltage.Data.PrefabData;
using Voltage.Editor.UndoActions;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.Interfaces;
using Voltage.Editor.Utils;

namespace Voltage.Editor.SerializedData;

public class DataLoader
{
    public bool HasExitedEditorMode = false;

    public DataLoader()
    {
       
    }

#if DEBUG
    public DataLoader(ImGuiManager imGuiManager)
    {
        _imGuiManager = imGuiManager;
        Core.Emitter.AddObserver(CoreEvents.SceneChanged, OnSceneChanged);
        Core.OnChangedToPlayMode += ChangedToPlayMode;
        _imGuiManager.OnSaveSceneAsync += SaveSceneChanges;
        _imGuiManager.OnPrefabCreated += OnPrefabCreated;
        _imGuiManager.OnPrefabLoadRequested += OnPrefabLoadRequested;
        _imGuiManager.OnLoadEntityData += OnLoadEntityData;
    }

    private ImGuiManager _imGuiManager;
    
    private void ChangedToPlayMode()
    {
        HasExitedEditorMode = true;
    }

    private void OnSceneChanged()
    {
        HasExitedEditorMode = false;
    }

    private async Task<bool> OnPrefabCreated(Entity prefabEntity, bool overrideExistingPrefab)
    {
        return await SavePrefabDataAsync(prefabEntity, overrideExistingPrefab);
    }

    private PrefabData OnPrefabLoadRequested(string prefabName)
    {
        var prefabData = LoadPrefabData(prefabName);
        if (prefabData.HasValue)
        {
            return prefabData.Value;
        }
        return new PrefabData();
    }

    private void OnLoadEntityData(Entity entity, object entityData)
    {
        if (entityData is SceneData.SceneEntityData sceneEntityData)
        {
            LoadPredefinedEntityData(entity, sceneEntityData);
        }
        else if (entityData is PrefabData prefabData)
        {
            LoadPrefabEntityData(entity, prefabData);
        }
    }
#endif

    #region Load Methods

    public static SceneData LoadSceneData(Voltage.Scene scene)
    {
        var outputFile = $"Content/Data/Scene/{scene.GetType().Name}.json";
        var outputExists = File.Exists(outputFile);
        var filePath = string.Empty;

        if (outputExists)
        {
            filePath = File.ReadAllText($"Content/Data/Scene/{scene.GetType().Name}.json");
        }
        else
        {
#if DEBUG
             NotificationSystem.ShowTimedNotification(
                $"The JSON file for this {scene.GetType().Name} scene hasn't been created yet. Create it by saving the scene");
#endif
            return null;
        }

        try
        {
            var sceneData = Json.FromJson<SceneData>(filePath);
            return sceneData;
        }
        catch (SerializationException args)
        {
            throw new SerializationException($"Failed to serialize JSON file: " +
                                             $"'Content/Data/Scene/{scene.GetType().Name}.json' into SceneData!", args);
        }

    }

    #endregion

    #region Save Methods

#if DEBUG
    
    private async Task SaveSceneChanges()
    {
        await SaveSceneDataAsync(Core.Scene, HasExitedEditorMode);

        if (HasExitedEditorMode)
            NotificationSystem.ShowTimedNotification(
                $"WARNING. Only saved EntityData, without Transform and Component data. Must Reset the Scene to save current state!");
        else
            NotificationSystem.ShowTimedNotification($"Successfully saved {Core.Scene.GetType().Name} data");

        EditorChangeTracker.ClearOnSave();
    }

    public static async Task SaveSceneDataAsync(Voltage.Scene scene, bool ignoreEntityTransform)
    {
        var sourceFilePath = $"{Environment.CurrentDirectory}/Content/Data/Scene/{scene.GetType().Name}.json";
        var outputFilePath = $"Content/Data/Scene/{scene.GetType().Name}.json";

        SceneData oldSceneData = null;
        var oldDataExists = File.Exists(sourceFilePath);
                
        if (oldDataExists)
        {
            try
            {
                oldSceneData = Json.FromJson<SceneData>(File.ReadAllText(sourceFilePath));
            }
            catch
            {
                oldSceneData = null;
            }
        }

        var newSceneData = new SceneData();

        if (scene.SceneData != null)
            newSceneData.TiledMapFileName = scene.SceneData.TiledMapFileName;
        else if (oldSceneData != null)
            newSceneData.TiledMapFileName = oldSceneData.TiledMapFileName;

        var oldEntitiesByName = new Dictionary<string, SceneData.SceneEntityData>(StringComparer.OrdinalIgnoreCase);

        if (oldSceneData?.Entities != null)
        {
            foreach (var entity in oldSceneData.Entities)
                oldEntitiesByName[entity.Name] = entity;
        }

        for (int i = 0; i < scene.Entities.Count; i++)
        {
            var entity = scene.Entities[i];
            SceneData.SceneEntityData oldEntityData;  
            var hasOldData = oldEntitiesByName.TryGetValue(entity.Name, out oldEntityData);

            Vector2 positionToSave;
            float rotationToSave;
            Vector2 scaleToSave;
            
            if (ignoreEntityTransform && hasOldData)
            {
                positionToSave = oldEntityData.Position;
                rotationToSave = oldEntityData.Rotation;
                scaleToSave = oldEntityData.Scale;
            }
            else
            {
                // Use current transform values (either in Edit Mode or no old data exists)
                if (entity.Transform.Parent != null)
                {
                    // Entity has a parent - save LOCAL transform values
                    positionToSave = entity.Transform.LocalPosition;
                    rotationToSave = entity.Transform.LocalRotation;
                    scaleToSave = entity.Transform.LocalScale;
                }
                else
                {
                    // Entity has no parent - save WORLD transform values
                    positionToSave = entity.Transform.Position;
                    rotationToSave = entity.Transform.Rotation;
                    scaleToSave = entity.Transform.Scale;
                }
            }

            var sceneEntityData = new SceneData.SceneEntityData
            {
                InstanceType = entity.Type,
                Name = entity.Name,
                EntityType = entity.GetType().Name,
                // Use the determined transform values
                Position = positionToSave,
                Rotation = rotationToSave,
                Scale = scaleToSave,
                
                // Save parent entity name
                ParentEntityName = ignoreEntityTransform && hasOldData 
                    ? oldEntityData.ParentEntityName 
                    : entity.Transform.Parent?.Entity?.Name,
    
                Enabled = entity.Enabled,
                UpdateOrder = entity.UpdateOrder,
                Tag = entity.Tag,
                IsSelectableInEditor = entity.IsSelectableInEditor,
                DebugRenderEnabled = entity.DebugRenderEnabled,
                OriginalPrefabName = entity.OriginalPrefabName
            };

            // Get entity data using the GetEntityData method
            var entityData = entity.GetEntityData();
            
            // Component data handling based on PlayMode
            if (ignoreEntityTransform && hasOldData && oldEntityData.EntityData != null)
            {
                // Use old data as base, but update ONLY IPlayModeSaveableComponent components
                var oldEntityDataClone = oldEntityData.EntityData.Clone();
                
                if (entityData != null)
                {
                    var updatedComponentDataList = new List<ComponentDataEntry>();
                    
                    if (oldEntityDataClone.ComponentDataList != null)
                    {
                        foreach (var oldEntry in oldEntityDataClone.ComponentDataList)
                        {
                            var isSaveableComponent = ComponentDataTypeRegistrator.DataTypes.TryGetValue(oldEntry.ComponentTypeName, out var componentType) && 
                                                     typeof(IPlayModeSaveableComponent).IsAssignableFrom(componentType);
                
                            if (!isSaveableComponent)
                            {
                                updatedComponentDataList.Add(oldEntry);
                            }
                        }
                    }
                    
                    // Then, add current IPlayModeSaveableComponent components (update them)
                    foreach (var component in entity.Components)
                    {
                        if (component.Data != null)
                        {
                            // Check if this component implements IPlayModeSaveableComponent
                            var isSaveableComponent = component is IPlayModeSaveableComponent;
                
                            if (isSaveableComponent)
                            {
                                var componentJsonSettings = new JsonSettings
                                {
                                    PrettyPrint = true,
                                    TypeNameHandling = TypeNameHandling.Auto,
                                    PreserveReferencesHandling = false
                                };
                                
                                var json = Json.ToJson(component.Data, componentJsonSettings);
                                updatedComponentDataList.Add(new ComponentDataEntry
                                {
                                    ComponentTypeName = component.GetType().FullName,
                                    ComponentName = component.Name,
                                    DataTypeName = component.Data.GetType().FullName,
                                    Json = json
                                });
                            }
                        }
                    }
                    
                    oldEntityDataClone.ComponentDataList = updatedComponentDataList;
                }
                
                sceneEntityData.EntityData = oldEntityDataClone;
            }
            else
            {
                // Edit Mode: Save all component data as before
                entityData.ComponentDataList.Clear();
                
                foreach (var component in entity.Components)
                {
                    if (component.Data != null)
                    {
                        var componentJsonSettings = new JsonSettings
                        {
                            PrettyPrint = true,
                            TypeNameHandling = TypeNameHandling.Auto,
                            PreserveReferencesHandling = false
                        };
                        
                        var json = Json.ToJson(component.Data, componentJsonSettings);
                        entityData.ComponentDataList.Add(new ComponentDataEntry
                        {
                            ComponentTypeName = component.GetType().FullName,
                            ComponentName = component.Name,
                            DataTypeName = component.Data.GetType().FullName,
                            Json = json
                        });
                    }
                }
                
                sceneEntityData.EntityData = entityData;
            }

            newSceneData.Entities.Add(sceneEntityData);
        }

        var settings = new JsonSettings
        {
            PrettyPrint = true,
            TypeNameHandling = TypeNameHandling.Auto,
            PreserveReferencesHandling = false
        };

        try
        {
            var jsonData = Json.ToJson(newSceneData, settings);
            await File.WriteAllTextAsync(sourceFilePath, jsonData);
            await File.WriteAllTextAsync(outputFilePath, jsonData);
        }
        catch (ArgumentException ex)
        {
            throw new Exception($"Failed to save scene {scene.GetType().Name}", ex);
        }
    }
#endif


    #endregion
    
    public static void LoadPredefinedEntityData(Entity newEntity, SceneData.SceneEntityData entityData)
    {
        newEntity.Name = entityData.Name;
        newEntity.SetTag(entityData.Tag);
        newEntity.Enabled = entityData.Enabled;
        newEntity.UpdateOrder = entityData.UpdateOrder;
        newEntity.DebugRenderEnabled = entityData.DebugRenderEnabled;
        newEntity.Type = entityData.InstanceType;

        if(newEntity.Type == Entity.InstanceType.Prefab)
            newEntity.OriginalPrefabName = entityData.OriginalPrefabName;
        else
            newEntity.OriginalPrefabName = null;

        // Handle transform and parent assignment
        if (!string.IsNullOrEmpty(entityData.ParentEntityName))
        {
            var parentEntity = newEntity.Scene?.FindEntity(entityData.ParentEntityName);
            if (parentEntity != null)
            {
                newEntity.Transform.SetParent(parentEntity.Transform);
                newEntity.Transform.SetLocalPosition(entityData.Position);
                newEntity.Transform.SetLocalRotation(entityData.Rotation);
                newEntity.Transform.SetLocalScale(entityData.Scale);
            }
            else
            {
                newEntity.SetData("_PendingParentName", entityData.ParentEntityName);
                newEntity.SetData("_PendingLocalPosition", entityData.Position);
                newEntity.SetData("_PendingLocalRotation", entityData.Rotation);
                newEntity.SetData("_PendingLocalScale", entityData.Scale);
            }
        }
        else
        {
            newEntity.Transform.Position = entityData.Position;
            newEntity.Transform.Rotation = entityData.Rotation;
            newEntity.Transform.Scale = entityData.Scale;
        }

        if (entityData.EntityData != null)
        {
            // Deserialize the correct type from the JSON
            var entityDataType = entityData.EntityData.GetType();
            var json = Json.ToJson(entityData.EntityData, true);
            var deserializedEntityData = (EntityData)Json.FromJson(json, entityDataType);

            // Deep clone ComponentDataList to avoid shared references
            if (deserializedEntityData.ComponentDataList != null)
            {
                deserializedEntityData.ComponentDataList = deserializedEntityData.ComponentDataList
                    .Select(CloneComponentDataEntry)
                    .ToList();
            }

            // Set the EntityData directly on the entity (not as a component)
            newEntity.SetEntityData(deserializedEntityData);

            // Track components that have already been processed
            var processedComponents = new HashSet<string>();

            // Assign data to already existing components (for HardCoded entities)
            foreach (var comp in newEntity.ComponentsToAdd)
            {
                if (TryAssignComponentDataFromEntityData(newEntity, comp))
                {
                    // Mark this component as processed using a unique identifier
                    var componentId = $"{comp.GetType().FullName}:{comp.Name}";
                    processedComponents.Add(componentId);
                }
            }

            // Register callback for other components that might be added later
            // But skip components that were already processed
            newEntity.OnComponentAdded<Component>(comp =>
            {
                var componentId = $"{comp.GetType().FullName}:{comp.Name}";
                
                // Only process if we haven't already processed this component
                if (!processedComponents.Contains(componentId))
                {
                    TryAssignComponentDataFromEntityData(newEntity, comp);
                }
            });
        }
    }

    /// <summary>
    /// Helper to deep-clone a ComponentDataEntry
    /// </summary>
    private static ComponentDataEntry CloneComponentDataEntry(ComponentDataEntry entry)
    {
        // Serialize and deserialize to ensure a deep copy
        var json = Json.ToJson(entry, true);
        return Json.FromJson<ComponentDataEntry>(json);
    }

    /// <summary>
    /// Loads component data when a component is added to an entity
    /// </summary>
    /// <returns>True if component data was found and applied, false otherwise</returns>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(List<SceneData.SceneEntityData>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(ComponentData))]

    private static bool TryAssignComponentDataFromEntityData(Entity entity, Component component)
    {
        var entityData = entity.GetEntityData(); // Get EntityData directly from entity

        if (entityData == null || entityData.ComponentDataList == null)
            return false;

        for (int i = entityData.ComponentDataList.Count - 1; i >= 0; i--)
        {
            var entry = entityData.ComponentDataList[i];

            // IMPORTANT: Make sure if you have a custom name for the component, you assign it before AddComponent() is called!!!
            if (component.Name == entry.ComponentName)
            {
                var dataType = ComponentDataTypeRegistrator.DataTypes.TryGetValue(entry.DataTypeName, out var t)
                    ? t
                    : null;

                if (dataType != null)
                {
                    try
                    {
                        var data = (ComponentData)Json.FromJson(entry.Json, dataType);
                        component.Data = data;

                        // Remove the processed entry
                        entityData.ComponentDataList.RemoveAt(i);

                        // Update the entity's data
                        entity.SetEntityData(entityData);

                        return true; // Successfully processed
                    }
                    catch (Exception ex)
                    {
                        Debug.Error($"Error loading component data for {component.Name}: {ex.Message}");
                        return false;
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Component type '{entry.DataTypeName}' is not registered in ComponentDataTypeRegistrator.DataTypes. " +
                                                        $"Please add it to the registry to support serialization/deserialization.");
                }
            }
        }

        return false; // No matching component data found
    }


    #region Prefab Methods

#if DEBUG
    /// <summary>
    /// Saves a prefab entity to a JSON file, organized into subdirectories by EntityType.
    /// </summary>
    public static async Task<bool> SavePrefabDataAsync(Entity prefabEntity, bool overrideExistingPrefab = false)
    {
        if (prefabEntity == null)
        {
            NotificationSystem.ShowTimedNotification("Entity is null");
            return false;
        }

        if (prefabEntity.Type != Entity.InstanceType.Prefab)
        {
            NotificationSystem.ShowTimedNotification("Entity is not a Prefab type");
            return false;
        }

        try
        {
            var sourceEntityTypeName = prefabEntity.GetType().Name;

            var prefabsDirectory = $"{Environment.CurrentDirectory}/Content/Data/Prefabs/{sourceEntityTypeName}";
            var outputDirectory = $"Content/Data/Prefabs/{sourceEntityTypeName}";
            Directory.CreateDirectory(prefabsDirectory);
            Directory.CreateDirectory(outputDirectory);

            var prefabFileName = !string.IsNullOrEmpty(prefabEntity.OriginalPrefabName)
                ? prefabEntity.OriginalPrefabName
                : prefabEntity.Name;

            var sourceFilePath = $"{prefabsDirectory}/{prefabFileName}.json";
            var outputFilePath = $"{outputDirectory}/{prefabFileName}.json";

            if (File.Exists(sourceFilePath) && !overrideExistingPrefab)
            {
				Debug.Error($"Prefab with name '{prefabEntity.Name}' already exists!");
                return false;
            }

            var prefabData = new PrefabData
            {
                InstanceType = prefabEntity.Type,
                Name = prefabEntity.Name,
                EntityType = sourceEntityTypeName,
                Rotation = prefabEntity.Transform.Rotation,
                Scale = prefabEntity.Transform.Scale,
                Enabled = prefabEntity.Enabled,
                UpdateOrder = prefabEntity.UpdateOrder,
                Tag = prefabEntity.Tag,
                DebugRenderEnabled = prefabEntity.DebugRenderEnabled
            };

            var entityData = prefabEntity.GetEntityData().Clone();

            entityData.ComponentDataList.Clear();

            foreach (var component in prefabEntity.ComponentsToAdd)
            {
                if (component.Data != null)
                {
                    var componentJsonSettings = new JsonSettings
                    {
                        PrettyPrint = true,
                        TypeNameHandling = TypeNameHandling.Auto,
                        PreserveReferencesHandling = false
                    };

                    var json = Json.ToJson(component.Data, componentJsonSettings);

                    entityData.ComponentDataList.Add(new ComponentDataEntry
                    {
                        ComponentTypeName = component.GetType().FullName,
                        ComponentName = component.Name,
                        DataTypeName = component.Data.GetType().FullName,
                        Json = json
                    });
                }
            }

            foreach (var component in prefabEntity.Components)
            {
                if (component.Data != null)
                {
                    var componentJsonSettings = new JsonSettings
                    {
                        PrettyPrint = true,
                        TypeNameHandling = TypeNameHandling.Auto,
                        PreserveReferencesHandling = false
                    };

                    var json = Json.ToJson(component.Data, componentJsonSettings);

                    entityData.ComponentDataList.Add(new ComponentDataEntry
                    {
                        ComponentTypeName = component.GetType().FullName,
                        ComponentName = component.Name,
                        DataTypeName = component.Data.GetType().FullName,
                        Json = json
                    });
                }
            }

            prefabData.ChildEntities.Clear();
            foreach (var child in prefabEntity.Transform.Children)
            {
                var childEntity = child.Entity;
                if (childEntity.Type != Entity.InstanceType.HardCoded)
                {
                    var childData = new SceneData.SceneEntityData
                    {
                        InstanceType = childEntity.Type,
                        Name = childEntity.Name,
                        EntityType = childEntity.GetType().Name,
                        Position = childEntity.Transform.LocalPosition,
                        Rotation = childEntity.Transform.LocalRotation,
                        Scale = childEntity.Transform.LocalScale,
                        ParentEntityName = prefabEntity.Name,
                        Enabled = childEntity.Enabled,
                        UpdateOrder = childEntity.UpdateOrder,
                        Tag = childEntity.Tag,
                        DebugRenderEnabled = childEntity.DebugRenderEnabled,
                        OriginalPrefabName = childEntity.OriginalPrefabName,
                        EntityData = childEntity.GetEntityData().Clone()
                    };
                    prefabData.ChildEntities.Add(childData);
                }
            }

            prefabData.EntityData = entityData;

            var settings = new JsonSettings
            {
                PrettyPrint = true,
                TypeNameHandling = TypeNameHandling.Auto,
                PreserveReferencesHandling = false
            };

            var jsonData = Json.ToJson(prefabData, settings);
            await File.WriteAllTextAsync(sourceFilePath, jsonData);
            await File.WriteAllTextAsync(outputFilePath, jsonData);

            return true;
        }
        catch (Exception ex)
        {
            Debug.Error($"Failed to save prefab {prefabEntity.Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Loads a prefab from JSON file, searching in EntityType subdirectories.
    /// </summary>
    public static PrefabData? LoadPrefabData(string prefabName)
    {
        try
        {
            var prefabsDirectory = "Content/Data/Prefabs";
            Directory.CreateDirectory(prefabsDirectory);

            var entityTypeDirectories = Directory.GetDirectories(prefabsDirectory);
            
            foreach (var entityTypeDir in entityTypeDirectories)
            {
                var prefabFilePath = Path.Combine(entityTypeDir, $"{prefabName}.json");
                
                if (File.Exists(prefabFilePath))
                {
                    var jsonContent = File.ReadAllText(prefabFilePath);
                    
                    if (string.IsNullOrWhiteSpace(jsonContent))
                    {
                        throw new Exception($"Prefab file is empty: {prefabFilePath}");
                    }

                    var prefabData = Json.FromJson<PrefabData>(jsonContent);
                    
                    if (prefabData.EntityType == null || prefabData.Name == null)
                    {
                        throw new Exception($"Invalid prefab data format for: {prefabName} - missing EntityType or Name");
                    }

                    return prefabData;
                }
            }
            
            throw new Exception($"Prefab file not found: {prefabName}");
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to load prefab {prefabName}: {ex.Message}");
        }
    }
#endif

    public static void LoadPrefabEntityData(Entity newEntity, PrefabData prefabData)
    {
        newEntity.Name = prefabData.Name;
        newEntity.SetTag(prefabData.Tag);
        newEntity.Enabled = prefabData.Enabled;
        newEntity.UpdateOrder = prefabData.UpdateOrder;
        newEntity.DebugRenderEnabled = prefabData.DebugRenderEnabled;

        newEntity.Transform.Rotation = prefabData.Rotation;
        newEntity.Transform.Scale = prefabData.Scale;

        if (prefabData.EntityData != null)
        {
            var entityDataType = prefabData.EntityData.GetType();
            var json = Json.ToJson(prefabData.EntityData, true);
            var deserializedEntityData = (EntityData)Json.FromJson(json, entityDataType);

            // Deep clone ComponentDataList to avoid shared references
            if (deserializedEntityData.ComponentDataList != null)
            {
                deserializedEntityData.ComponentDataList = deserializedEntityData.ComponentDataList
                    .Select(CloneComponentDataEntry)
                    .ToList();
            }

            newEntity.SetEntityData(deserializedEntityData);
            var processedComponents = new HashSet<string>();

            foreach (var comp in newEntity.ComponentsToAdd)
            {
                if (TryAssignComponentDataFromEntityData(newEntity, comp))
                {
                    var componentId = $"{comp.GetType().FullName}:{comp.Name}";
                    processedComponents.Add(componentId);
                }
            }

            newEntity.OnComponentAdded<Component>(comp =>
            {
                var componentId = $"{comp.GetType().FullName}:{comp.Name}";

                if (!processedComponents.Contains(componentId))
                {
                    TryAssignComponentDataFromEntityData(newEntity, comp);
                }
            });
        }

        // IMPORTANT: Only create Dynamic and Prefab children, skip HardCoded ones
        // HardCoded children should be instantiated by the entity's OnAddedToEntity() method
        if (prefabData.ChildEntities != null)
        {
            foreach (var childData in prefabData.ChildEntities)
            {
                if (childData.InstanceType == Entity.InstanceType.HardCoded)
                {
                    continue;
                }

                if (IsDuplicateChild(newEntity.Scene, childData))
                {
                    continue;
                }

                var childEntity = EntityFactoryRegistry.TryCreate(childData.EntityType, out var e) ? e : null;
                if (childEntity != null)
                {
                    EntityFactoryRegistry.InvokeEntityCreated(childEntity);
                    LoadPredefinedEntityData(childEntity, childData);
                    childEntity.Transform.SetParent(newEntity.Transform);
                    newEntity.Scene.AddEntity(childEntity);
                }
            }
        }
    }

    static bool IsDuplicateChild(Voltage.Scene scene, SceneData.SceneEntityData childData)
    {
        var existing = scene.FindEntity(childData.Name);
        if (existing != null && existing.Transform.Parent?.Entity?.Name == childData.ParentEntityName)
            return true;
        return false;
    }

    #endregion
}