using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Voltage.Data;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.Interfaces;
using Voltage.Editor.ProjectFile;
using Voltage.Editor.SceneFile;
using Voltage.Editor.Undo.Core;
using Voltage.Editor.Utils;
using Voltage.Persistence;
using Voltage.Serialization;
using Voltage.Utils;
using Voltage.Utils.Extensions;
using PrefabData = Voltage.Data.PrefabData;

namespace Voltage.Editor.SerializedData;

/// <summary>
/// Manages all Scene/Entity/Prefab data loading and saving operations.
/// Central point for serialization/deserialization with clear separation of concerns.
/// </summary>
public class DataManager : GlobalManager
{
	private static DataManager _instance;

	public static DataManager Instance
	{
		get
		{
			if (_instance == null)
			{
				_instance = Core.GetGlobalManager<DataManager>();
				if (_instance == null)
				{
					_instance = new DataManager();
					Core.RegisterGlobalManager(_instance);
				}
			}
			return _instance;
		}
	}

	public bool HasExitedEditorMode { get; private set; } = false;

	#region Editor Camera Persistence

	// EditorData keys for persisting the editor camera state per-scene.
	// Stored in SceneData.EditorData (a Dictionary<string, string>) so the
	// camera entity stays NonSerialized and no duplicate is ever created.
	private const string kEditorCameraPosX = "EditorCamera.PositionX";
	private const string kEditorCameraPosY = "EditorCamera.PositionY";
	private const string kEditorCameraZoom = "EditorCamera.Zoom";
	private const string kEditorCameraRotation = "EditorCamera.Rotation";

	/// <summary>
	/// Snapshots the editor camera state into SceneData.EditorData.
	/// Only writes in Edit Mode so PlayMode camera changes are never persisted.
	/// Called automatically before scene serialization.
	/// </summary>
	private static void SaveEditorCameraState(Scene scene)
	{
		if (scene?.Camera == null || scene.SceneData == null)
			return;

		if (!Core.IsEditMode)
			return;

		var ed = scene.SceneData.EditorData;
		ed[kEditorCameraPosX] = scene.Camera.Position.X.ToString(CultureInfo.InvariantCulture);
		ed[kEditorCameraPosY] = scene.Camera.Position.Y.ToString(CultureInfo.InvariantCulture);
	}

	#endregion

	#region Events

	/// <summary>
	/// Invoked when a scene needs to be saved asynchronously.
	/// </summary>
	public event Func<Task> OnSaveSceneAsync;

	/// <summary>
	/// Invoked when a prefab is created and needs to be saved.
	/// </summary>
	public event Func<Entity, bool, Task<bool>> OnPrefabCreated;

	/// <summary>
	/// Invoked when a prefab needs to be loaded by name.
	/// </summary>
	public event Func<string, PrefabData> OnPrefabLoadRequested;

	/// <summary>
	/// Invoked when entity data needs to be loaded onto an entity.
	/// </summary>
	public event Action<Entity, object> OnLoadEntityData;

	#endregion

	public DataManager()
	{
		_instance = this;
		Core.Emitter.AddObserver(CoreEvents.SceneChanged, OnSceneChanged);
		Core.OnChangedToPlayMode += ChangedToPlayMode;

		OnSaveSceneAsync += SaveSceneChangesAsync;
		OnPrefabCreated += OnPrefabCreated;
		OnPrefabLoadRequested += OnPrefabLoadRequested;
		OnLoadEntityData += OnLoadEntityData;
	}

	#region Event Invokers

	public void InvokeSaveSceneChanges()
	{
		if (Core.Scene == null)
		{
			Debug.Error("No active scene to save!");
			return;
		}

		if (!SceneManager.Instance.HasLoadedScene)
		{
			Debug.Error("No Scene has been loaded yet!");
			return;
		}

		// Schedule the async save operation properly
		Core.Schedule(0f, false, this, async _ =>
		{
			try
			{
				if (OnSaveSceneAsync != null)
				{
					await OnSaveSceneAsync.Invoke();
					EditorDebug.Log("Save scene completed successfully");
				}
				else
				{
					Debug.Error("OnSaveSceneAsync has no subscribers!");
				}
			}
			catch (Exception ex)
			{
				Debug.Error($"Failed to save scene: {ex.Message}");
				Debug.Error($"Stack trace: {ex.StackTrace}");
			}
		});
	}

	/// <summary>
	/// Invokes the prefab creation event.
	/// </summary>
	public async Task<bool> InvokePrefabCreated(Entity prefabEntity, bool overrideExistingPrefab)
	{
		if (OnPrefabCreated != null)
		{
			return await OnPrefabCreated.Invoke(prefabEntity, overrideExistingPrefab);
		}
		return false;
	}

	/// <summary>
	/// Invokes the prefab load request event.
	/// </summary>
	public PrefabData InvokePrefabLoadRequested(string prefabName)
	{
		if (OnPrefabLoadRequested != null)
		{
			return OnPrefabLoadRequested.Invoke(prefabName);
		}
		return new PrefabData();
	}

	/// <summary>
	/// Invokes the entity data load event.
	/// </summary>
	public void InvokeLoadEntityData(Entity entity, object entityData)
	{
		OnLoadEntityData?.Invoke(entity, entityData);
	}

	#endregion

	#region Event Handlers

	private void ChangedToPlayMode()
	{
		HasExitedEditorMode = true;
	}

	private void OnSceneChanged()
	{
		HasExitedEditorMode = false;
	}

	#endregion

	#region Scene Save/Load Methods

	/// <summary>
	/// Saves the current scene changes.
	/// </summary>
	public async Task SaveSceneChangesAsync()
	{
		await SaveSceneDataAsync(Core.Scene, HasExitedEditorMode);

		if (HasExitedEditorMode)
		{
			EditorDebug.Log(
				"WARNING. Only saved EntityData, without Transform and Component data. Must Reset the Scene to save current state!");
			NotificationSystem.ShowTimedNotification("WARNING. Only saved EntityData, without Transform and Component data. Must Reset the Scene to save current state!");
		}
		else
		{
			EditorDebug.Log($"Successfully saved {Core.Scene.SceneData?.Name ?? "Scene"} data");
			NotificationSystem.ShowTimedNotification($"Successfully saved {Core.Scene.SceneData?.Name ?? "Scene"} data");
		}

		EditorChangeTracker.ClearOnSave();
	}

	/// <summary>
	/// Saves scene data to file asynchronously.
	/// </summary>
	public async Task SaveSceneDataAsync(Scene scene, bool ignoreEntityTransform)
	{
		ComponentDataSerializationBootstrap.EnsureInitialized();
		var filePath = SceneManager.Instance.CurrentScenePath;

		if (string.IsNullOrEmpty(filePath))
		{
			Debug.Error("Cannot save scene: No file path is set");
			throw new InvalidOperationException("Cannot save scene without a file path. Create a scene file first.");
		}

		// Validate the save target belongs to the current project to prevent
		// cross-project contamination when projects are switched.
		if (!ProjectManager.Instance.IsPathInCurrentProject(filePath))
		{
			Debug.Error($"Cannot save scene: target path '{filePath}' does not belong to the current project '{ProjectManager.Instance.CurrentProject?.ProjectName}'.");
			throw new InvalidOperationException(
				$"Scene save path mismatch. Target '{filePath}' is outside the current project. " +
				"This usually means the project was changed without clearing the scene state.");
		}

		// Snapshot editor camera into SceneData.EditorData before serializing.
		// Already guarded against PlayMode internally.
		SaveEditorCameraState(scene);

		SceneData oldSceneData = null;
		var oldDataExists = File.Exists(filePath);

		if (oldDataExists)
		{
			try
			{
				oldSceneData = Json.FromJson<SceneData>(File.ReadAllText(filePath));
			}
			catch
			{
				oldSceneData = null;
			}
		}

		var newSceneData = new SceneData();

		// Preserve scene metadata
		if (scene.SceneData != null)
		{
			newSceneData.Name = scene.SceneData.Name;
			newSceneData.FilePath = filePath;
			newSceneData.CreatedAt = scene.SceneData.CreatedAt;
			newSceneData.ModifiedAt = DateTime.Now;
			newSceneData.TiledMapFileName = scene.SceneData.TiledMapFileName;
			newSceneData.EditorData = scene.SceneData.EditorData;
		}
		else if (oldSceneData != null)
		{
			newSceneData.Name = oldSceneData.Name;
			newSceneData.FilePath = filePath;
			newSceneData.CreatedAt = oldSceneData.CreatedAt;
			newSceneData.ModifiedAt = DateTime.Now;
			newSceneData.TiledMapFileName = oldSceneData.TiledMapFileName;
			newSceneData.EditorData = oldSceneData.EditorData;
		}
		else
		{
			// Fallback if no existing data
			newSceneData.Name = Path.GetFileNameWithoutExtension(filePath);
			newSceneData.FilePath = filePath;
			newSceneData.CreatedAt = DateTime.Now;
			newSceneData.ModifiedAt = DateTime.Now;
		}

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
				Id = hasOldData && oldEntityData.Id != Guid.Empty ? oldEntityData.Id : Guid.NewGuid(),
				InstanceType = entity.Type,
				Name = entity.Name,
				Position = positionToSave,
				Rotation = rotationToSave,
				Scale = scaleToSave,
				ParentEntityName = ignoreEntityTransform && hasOldData
					? oldEntityData.ParentEntityName
					: entity.Transform.Parent?.Entity?.Name,
				ParentId = ignoreEntityTransform && hasOldData
					? oldEntityData.ParentId
					: (entity.Transform.Parent?.Entity != null && oldSceneData != null
						? oldSceneData.Entities.FirstOrDefault(e => string.Equals(e.Name, entity.Transform.Parent.Entity.Name, StringComparison.OrdinalIgnoreCase))?.Id
						: null),
				Enabled = entity.Enabled,
				UpdateOrder = entity.UpdateOrder,
				Tag = entity.Tag,
				IsSelectableInEditor = entity.IsSelectableInEditor,
				DebugRenderEnabled = entity.DebugRenderEnabled,
				OriginalPrefabName = entity.OriginalPrefabName
			};

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
							var isSaveableComponent = Type.GetType(oldEntry.ComponentTypeName) is { } componentType &&
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
						if (!component.IsSerialized)
							continue;

						if (component.Data != null)
						{
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
				// Edit Mode: Save only serialized component data
				if (entityData != null)
				{
					entityData.ComponentDataList.Clear();

					foreach (var component in entity.Components)
					{
						if (!component.IsSerialized)
							continue;

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
						else
						{
							entityData.ComponentDataList.Add(new ComponentDataEntry
							{
								ComponentTypeName = component.GetType().FullName,
								ComponentName = component.Name,
								DataTypeName = null,
								Json = null
							});
						}
					}

					sceneEntityData.EntityData = entityData;
				}
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
			await File.WriteAllTextAsync(filePath, jsonData);
			Debug.Log($"Scene saved successfully: {filePath}");
		}
		catch (Exception ex)
		{
			Debug.Error($"Failed to save scene: {ex.Message}");
			throw new Exception($"Failed to save scene {scene.SceneData?.Name ?? "Unknown"}", ex);
		}
	}

	/// <summary>
	/// Loads scene data from a .vscene file path.
	/// </summary>
	/// <param name="sceneFilePath">Full path to the .vscene file</param>
	/// <returns>Deserialized SceneData or null if failed</returns>
	public SceneData LoadSceneData(string sceneFilePath)
	{
		if (string.IsNullOrWhiteSpace(sceneFilePath))
		{
			Debug.Error("Scene file path cannot be null or empty");
			return null;
		}

		if (!File.Exists(sceneFilePath))
		{
			Debug.Error($"Scene file not found: {sceneFilePath}");
			return null;
		}

		try
		{
			var fileContent = File.ReadAllText(sceneFilePath);
			var sceneData = Json.FromJson<SceneData>(fileContent);
			
			if (sceneData == null)
			{
				Debug.Error($"Failed to deserialize scene data from: {sceneFilePath}");
				return null;
			}

			// Ensure FilePath is set correctly
			if (string.IsNullOrEmpty(sceneData.FilePath))
			{
				sceneData.FilePath = sceneFilePath;
			}

			Debug.Log($"Successfully loaded scene data: {sceneData.Name} from {sceneFilePath}");
			return sceneData;
		}
		catch (Exception ex)
		{
			Debug.Error($"Failed to load scene data from '{sceneFilePath}': {ex.Message}");
			Debug.Error($"Stack trace: {ex.StackTrace}");
			return null;
		}
	}

	/// <summary>
	/// Loads scene data by scene name (searches in the project's Scenes folder).
	/// </summary>
	/// <param name="sceneName">Name of the scene (without .vscene extension)</param>
	/// <returns>Deserialized SceneData or null if failed</returns>
	public SceneData LoadSceneDataByName(string sceneName)
	{
		if (string.IsNullOrWhiteSpace(sceneName))
		{
			Debug.Error("Scene name cannot be null or empty");
			return null;
		}

		if (!ProjectManager.Instance.HasActiveProject)
		{
			Debug.Error("No active project. Cannot load scene.");
			return null;
		}

		var sceneFilePath = Path.Combine(
			ProjectManager.Instance.CurrentProject.ScenesFolder,
			$"{sceneName}.vscene"
		);

		return LoadSceneData(sceneFilePath);
	}

	#endregion

	#region Entity Data Loading

	/// <summary>
	/// Loads predefined entity data onto an entity.
	/// </summary>
	public void LoadPredefinedEntityData(Entity newEntity, SceneData.SceneEntityData entityData)
	{
		newEntity.Name = entityData.Name;
		newEntity.SetTag(entityData.Tag);
		newEntity.Enabled = entityData.Enabled;
		newEntity.UpdateOrder = entityData.UpdateOrder;
		newEntity.DebugRenderEnabled = entityData.DebugRenderEnabled;
		newEntity.Type = entityData.InstanceType;

		if (newEntity.Type == Entity.InstanceType.SerializedPrefab)
			newEntity.OriginalPrefabName = entityData.OriginalPrefabName;
		else
			newEntity.OriginalPrefabName = null;

		// Handle transform and parent assignment
		Entity parentEntity = null;
		if (entityData.ParentId.HasValue && newEntity.Scene?.SceneData?.Entities != null)
		{
			var parentSceneData = newEntity.Scene.SceneData.Entities.FirstOrDefault(e => e.Id == entityData.ParentId.Value);
			if (parentSceneData != null)
				parentEntity = newEntity.Scene.FindEntity(parentSceneData.Name);
		}

		if (parentEntity == null && !string.IsNullOrEmpty(entityData.ParentEntityName))
			parentEntity = newEntity.Scene?.FindEntity(entityData.ParentEntityName);

		if (parentEntity != null)
		{
			newEntity.Transform.SetParent(parentEntity.Transform);
			newEntity.Transform.SetLocalPosition(entityData.Position);
			newEntity.Transform.SetLocalRotation(entityData.Rotation);
			newEntity.Transform.SetLocalScale(entityData.Scale);
		}
		else
		{
			if (entityData.ParentId.HasValue)
				newEntity.SetData("_PendingParentId", entityData.ParentId.Value);
			else if (!string.IsNullOrEmpty(entityData.ParentEntityName))
				newEntity.SetData("_PendingParentName", entityData.ParentEntityName);

			newEntity.SetData("_PendingLocalPosition", entityData.Position);
			newEntity.SetData("_PendingLocalRotation", entityData.Rotation);
			newEntity.SetData("_PendingLocalScale", entityData.Scale);

			newEntity.Transform.Position = entityData.Position;
			newEntity.Transform.Rotation = entityData.Rotation;
			newEntity.Transform.Scale = entityData.Scale;
		}

		if (entityData.EntityData != null)
		{
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

		newEntity.SetEntityData(deserializedEntityData);

			// Instantiate components from ComponentDataList
			if (deserializedEntityData.ComponentDataList != null)
			{
				foreach (var componentEntry in deserializedEntityData.ComponentDataList)
				{
					try
					{
						var componentType = ResolveType(componentEntry.ComponentTypeName);
						if (componentType == null)
						{
							Debug.Error($"Could not find component type: {componentEntry.ComponentTypeName}");
							continue;
						}

						var component = (Component)Activator.CreateInstance(componentType);
						component.Name = componentEntry.ComponentName;
						component.SetSerialized(true);
						newEntity.AddComponent(component, true);
					}
					catch (Exception ex)
					{
					 Debug.Error($"Failed to instantiate component {componentEntry.ComponentTypeName}: {ex.Message}");
					}
				}
			}

			var processedComponents = new HashSet<string>();

			// Assign data to already existing components
			foreach (var comp in newEntity.ComponentsToAdd)
			{
				if (TryAssignComponentDataFromEntityData(newEntity, comp))
				{
					var componentId = $"{comp.GetType().FullName}:{comp.Name}";
					processedComponents.Add(componentId);
				}
			}

			// Register callback for components added later
			newEntity.OnComponentAdded<Component>(comp =>
			{
				var componentId = $"{comp.GetType().FullName}:{comp.Name}";

				if (!processedComponents.Contains(componentId))
				{
					TryAssignComponentDataFromEntityData(newEntity, comp);
				}
			});
		}
	}

	/// <summary>
	/// Helper to deep-clone a ComponentDataEntry.
	/// </summary>
	private ComponentDataEntry CloneComponentDataEntry(ComponentDataEntry entry)
	{
		var json = Json.ToJson(entry, true);
		return Json.FromJson<ComponentDataEntry>(json);
	}

	/// <summary>
	/// Resolves a type by its full name, searching all loaded assemblies if Type.GetType() fails.
	/// When a LatestScriptAssembly is set, it is checked first to ensure newly compiled script types
	/// take priority over stale types from previously loaded assemblies.
	/// </summary>
	private static Type ResolveType(string typeName)
	{
		if (string.IsNullOrEmpty(typeName))
			return null;

		var type = Type.GetType(typeName);
		if (type != null)
			return type;

		// Check the latest script assembly first to ensure newly compiled types take priority
		// over stale types from old assemblies that are still loaded in the AppDomain.
		if (Core.LatestScriptAssembly != null)
		{
			type = Core.LatestScriptAssembly.GetType(typeName);
			if (type != null)
				return type;
		}

		// Fall back to searching all loaded assemblies but skip stale DynamicScripts
		// assemblies. Only the LatestScriptAssembly is authoritative for script types.
		foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
		{
			var assemblyName = assembly.GetName().Name;
			if (assemblyName != null && assemblyName.StartsWith("DynamicScripts"))
				continue;

			type = assembly.GetType(typeName);
			if (type != null)
				return type;
		}

		return null;
	}

	/// <summary>
	/// Tries to assign component data from entity data.
	/// </summary>
	[DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(List<SceneData.SceneEntityData>))]
	[DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(ComponentData))]
	private bool TryAssignComponentDataFromEntityData(Entity entity, Component component)
	{
		ComponentDataSerializationBootstrap.EnsureInitialized();
		var entityData = entity.GetEntityData();

		if (entityData == null || entityData.ComponentDataList == null)
			return false;

		for (int i = entityData.ComponentDataList.Count - 1; i >= 0; i--)
		{
			var entry = entityData.ComponentDataList[i];

			if (component.Name == entry.ComponentName)
			{
				// Data-less component (e.g. script component with no ComponentData override)
				if (string.IsNullOrWhiteSpace(entry.DataTypeName))
				{
					entityData.ComponentDataList.RemoveAt(i);
					entity.SetEntityData(entityData);
					return true;
				}

				var dataType = ResolveType(entry.DataTypeName);

				if (dataType != null)
				{
					try
					{
						var data = (ComponentData)Json.FromJson(entry.Json, dataType);
						component.Data = data;

						entityData.ComponentDataList.RemoveAt(i);
						entity.SetEntityData(entityData);

						return true;
					}
					catch (Exception ex)
					{
						Debug.Error($"Error loading component data for {component.Name}: {ex.Message}");
						return false;
					}
				}
				else
				{
					throw new InvalidOperationException($"Component data type '{entry.DataTypeName}' could not be resolved. " +
										$"If this entity was loaded from cooked content, ensure the type id is registered in ComponentDataSerializationBootstrap.");
				}
			}
		}

		return false;
	}

	#endregion

	#region Prefab Methods

	/// <summary>
	/// Saves a prefab entity to a JSON file.
	/// </summary>
	public async Task<bool> SavePrefabDataAsync(Entity prefabEntity, bool overrideExistingPrefab = false)
	{
		if (prefabEntity == null)
		{
			Debug.Error("Entity is null");
			return false;
		}

		if (prefabEntity.Type != Entity.InstanceType.SerializedPrefab)
		{
			Debug.Error("Entity is not a SerializedPrefab type!");
			return false;
		}

		try
		{
			var prefabsDirectory = $"{ProjectManager.Instance.CurrentProject.PrefabsFolder}";
			Directory.CreateDirectory(prefabsDirectory);

			var prefabFileName = !string.IsNullOrEmpty(prefabEntity.OriginalPrefabName)
				? prefabEntity.OriginalPrefabName
				: prefabEntity.Name;

			var sourceFilePath = $"{prefabsDirectory}/{prefabFileName}.vprefab";

			if (File.Exists(sourceFilePath) && !overrideExistingPrefab)
			{
				Debug.Error($"SerializedPrefab with name '{prefabEntity.Name}' already exists!");
				return false;
			}

			var prefabData = new PrefabData
			{
				InstanceType = prefabEntity.Type,
				Name = prefabEntity.Name,
				Rotation = prefabEntity.Transform.Rotation,
				Scale = prefabEntity.Transform.Scale,
				Enabled = prefabEntity.Enabled,
				UpdateOrder = prefabEntity.UpdateOrder,
				Tag = prefabEntity.Tag,
				DebugRenderEnabled = prefabEntity.DebugRenderEnabled
			};

			var entityData = prefabEntity.GetEntityData().Clone();
			entityData.ComponentDataList.Clear();

			foreach (var component in prefabEntity.ComponentsToAdd.Concat(prefabEntity.Components))
			{
				if (!component.IsSerialized)
					continue;

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
				else
				{
					entityData.ComponentDataList.Add(new ComponentDataEntry
					{
						ComponentTypeName = component.GetType().FullName,
						ComponentName = component.Name,
						DataTypeName = null,
						Json = null
					});
				}
			}

			prefabData.ChildEntities.Clear();
			foreach (var child in prefabEntity.Transform.Children)
			{
				var childEntity = child.Entity;
				if (childEntity.Type != Entity.InstanceType.NonSerialized)
				{
					var childData = new SceneData.SceneEntityData
					{
						InstanceType = childEntity.Type,
						Name = childEntity.Name,
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

			return true;
		}
		catch (Exception ex)
		{
			Debug.Error($"Failed to save prefab {prefabEntity.Name}: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Loads a prefab from JSON file by name.
	/// </summary>
	public PrefabData? LoadPrefabData(string prefabName)
	{
		try
		{
			var prefabsDirectory = ProjectManager.Instance.CurrentProject.PrefabsFolder;
			Directory.CreateDirectory(prefabsDirectory);

			var entityTypeDirectories = Directory.GetDirectories(prefabsDirectory);

			foreach (var entityTypeDir in entityTypeDirectories)
			{
				var prefabFilePath = Path.Combine(entityTypeDir, $"{prefabName}.vprefab");

				if (File.Exists(prefabFilePath))
				{
					var jsonContent = File.ReadAllText(prefabFilePath);

					if (string.IsNullOrWhiteSpace(jsonContent))
					{
						throw new Exception($"SerializedPrefab file is empty: {prefabFilePath}");
					}

					var prefabData = Json.FromJson<PrefabData>(jsonContent);

					if (prefabData.Name == null)
					{
						throw new Exception($"Invalid prefab data format for: {prefabName} - missing Name");
					}

					return prefabData;
				}
			}

			throw new Exception($"SerializedPrefab file not found: {prefabName}");
		}
		catch (Exception ex)
		{
			throw new Exception($"Failed to load prefab {prefabName}: {ex.Message}");
		}
	}

	/// <summary>
	/// Loads prefab entity data onto an entity.
	/// </summary>
	public void LoadPrefabEntityData(Entity newEntity, PrefabData prefabData)
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

		// Create child entities
		if (prefabData.ChildEntities != null)
		{
			foreach (var childData in prefabData.ChildEntities)
			{
				if (childData.InstanceType == Entity.InstanceType.NonSerialized)
					continue;

				if (IsDuplicateChild(newEntity.Scene, childData))
					continue;

				var childEntity = new Entity();
				LoadPredefinedEntityData(childEntity, childData);
				childEntity.Transform.SetParent(newEntity.Transform);
				newEntity.Scene.AddEntity(childEntity);
			}
		}
	}

	private bool IsDuplicateChild(Scene scene, SceneData.SceneEntityData childData)
	{
		var existing = scene.FindEntity(childData.Name);
		if (existing != null && existing.Transform.Parent?.Entity?.Name == childData.ParentEntityName)
			return true;
		return false;
	}

	#endregion
}