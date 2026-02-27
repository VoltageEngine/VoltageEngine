using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Voltage.Data;
using Voltage.Serialization;
using Voltage.Utils.Extensions;

namespace Voltage
{
	partial class Scene
	{
		/// <summary>
		/// Loads a scene from a .vscene file path
		/// </summary>
		public static Scene LoadFromFile(string scenePath)
		{
			if (string.IsNullOrWhiteSpace(scenePath))
			{
				Debug.Error("Scene path cannot be null or empty");
				return null;
			}

			if (!File.Exists(scenePath))
			{
				Debug.Error($"Scene file not found: {scenePath}");
				return null;
			}

			try
			{
				var jsonContent = File.ReadAllText(scenePath);
				var sceneData = Persistence.Json.FromJson<SceneData>(jsonContent);

				if (sceneData == null)
				{
					Debug.Error($"Failed to deserialize scene from: {scenePath}");
					return null;
				}

				var scene = new Scene();
				scene.ApplySceneData(sceneData);

				return scene;
			}
			catch (Exception ex)
			{
				Debug.Error($"Failed to load scene from '{scenePath}': {ex.Message}." +
							$" \n Stack trace: {ex.StackTrace}");
				return null;
			}
		}

		/// <summary>
		/// Saves this scene to a JSON file
		/// </summary>
		public bool SaveToFile(string scenePath)
		{
			if (string.IsNullOrWhiteSpace(scenePath))
			{
				Debug.Error("Scene path cannot be null or empty");
				return false;
			}

			try
			{
				// Build scene data from current state
				var sceneData = BuildSceneData();
				sceneData.FilePath = scenePath;

				// Update modification time
				sceneData.ModifiedAt = DateTime.Now;

				// Serialize to JSON
				var jsonSettings = new Voltage.Persistence.JsonSettings
				{
					PrettyPrint = true,
					TypeNameHandling = Voltage.Persistence.TypeNameHandling.Auto,
					PreserveReferencesHandling = false
				};

				var jsonContent = Voltage.Persistence.Json.ToJson(sceneData, jsonSettings);

				// Ensure directory exists
				var directory = Path.GetDirectoryName(scenePath);
				if (!string.IsNullOrEmpty(directory))
				{
					Directory.CreateDirectory(directory);
				}

				// Write to file
				File.WriteAllText(scenePath, jsonContent, new System.Text.UTF8Encoding(false));

				Debug.Log($"Successfully saved scene to: {scenePath}");
				return true;
			}
			catch (Exception ex)
			{
				Debug.Error($"Failed to save scene to '{scenePath}': {ex.Message}");
				Debug.Error($"Stack trace: {ex.StackTrace}");
				return false;
			}
		}

		/// <summary>
		/// Builds SceneData from the current scene state
		/// </summary>
		public SceneData BuildSceneData()
		{
			var sceneData = new SceneData();

			// Copy scene metadata
			if (SceneData != null)
			{
				sceneData.Name = SceneData.Name;
				sceneData.CreatedAt = SceneData.CreatedAt;
				sceneData.TiledMapFileName = SceneData.TiledMapFileName;
				sceneData.EditorData = SceneData.EditorData != null
					? new Dictionary<string, string>(SceneData.EditorData)
					: new Dictionary<string, string>();
			}
			else
			{
				// Fallback: use the scene type name if no SceneData exists
				sceneData.Name = GetType().Name;
			}

			// IMPORTANT: Ensure the name is never empty
			if (string.IsNullOrWhiteSpace(sceneData.Name))
			{
				sceneData.Name = "Untitled Scene";
			}

			// Copy scene settings
			sceneData.ClearColor = ClearColor;
			sceneData.LetterboxColor = LetterboxColor;
			sceneData.ResolutionPolicy = _resolutionPolicy.ToString();
			sceneData.DesignResolutionWidth = _designResolutionSize.X;
			sceneData.DesignResolutionHeight = _designResolutionSize.Y;
			sceneData.HorizontalBleed = _designBleedSize.X;
			sceneData.VerticalBleed = _designBleedSize.Y;
			sceneData.EnablePostProcessing = EnablePostProcessing;

			// Build entity data
			sceneData.Entities.Clear();

			foreach (var entity in Entities)
			{
				// Skip non-serialized entities (like the camera)
				if (entity.Type == Entity.InstanceType.NonSerialized)
					continue;

				var entityData = BuildEntityData(entity);
				sceneData.Entities.Add(entityData);
			}

			return sceneData;
		}

		/// <summary>
		/// Builds entity data from an entity
		/// </summary>
		private SceneData.SceneEntityData BuildEntityData(Entity entity)
		{
			Vector2 positionToSave;
			float rotationToSave;
			Vector2 scaleToSave;

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

			var existingId = Guid.Empty;
			if (SceneData?.Entities != null)
				existingId = SceneData.Entities.FirstOrDefault(e => string.Equals(e.Name, entity.Name, StringComparison.OrdinalIgnoreCase))?.Id ?? Guid.Empty;

			Guid? parentId = null;
			var parentName = entity.Transform.Parent?.Entity?.Name;
			if (SceneData?.Entities != null && !string.IsNullOrWhiteSpace(parentName))
				parentId = SceneData.Entities.FirstOrDefault(e => string.Equals(e.Name, parentName, StringComparison.OrdinalIgnoreCase))?.Id;

			var entityData = new SceneData.SceneEntityData
			{
				Id = existingId != Guid.Empty ? existingId : Guid.NewGuid(),
				ParentId = parentId,
				InstanceType = entity.Type,
				Name = entity.Name,
				Position = positionToSave,
				Rotation = rotationToSave,
				Scale = scaleToSave,
				ParentEntityName = parentName,
				Enabled = entity.Enabled,
				UpdateOrder = entity.UpdateOrder,
				Tag = entity.Tag,
				IsSelectableInEditor = entity.IsSelectableInEditor,
				DebugRenderEnabled = entity.DebugRenderEnabled,
				OriginalPrefabName = entity.OriginalPrefabName
			};

			// Get entity-specific data
			var entData = entity.GetEntityData();
			if (entData != null)
			{
				entData.ComponentDataList.Clear();

				// Serialize only components marked as serialized (added via editor/scene data)
				foreach (var component in entity.Components)
				{
					if (!component.IsSerialized)
						continue;

					if (component.Data != null)
					{
						var componentJsonSettings = new Voltage.Persistence.JsonSettings
						{
							PrettyPrint = true,
							TypeNameHandling = Voltage.Persistence.TypeNameHandling.Auto,
							PreserveReferencesHandling = false
						};

						var json = Voltage.Persistence.Json.ToJson(component.Data, componentJsonSettings);
						entData.ComponentDataList.Add(new ComponentDataEntry
						{
							ComponentTypeName = component.GetType().FullName,
							ComponentName = component.Name,
							DataTypeName = component.Data.GetType().FullName,
							Json = json
						});
					}
					else
					{
						// Save data-less components (e.g. script components with no ComponentData override)
						// so they are recreated when the scene is reloaded
						entData.ComponentDataList.Add(new ComponentDataEntry
						{
							ComponentTypeName = component.GetType().FullName,
							ComponentName = component.Name,
							DataTypeName = null,
							Json = null
						});
					}
				}

				entityData.EntityData = entData;
			}

			return entityData;
		}

		/// <summary>
		/// Applies loaded SceneData to this scene
		/// </summary>
		private void ApplySceneData(SceneData sceneData)
		{
			if (sceneData == null)
			{
				Debug.Error("Cannot apply null SceneData");
				return;
			}

			SceneData = sceneData;

			ClearColor = sceneData.ClearColor;
			LetterboxColor = sceneData.LetterboxColor;
			EnablePostProcessing = sceneData.EnablePostProcessing;

			// Parse and apply resolution policy
			if (Enum.TryParse<SceneResolutionPolicy>(sceneData.ResolutionPolicy, out var resolutionPolicy))
			{
				SetDesignResolution(
					sceneData.DesignResolutionWidth,
					sceneData.DesignResolutionHeight,
					resolutionPolicy,
					sceneData.HorizontalBleed,
					sceneData.VerticalBleed
				);
			}
			else
			{
				Debug.Warn($"Unknown resolution policy: {sceneData.ResolutionPolicy}, using default");
				SetDesignResolution(1920, 1080, SceneResolutionPolicy.BestFit);
			}
		}

		/// <summary>
		/// Loads entities from the stored SceneData.
		/// This is called automatically during scene initialization.
		/// </summary>
		private void LoadSceneEntitiesData()
		{
			if (SceneData == null || SceneData.Entities == null)
			{
				Debug.Warn("No SceneData or entities to load");
				return;
			}

			var sceneEntitiesByName = new Dictionary<string, SceneData.SceneEntityData>(StringComparer.OrdinalIgnoreCase);
			var sceneEntitiesById = new Dictionary<Guid, SceneData.SceneEntityData>();

			for (var i = 0; i < SceneData.Entities.Count; i++)
			{
				var e = SceneData.Entities[i];
				if (!string.IsNullOrWhiteSpace(e.Name))
					sceneEntitiesByName[e.Name] = e;
				if (e.Id != Guid.Empty)
					sceneEntitiesById[e.Id] = e;
			}

			var entitiesNeedingParents = new List<Entity>();

			// NonSerialized entities (already in the scene, like camera)
			for (var i = 0; i < Entities.Count; i++)
			{
				if (Entities[i].Type != Entity.InstanceType.NonSerialized)
					continue;

				if (sceneEntitiesByName.TryGetValue(Entities[i].Name, out var sceneEntityData))
				{
					LoadEntityData(Entities[i], sceneEntityData);

					// Check if this entity needs parent assignment later
					if (!string.IsNullOrEmpty(Entities[i].GetData<string>("_PendingParentName")))
						entitiesNeedingParents.Add(Entities[i]);
				}
			}

			// Serialized & SerializedPrefab entities (to be created now)
			foreach (var sceneEntity in SceneData.Entities)
			{
				if (sceneEntity.InstanceType == Entity.InstanceType.NonSerialized)
					continue;

				var entity = new Entity(sceneEntity.Name);
				entity.Type = sceneEntity.InstanceType;
				AddEntity(entity);
				LoadEntityData(entity, sceneEntity);

				if (!string.IsNullOrEmpty(entity.GetData<string>("_PendingParentName")))
					entitiesNeedingParents.Add(entity);
			}

			AssignParentRelationships(entitiesNeedingParents);
			Debug.Info($"Loaded {SceneData.Entities.Count} entities from scene data");
		}

		/// <summary>
		/// Loads entity data into an entity instance
		/// </summary>
		private void LoadEntityData(Entity entity, SceneData.SceneEntityData entityData)
		{
			entity.Name = entityData.Name;
			entity.SetTag(entityData.Tag);
			entity.Enabled = entityData.Enabled;
			entity.UpdateOrder = entityData.UpdateOrder;
			entity.DebugRenderEnabled = entityData.DebugRenderEnabled;
			entity.Type = entityData.InstanceType;
			entity.IsSelectableInEditor = entityData.IsSelectableInEditor;

			if (entity.Type == Entity.InstanceType.SerializedPrefab)
				entity.OriginalPrefabName = entityData.OriginalPrefabName;
			else
				entity.OriginalPrefabName = null;

			// Handle transform and parent assignment (prefer ParentId if present)
			Entity parentEntity = null;
			if (entityData.ParentId.HasValue && SceneData?.Entities != null)
			{
				var parentSceneData = SceneData.Entities.FirstOrDefault(e => e.Id == entityData.ParentId.Value);
				if (parentSceneData != null)
					parentEntity = FindEntity(parentSceneData.Name);
			}
			if (parentEntity == null && !string.IsNullOrEmpty(entityData.ParentEntityName))
				parentEntity = FindEntity(entityData.ParentEntityName);

			if (parentEntity != null)
			{
				entity.Transform.SetParent(parentEntity.Transform);
				entity.Transform.SetLocalPosition(entityData.Position);
				entity.Transform.SetLocalRotation(entityData.Rotation);
				entity.Transform.SetLocalScale(entityData.Scale);
			}
			else
			{
				// Parent not found yet, save for later
				if (entityData.ParentId.HasValue)
					entity.SetData("_PendingParentId", entityData.ParentId.Value);
				else if (!string.IsNullOrEmpty(entityData.ParentEntityName))
					entity.SetData("_PendingParentName", entityData.ParentEntityName);

				entity.SetData("_PendingLocalPosition", (Vector2)entityData.Position);
				entity.SetData("_PendingLocalRotation", entityData.Rotation);
				entity.SetData("_PendingLocalScale", (Vector2)entityData.Scale);

				entity.Transform.Position = entityData.Position;
				entity.Transform.Rotation = entityData.Rotation;
				entity.Transform.Scale = entityData.Scale;
			}

			// Load entity-specific data and components
			if (entityData.EntityData != null)
			{
				var entDataType = entityData.EntityData.GetType();
				var json = Voltage.Persistence.Json.ToJson(entityData.EntityData, true);
				var deserializedEntityData = (EntityData)Voltage.Persistence.Json.FromJson(json, entDataType);

				// Deep clone ComponentDataList to avoid shared references
				if (deserializedEntityData.ComponentDataList != null)
				{
					deserializedEntityData.ComponentDataList = deserializedEntityData.ComponentDataList
						.Select(entry =>
						{
							var cloneJson = Voltage.Persistence.Json.ToJson(entry, true);
							return Voltage.Persistence.Json.FromJson<ComponentDataEntry>(cloneJson);
						})
						.ToList();
				}

				entity.SetEntityData(deserializedEntityData);

				// Instantiate components from ComponentDataList
				if (deserializedEntityData.ComponentDataList != null)
				{
					foreach (var componentEntry in deserializedEntityData.ComponentDataList)
					{
						try
						{
							// Get the component type from the type name
							var componentType = ResolveType(componentEntry.ComponentTypeName);
							if (componentType == null)
							{
								Debug.Error($"Could not find component type: {componentEntry.ComponentTypeName}");
								continue;
							}

							// Create a new instance of the component
							var component = (Component)Activator.CreateInstance(componentType);
							component.Name = componentEntry.ComponentName;
							component.SetSerialized(true);

							// Add the component to the entity
							entity.AddComponent(component, true);
						}
						catch (Exception ex)
						{
							Debug.Error($"Failed to instantiate component {componentEntry.ComponentTypeName}: {ex.Message}");
						}
					}
				}

				var processedComponents = new HashSet<string>();

				// Assign data to already existing components (including newly added ones)
				foreach (var comp in entity.ComponentsToAdd)
				{
					if (TryAssignComponentData(entity, comp))
					{
						var componentId = $"{comp.GetType().FullName}:{comp.Name}";
						processedComponents.Add(componentId);
					}
				}

				// Register callback for components added later (if any are added dynamically)
				entity.OnComponentAdded<Component>(comp =>
				{
					var componentId = $"{comp.GetType().FullName}:{comp.Name}";

					if (!processedComponents.Contains(componentId))
					{
						TryAssignComponentData(entity, comp);
					}
				});
			}
		}

		/// <summary>
		/// Tries to assign component data from entity data
		/// </summary>
		private bool TryAssignComponentData(Entity entity, Component component)
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

					Type dataType = ResolveType(entry.DataTypeName);

					if (dataType != null)
					{
						try
						{
							var data = (ComponentData)Voltage.Persistence.Json.FromJson(entry.Json, dataType);
							component.Data = data;

							// Remove the processed entry
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
						Debug.Error($"Component type '{entry.DataTypeName}' is not registered in ComponentDataTypeRegistrator.DataTypes");
						return false;
					}
				}
			}

			return false;
		}

		/// <summary>
		/// Resolves a type by its full name, searching all loaded assemblies if Type.GetType fails.
		/// This is necessary for dynamically compiled script assemblies which Type.GetType cannot find
		/// since it only searches the calling assembly and core libraries by default.
		/// When a LatestScriptAssembly is set, it is checked first to ensure newly compiled script types
		/// take priority over stale types from previously loaded assemblies.
		/// </summary>
		private static Type ResolveType(string typeName)
		{
			// Try the standard lookup first (works for engine/framework types)
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

			// Fall back to searching all loaded assemblies (needed for dynamically compiled scripts)
			foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				type = assembly.GetType(typeName);
				if (type != null)
					return type;
			}

			return null;
		}

		/// <summary>
		/// Assigns parent relationships after all entities are loaded
		/// </summary>
		private void AssignParentRelationships(List<Entity> entitiesNeedingParents)
		{
			foreach (var entity in entitiesNeedingParents)
			{
				Entity parentEntity = null;
				var parentId = entity.GetData<Guid>("_PendingParentId");
				if (parentId != Guid.Empty && SceneData?.Entities != null)
				{
					var parentSceneData = SceneData.Entities.FirstOrDefault(e => e.Id == parentId);
					if (parentSceneData != null)
						parentEntity = FindEntity(parentSceneData.Name);
				}

				if (parentEntity == null)
				{
					var parentName = entity.GetData<string>("_PendingParentName");
					if (!string.IsNullOrEmpty(parentName))
						parentEntity = FindEntity(parentName);
				}

				if (parentEntity != null)
				{
					var savedLocalPosition = entity.GetData<Vector2>("_PendingLocalPosition");
					var savedLocalRotation = entity.GetData<float>("_PendingLocalRotation");
					var savedLocalScale = entity.GetData<Vector2>("_PendingLocalScale");

					entity.Transform.SetParent(parentEntity.Transform);
					entity.Transform.SetLocalPosition(savedLocalPosition);
					entity.Transform.SetLocalRotation(savedLocalRotation);
					entity.Transform.SetLocalScale(savedLocalScale);
				}
				else
				{
					Debug.Error($"Could not find parent entity for entity '{entity.Name}'");
				}

				// Clean up temporary data
				entity.RemoveData("_PendingParentId");
				entity.RemoveData("_PendingParentName");
				entity.RemoveData("_PendingLocalPosition");
				entity.RemoveData("_PendingLocalRotation");
				entity.RemoveData("_PendingLocalScale");
			}
		}
	}
}
