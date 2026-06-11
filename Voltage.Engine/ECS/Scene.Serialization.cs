using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Voltage.Data;
using Voltage.Serialization;
using Voltage.Serialization.Registries;
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

				// Use the AOT-safe deserializer — no reflection needed
				var sceneData = Serialization.AotDeserializers.DeserializeSceneData(jsonContent);

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
				// Skip non-serialized entities
				if (entity.Type == Entity.InstanceType.NonSerialized)
					continue;

				var entityData = BuildEntityData(entity);
				sceneData.Entities.Add(entityData);
			}

			// Build SceneComponent data — only include components marked as serialized
			sceneData.SceneComponents.Clear();

			for (var i = 0; i < _sceneComponents.Length; i++)
			{
				var sc = _sceneComponents.Buffer[i];
				if (!sc.IsSerialized)
					continue;

				var isEngineType = sc.GetType().Assembly == typeof(Component).Assembly;

				var entry = new SceneComponentDataEntry
				{
					ComponentTypeName = sc.GetType().FullName,
					ComponentName     = sc.Name
				};

				if (sc.Data != null)
				{
					var jsonSettings = new Voltage.Persistence.JsonSettings
					{
						PrettyPrint = true,
						TypeNameHandling = isEngineType
							? Voltage.Persistence.TypeNameHandling.Auto
							: Voltage.Persistence.TypeNameHandling.None,
						PreserveReferencesHandling = false
					};

					entry.DataTypeName = sc.Data.GetType().FullName;
					entry.Json = Voltage.Persistence.Json.ToJson(sc.Data, jsonSettings);
				}

				sceneData.SceneComponents.Add(entry);
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
				//Id = existingId != Guid.Empty ? existingId : Guid.NewGuid(),
				Id = entity.PersistentId,
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
				IsSelectableInEditor = entity.CanBeSelected,
				DebugRenderEnabled = entity.DebugRenderEnabled,
				OriginalPrefabName = entity.OriginalPrefabName
			};

			// Get entity-specific data
			var entData = entity.GetEntityData();
			if (entData != null)
			{
				entData.ComponentDataList.Clear();

				// Include both live and pending-add components.
				var allComponents = entity.Components
					.Concat(entity.ComponentsToAdd);

				// Serialize only components marked as serialized (added via editor/scene data)
				foreach (var component in allComponents)
				{
					if (!component.IsSerialized)
						continue;

					if (component.Data != null)
					{
						// Don't write "$type" into the JSON for script/game component data.
						// We already store the type name in DataTypeName (no assembly info).
						// A stale assembly-qualified $type in the JSON would break
						// deserialization in published builds or after a recompile.
						var isEngineType = component.GetType().Assembly == typeof(Component).Assembly;

						var componentJsonSettings = new Voltage.Persistence.JsonSettings
						{
							PrettyPrint = true,
							TypeNameHandling = isEngineType
								? Voltage.Persistence.TypeNameHandling.Auto
								: Voltage.Persistence.TypeNameHandling.None,
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

		private void LoadSceneEntitiesData()
		{
			if (SceneData == null || SceneData.Entities == null)
			{
				Debug.Warn("No SceneData or entities to load");
				return;
			}

			ComponentDataSerializationBootstrap.EnsureInitialized();

			var sceneEntitiesByName =
				new Dictionary<string, SceneData.SceneEntityData>(StringComparer.OrdinalIgnoreCase);

			for (var i = 0; i < SceneData.Entities.Count; i++)
			{
				var e = SceneData.Entities[i];
				if (!string.IsNullOrWhiteSpace(e.Name))
					sceneEntitiesByName[e.Name] = e;
			}

			var entitiesNeedingParents = new List<Entity>();
			var allExistingEntities = Entities.Concat(Entities.EntitiesToAdd);

			foreach (var entity in allExistingEntities)
			{
				if (entity.Type != Entity.InstanceType.SceneRequired)
					continue;

				if (sceneEntitiesByName.TryGetValue(entity.Name, out var sceneEntityData))
				{
					LoadEntityData(entity, sceneEntityData);

					if (!string.IsNullOrEmpty(entity.GetData<string>("_PendingParentName")) ||
					    entity.GetData<Guid>("_PendingParentId") != Guid.Empty)
						entitiesNeedingParents.Add(entity);
				}
			}

			foreach (var entity in allExistingEntities)
			{
				if (entity.Type != Entity.InstanceType.NonSerialized)
					continue;

				if (sceneEntitiesByName.TryGetValue(entity.Name, out var sceneEntityData))
				{
					LoadEntityData(entity, sceneEntityData);

					if (!string.IsNullOrEmpty(entity.GetData<string>("_PendingParentName")) ||
					    entity.GetData<Guid>("_PendingParentId") != Guid.Empty)
						entitiesNeedingParents.Add(entity);
				}
			}

			foreach (var sceneEntity in SceneData.Entities)
			{
				if (sceneEntity.InstanceType == Entity.InstanceType.NonSerialized)
					continue;

				if (sceneEntity.InstanceType == Entity.InstanceType.SceneRequired)
				{
					var existingEntity = FindEntity(sceneEntity.Name);
					if (existingEntity != null && existingEntity.Type == Entity.InstanceType.SceneRequired)
						continue;
				}

				var entity = new Entity(sceneEntity.Name);
				AddEntity(entity);
				LoadEntityData(entity, sceneEntity);

				if (!string.IsNullOrEmpty(entity.GetData<string>("_PendingParentName")) ||
				    entity.GetData<Guid>("_PendingParentId") != Guid.Empty)
					entitiesNeedingParents.Add(entity);
			}

			AssignParentRelationships(entitiesNeedingParents);
		}

		private void LoadEntityData(Entity entity, SceneData.SceneEntityData entityData)
		{
			entity.PersistentId = entityData.Id;
			entity.Name = entityData.Name;
			entity.SetTag(entityData.Tag);
			entity.Enabled = entityData.Enabled;
			entity.UpdateOrder = entityData.UpdateOrder;
			entity.DebugRenderEnabled = entityData.DebugRenderEnabled;
			entity.Type = entityData.InstanceType;
			entity.CanBeSelected = entityData.IsSelectableInEditor;

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
				// The EntityData was already deserialized by AotDeserializers — no need
				// to round-trip through Json.ToJson/FromJson. Just clone the
				// ComponentDataList entries directly to avoid shared references.
				var clonedEntityData = entityData.EntityData.Clone();
				
				entity.SetEntityData(clonedEntityData);

				// Instantiate components from ComponentDataList, but skip any that
				// already exist on the entity (e.g. Camera on SceneRequired entities).
				if (clonedEntityData.ComponentDataList != null)
				{
					foreach (var componentEntry in clonedEntityData.ComponentDataList)
					{
						// Check if a component with this name already exists on the entity
						// (in either the live Components list or the pending ComponentsToAdd list)
						bool alreadyExists = false;
						foreach (var existing in entity.Components)
						{
							if (existing.Name == componentEntry.ComponentName)
							{
								alreadyExists = true;
								break;
							}
						}
						if (!alreadyExists)
						{
							foreach (var existing in entity.ComponentsToAdd)
							{
								if (existing.Name == componentEntry.ComponentName)
								{
									alreadyExists = true;
									break;
								}
							}
						}

						if (alreadyExists)
							continue;

						try
						{
							var component = CreateComponentInstance(componentEntry.ComponentTypeName);
							if (component == null)
							{
								Debug.Error($"Could not create component: {componentEntry.ComponentTypeName}");
								continue;
							}

							component.Name = componentEntry.ComponentName;
							component.SetSerialized(true);
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

				// Also assign data to components already live on the entity (e.g. Camera on SceneRequired)
				foreach (var comp in entity.Components)
				{
					var componentId = $"{comp.GetType().FullName}:{comp.Name}";
					if (!processedComponents.Contains(componentId))
					{
						if (TryAssignComponentData(entity, comp))
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
		/// Creates a component instance by type name.
		/// Uses ComponentAotFactory first (NativeAOT-safe), then falls back to reflection in editor builds.
		/// </summary>
		private static Component CreateComponentInstance(string componentTypeName)
		{
			if (string.IsNullOrEmpty(componentTypeName))
				return null;

			if (ComponentAotFactory.IsRegistered(componentTypeName))
				return (Component)ComponentAotFactory.Create(componentTypeName);

			// Fall back to reflection for both editor and published .NET 8 builds.
			// NativeAOT requires explicit registration — the source generator handles that.
			// If this warn fires in a published build, the component's class is not 'partial'
			// or Voltage.SourceGenerators.dll is not referenced by the game project.
			var type = ResolveType(componentTypeName);
			if (type == null)
			{
				Debug.Error($"[Scene] Could not instantiate component '{componentTypeName}': type not found in any loaded assembly.");
				return null;
			}

			Debug.Warn($"[Scene] Component '{componentTypeName}' has no AOT factory registration — using reflection. " +
				"Ensure the class is 'partial' and Voltage.SourceGenerators is referenced.");
			return (Component)Activator.CreateInstance(type);
		}

		/// <summary>
		/// Creates a SceneComponent instance by type name.
		/// Uses ComponentAotFactory first (NativeAOT-safe), then falls back to reflection in editor builds.
		/// </summary>
		private static SceneComponent CreateSceneComponentInstance(string typeName)
		{
			if (string.IsNullOrEmpty(typeName))
				return null;

			if (ComponentAotFactory.IsRegistered(typeName))
				return (SceneComponent)ComponentAotFactory.Create(typeName);

			var type = ResolveType(typeName);
			if (type == null)
				return null;

			Debug.Warn($"[Scene] SceneComponent '{typeName}' has no AOT factory registration — using reflection. " +
				"Ensure the class is 'partial' and Voltage.SourceGenerators is referenced.");
			return (SceneComponent)Activator.CreateInstance(type);
		}

		/// <summary>
		/// Tries to assign component data from entity data using Voltage.Persistence.Json.
		/// In published builds the generated Data property setter handles field assignment;
		/// the JSON layer just needs to deserialize the ComponentData subclass, which
		/// Voltage.Persistence.Json can do via the type name stored in the entry.
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
					if (string.IsNullOrWhiteSpace(entry.DataTypeName))
					{
						entityData.ComponentDataList.RemoveAt(i);
						entity.SetEntityData(entityData);
						return true;
					}

					try
					{
						ComponentData data = null;

						// Try AOT deserializer first — it keys by FullName only so it is
						// immune to stale assembly-qualified $type names in the JSON.
						if (Serialization.ComponentDataAotDeserializer.IsRegistered(entry.DataTypeName))
						{
							var cleanJson = StripTypeFieldFromJson(entry.Json);
							data = Serialization.ComponentDataAotDeserializer.TryDeserialize(entry.DataTypeName, cleanJson);
						}
						else
						{
							Type dataType = ResolveType(entry.DataTypeName);
							if (dataType != null)
							{
								// Strip stale $type so the deserializer binds to the
								// resolved type rather than the old assembly's type.
								var cleanJson = StripTypeFieldFromJson(entry.Json);
								data = (ComponentData)Persistence.Json.FromJson(cleanJson, dataType);
							}
						}

						if (data != null)
						{
							component._pendingLoadedData = data;
							component.Data = data;
						}
						else
						{
							Debug.Warn($"Could not deserialize component data '{entry.DataTypeName}' for '{component.Name}'. " +
								"Component will use default values. Re-save the scene to fix this.");
						}

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
			}

			return false;
		}

		/// <summary>
		/// Removes the top-level "$type" property from a JSON object string so the
		/// deserializer binds to the explicitly supplied target type rather than a
		/// potentially stale assembly-qualified name baked into the JSON.
		/// </summary>
		private static string StripTypeFieldFromJson(string json)
		{
			if (string.IsNullOrWhiteSpace(json) || !json.Contains("\"$type\""))
				return json;

			return System.Text.RegularExpressions.Regex.Replace(
				json,
				@"^\s*\{\s*""\$type""\s*:\s*""[^""]*""\s*,\s*",
				"{ ",
				System.Text.RegularExpressions.RegexOptions.Singleline);
		}

		/// <summary>
		/// Resolves a type by its full name, searching all loaded assemblies if Type.GetType fails.
		/// </summary>
		private static Type ResolveType(string typeName)
		{
			if (string.IsNullOrEmpty(typeName))
				return null;

			// Try the standard lookup first (works for engine/framework types)
			var type = Type.GetType(typeName);
			if (type != null)
				return type;

			// Check the latest script assembly first to ensure newly compiled types take priority
			if (Core.LatestScriptAssembly != null)
			{
				type = Core.LatestScriptAssembly.GetType(typeName);
				if (type != null)
					return type;
			}

			// Fall back to searching all loaded assemblies
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
		/// Instantiates and configures all serialized SceneComponents from SceneData.
		/// Called during <see cref="Scene.Begin"/> after entity loading.
		/// </summary>
		private void LoadSceneComponentsData()
		{
			if (SceneData?.SceneComponents == null)
				return;

			ComponentDataSerializationBootstrap.EnsureInitialized();

			foreach (var entry in SceneData.SceneComponents)
			{
				if (string.IsNullOrEmpty(entry.ComponentTypeName))
					continue;

				try
				{
					// Check if a SceneComponent of this type is already on the scene
					// (e.g. added via code in OnStart). If so, just apply data to it.
					SceneComponent existing = null;
					for (var i = 0; i < _sceneComponents.Length; i++)
					{
						if (_sceneComponents.Buffer[i].GetType().FullName == entry.ComponentTypeName
							&& _sceneComponents.Buffer[i].Name == entry.ComponentName)
						{
							existing = _sceneComponents.Buffer[i];
							break;
						}
					}

					if (existing == null)
					{
						existing = CreateSceneComponentInstance(entry.ComponentTypeName);
						if (existing == null)
						{
							Debug.Error($"[Scene] Could not load SceneComponent '{entry.ComponentTypeName}': type not found.");
							continue;
						}

						existing.Name = entry.ComponentName ?? existing.GetType().Name;
						existing.SetSerialized(true);
						AddSceneComponent(existing);
					}

					// Apply serialized data if present
					if (!string.IsNullOrEmpty(entry.DataTypeName) && !string.IsNullOrEmpty(entry.Json))
					{
						ComponentData data = null;

						if (Serialization.ComponentDataAotDeserializer.IsRegistered(entry.DataTypeName))
						{
							var cleanJson = StripTypeFieldFromJson(entry.Json);
							data = Serialization.ComponentDataAotDeserializer.TryDeserialize(entry.DataTypeName, cleanJson);
						}
						else
						{
							var dataType = ResolveType(entry.DataTypeName);
							if (dataType != null)
							{
								var cleanJson = StripTypeFieldFromJson(entry.Json);
								data = (ComponentData)Persistence.Json.FromJson(cleanJson, dataType);
							}
						}

						if (data != null)
						{
							existing._pendingLoadedData = data;
							existing.Data = data;
						}
						else
							Debug.Warn($"[Scene] Could not deserialize data for SceneComponent '{entry.ComponentTypeName}'.");
					}
				}
				catch (Exception ex)
				{
					Debug.Error($"[Scene] Failed to load SceneComponent '{entry.ComponentTypeName}': {ex.Message}");
				}
			}
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
