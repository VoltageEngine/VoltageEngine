using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Voltage.Data;
using Voltage.Project;
using Voltage.Serialization;
using Voltage.Serialization.Registries;
using Voltage.Utils;
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

				// Remember where we loaded from so the scene knows its level/file identity.
				// FilePath is [JsonExclude], so it is not present in the .vscene itself.
				scene.SceneData.FilePath = scenePath;

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
		/// Optional override for the directory that <see cref="LoadLevel"/> resolves level names against.
		/// In a published game this stays null and levels resolve to <c>AppContext.BaseDirectory/Data/Scenes</c>.
		/// The Voltage Editor sets this to the open project's Scenes folder so LoadLevel works in play mode,
		/// where the base directory is the editor binary rather than the game project.
		/// </summary>
		public static string ScenesDirectory;

		/// <summary>
		/// Optional resolver for Phase 4b prefab delta loading.
		/// Given a prefab GUID (may be <see cref="Guid.Empty"/>) and name, returns the absolute
		/// path to the <c>.vprefab</c> file, or null when it cannot be found.
		/// <para>
		/// In a published game this stays null and the engine falls back to the standard
		/// <see cref="PrefabsDirectory"/> path-by-name resolution.  The editor wires in a
		/// <see cref="Voltage.Editor.Assets.AssetDatabase"/>-backed implementation that
		/// resolves by GUID first, then by name.
		/// </para>
		/// </summary>
		public static Func<Guid, string, string> PrefabPathResolver;

		/// <summary>
		/// Optional resolver for general asset GUIDs (used by <see cref="AssetReference"/>). Given an
		/// asset GUID, returns its current absolute path, or null. In a published game this stays null
		/// and <see cref="AssetReference"/> resolves via the baked <see cref="Voltage.Assets.AssetManifest"/>;
		/// the editor wires this to <c>AssetDatabase.GetPath</c> so it works in play mode too.
		/// </summary>
		public static Func<Guid, string> AssetPathResolver;

		/// <summary>
		/// Loads a content asset referenced by an <see cref="AssetReference"/> through this scene's
		/// <see cref="Content"/> manager. The reference is resolved <b>GUID-first</b> (so it survives
		/// renaming/moving the file), then the resolved path is converted to a Content-relative name
		/// (the part under the <c>Content</c> folder, without extension) and loaded via the pipeline.
		/// Returns <c>default</c> when the reference is empty or cannot be resolved.
		/// </summary>
		public T LoadAsset<T>(AssetReference reference)
		{
			if (!reference.IsValid)
				return default;

			var path = reference.ResolvePath();
			if (string.IsNullOrEmpty(path))
			{
				Debug.Warn($"[Scene.LoadAsset] Could not resolve AssetReference '{reference}'. " +
				           "Open the project in the editor to (re)generate the asset manifest.");
				return default;
			}

			// Dispatch to the strongly-typed loader registered for T (Texture2D, SoundEffect,
			// SpriteAtlas, BitmapFont, AsepriteFile, TmxMap, …), falling back to the MonoGame
			// content pipeline for anything unmapped. File.Exists distinguishes a raw source
			// asset from a compiled-only .xnb for the dual-format types; the loader table is
			// resolved once per T, so this stays allocation- and reflection-free per call.
			return Content.LoadByType<T>(path, ToContentName(path), File.Exists(path));
		}

		/// <summary>
		/// Converts an absolute (or project-relative) asset path to a ContentManager name: the portion
		/// under the <c>Content</c> root, forward-slashed and without file extension
		/// (e.g. <c>…/Content/Sprites/Player.png → Sprites/Player</c>).
		/// </summary>
		private static string ToContentName(string assetPath)
		{
			var norm = assetPath.Replace('\\', '/');

			int idx = norm.LastIndexOf("/Content/", StringComparison.OrdinalIgnoreCase);
			string rel;
			if (idx >= 0)
				rel = norm.Substring(idx + "/Content/".Length);
			else if (norm.StartsWith("Content/", StringComparison.OrdinalIgnoreCase))
				rel = norm.Substring("Content/".Length);
			else
				rel = norm; // assume already Content-relative

			int dot = rel.LastIndexOf('.');
			if (dot > rel.LastIndexOf('/'))
				rel = rel.Substring(0, dot);

			return rel;
		}

		/// <summary>
		/// Optional override for the directory that <see cref="LoadPrefab"/> resolves relative paths against.
		/// In a published game this stays null and paths resolve to <c>AppContext.BaseDirectory/Data/Prefabs</c>.
		/// The Voltage Editor sets this to the open project's Prefabs folder so LoadPrefab works in play mode,
		/// where the base directory is the editor binary rather than the game project.
		/// </summary>
		public static string PrefabsDirectory;

		/// <summary>
		/// Instantiates a prefab from a <see cref="ComponentReference"/>-style <see cref="PrefabReference"/>
		/// (the inspector-assignable prefab field). Resolution is GUID-first via
		/// <see cref="PrefabReference.ResolvePath"/> — so it survives renaming/moving the <c>.vprefab</c> —
		/// then delegates to <see cref="LoadPrefab(string, Vector2)"/>. Returns <c>null</c> when the
		/// reference is empty or cannot be resolved.
		/// </summary>
		public Entity LoadPrefab(PrefabReference reference, Vector2 position = default, Entity.InstanceType type = Entity.InstanceType.NonSerialized)
		{
			if (!reference.IsValid)
				return null;

			var path = reference.ResolvePath();
			if (string.IsNullOrEmpty(path))
			{
				Debug.Warn($"[Scene.LoadPrefab] Could not resolve PrefabReference '{reference}'. " +
				           "Open the project in the editor to (re)generate the asset manifest.");
				return null;
			}

			return LoadPrefab(path, position, type);
		}

		/// <summary>
		/// Loads a prefab from a <c>.vprefab</c> file, instantiates it as a new <see cref="Entity"/>,
		/// adds it to this scene, and returns the entity.
		/// <para>
		/// <b>Path resolution:</b> If <paramref name="prefabPath"/> is an absolute path it is used verbatim.
		/// Otherwise it is resolved relative to <see cref="PrefabsDirectory"/> when set, or
		/// <c>AppContext.BaseDirectory/Data/Prefabs</c> in a published game.
		/// </para>
		/// <para>
		/// <b>Failure contract:</b> Returns <c>null</c> (never throws) if the file is missing or fails to
		/// parse. The reason is logged via <see cref="Debug"/>. Callers must null-check the return value.
		/// </para>
		/// <para>
		/// <b>Limitations vs editor prefab instancing:</b> <see cref="Entity.OriginalPrefabGuid"/> is always
		/// <see cref="Guid.Empty"/> at runtime because the GUID/meta system is editor-only. The entity is
		/// identified by <see cref="Entity.OriginalPrefabName"/> (the file name without extension) instead.
		/// Component references (<see cref="ComponentReference"/>, <see cref="EntityReference"/>) that cross
		/// entity boundaries are not resolved — they receive their serialised default values, matching the
		/// behaviour of entities loaded from a scene at runtime.
		/// </para>
		/// </summary>
		/// <param name="prefabPath">
		/// Absolute path to a <c>.vprefab</c> file, or a path relative to the prefabs root directory.
		/// </param>
		/// <param name="position">World-space position for the instantiated entity. Defaults to origin.</param>
		/// <returns>The newly added entity, or <c>null</c> on failure.</returns>
		public Entity LoadPrefab(string prefabPath, Vector2 position = default, Entity.InstanceType type = Entity.InstanceType.NonSerialized)
		{
			if (string.IsNullOrWhiteSpace(prefabPath))
			{
				Debug.Error("[Scene.LoadPrefab] prefabPath cannot be null or empty.");
				return null;
			}

			// Resolve to an absolute path using the same convention as LoadLevel/ScenesDirectory.
			var resolvedPath = Path.IsPathRooted(prefabPath)
				? prefabPath
				: Path.Combine(
					string.IsNullOrEmpty(PrefabsDirectory)
						? Path.Combine(AppContext.BaseDirectory, "Data", "Prefabs")
						: PrefabsDirectory,
					prefabPath);

			if (!File.Exists(resolvedPath))
			{
				Debug.Error($"[Scene.LoadPrefab] Prefab file not found: {resolvedPath}");
				return null;
			}

			PrefabData prefabData;
			try
			{
				var json = File.ReadAllText(resolvedPath);
				prefabData = AotDeserializers.DeserializePrefabData(json);

				if (prefabData.Name == null)
				{
					Debug.Error($"[Scene.LoadPrefab] Prefab at '{resolvedPath}' is missing a Name field — invalid .vprefab format.");
					return null;
				}
			}
			catch (Exception ex)
			{
				Debug.Error($"[Scene.LoadPrefab] Failed to read or parse '{resolvedPath}': {ex.Message}");
				return null;
			}

			try
			{
				
				var prefabName = Path.GetFileNameWithoutExtension(resolvedPath);

				// Convert to SceneEntityData so we can drive the shared LoadEntityData path.
				var sceneEntityData = prefabData.ToSceneEntityData(position);
				sceneEntityData.OriginalPrefabName = prefabName;
				sceneEntityData.OriginalPrefabGuid = null; // GUID is editor-only

				var entity = new Entity(prefabData.Name);
				entity.Type = type;
				LoadEntityData(entity, sceneEntityData);
				AddEntity(entity);

				// Instantiate child entities.
				if (prefabData.ChildEntities != null)
				{
					var childrenNeedingParents = new List<Entity>();

					foreach (var childData in prefabData.ChildEntities)
					{
						var childEntity = new Entity(childData.Name);
						LoadEntityData(childEntity, childData);
						childEntity.Type = type;
						AddEntity(childEntity);

						// If the parent wasn't resolved during LoadEntityData (parent not yet in
						// the scene at that point), track it for a deferred assignment pass.
						if (!string.IsNullOrEmpty(childEntity.GetData<string>("_PendingParentName")) ||
							childEntity.GetData<Guid>("_PendingParentId") != Guid.Empty)
							childrenNeedingParents.Add(childEntity);
					}

					if (childrenNeedingParents.Count > 0)
						AssignParentRelationships(childrenNeedingParents);
				}

				entity.Type = type;
				return entity;
			}
			catch (Exception ex)
			{
				Debug.Error($"[Scene.LoadPrefab] Failed to instantiate prefab '{resolvedPath}': {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Loads a level (.vscene) by name, makes it the active <see cref="Core.Scene"/>, and applies the
		/// design resolution (and optionally the display settings) configured in <see cref="ProjectSettings"/>.
		/// <para>This is the runtime entry point for level switching, e.g. <c>Scene.LoadLevel("Level2")</c>.</para>
		/// <para>Level names resolve against <see cref="ScenesDirectory"/> when set, otherwise
		/// <c>AppContext.BaseDirectory/Data/Scenes</c>. To reload the active scene, prefer
		/// <see cref="ReloadCurrentLevel"/>, which reuses the exact file the scene was loaded from.</para>
		/// </summary>
		/// <param name="levelName">The .vscene file name without extension (e.g. "MainScene").</param>
		/// <param name="applyDisplaySettings">
		/// When true, applies the ProjectSettings Display block (screen size, fullscreen, vsync) to the window.
		/// Intended for the initial load at startup; leave false for in-game level switches so the window is not reset.
		/// </param>
		/// <returns>The loaded Scene, now assigned as the active Core.Scene.</returns>
		/// <exception cref="Exception">Thrown when the level file is missing or fails to load.</exception>
		public static Scene LoadLevel(string levelName, bool applyDisplaySettings = false)
		{
			if (string.IsNullOrEmpty(levelName))
				throw new Exception("Scene.LoadLevel was called with a null or empty level name.");

			var scenesDir = string.IsNullOrEmpty(ScenesDirectory)
				? Path.Combine(AppContext.BaseDirectory, "Data", "Scenes")
				: ScenesDirectory;
			var scenePath = Path.Combine(scenesDir, levelName + ".vscene");

			return LoadLevelFromPath(scenePath, applyDisplaySettings);
		}

		/// <summary>
		/// Reloads the currently active scene from its source .vscene file — a fresh copy from disk,
		/// discarding any runtime changes. Handy for a "restart level" button or script.
		/// <para>Reloads from the exact path the scene was loaded from (<see cref="SceneData.FilePath"/>),
		/// so it works both in a published game and inside the editor regardless of the working directory.</para>
		/// <para>Like any Core.Scene assignment, the swap happens at the end of the current frame, so it
		/// is safe to call from inside a component's Update/OnStart.</para>
		/// </summary>
		/// <param name="applyDisplaySettings">See <see cref="LoadLevel"/>. Defaults to false for a reload.</param>
		/// <returns>The freshly loaded Scene.</returns>
		/// <exception cref="Exception">Thrown when the active scene was not loaded from a file.</exception>
		public static Scene ReloadCurrentLevel(bool applyDisplaySettings = true)
		{
#if EDITOR
			applyDisplaySettings = false; // let the editor handle it
#endif
			var scenePath = Core.Scene?.SceneData?.FilePath;
			if (string.IsNullOrEmpty(scenePath))
				Debug.Error("ReloadCurrentLevel: the active scene was not loaded from a file (SceneData.FilePath is empty).");

			return LoadLevelFromPath(scenePath, applyDisplaySettings);
		}

		/// <summary>
		/// Shared loader: loads a scene from a full .vscene path, makes it the active Core.Scene, and applies
		/// the display + design resolution settings from <see cref="ProjectSettings"/>.
		/// </summary>
		private static Scene LoadLevelFromPath(string scenePath, bool applyDisplaySettings = true)
		{
#if EDITOR
			applyDisplaySettings = false; // let the editor handle it
#endif
			if (!File.Exists(scenePath))
				Debug.Error($"Scene file not found: {scenePath}");

			var loadedScene = LoadFromFile(scenePath);
			if (loadedScene == null)
				Debug.Error($"Failed to load scene from: {scenePath}");

			Core.Scene = loadedScene;

			var settings = ProjectSettings.Instance;

			// Apply display settings (screen size, fullscreen, vsync). Startup only by default so that
			// in-game level switches don't resize/reset the window.
			if (applyDisplaySettings && settings?.Display != null)
			{
				Screen.SetSize(settings.Display.ScreenWidth, settings.Display.ScreenHeight);
				Screen.IsFullscreen = settings.Display.IsFullscreen;
				Screen.SynchronizeWithVerticalRetrace = settings.Display.EnableVSync;
				Screen.ApplyChanges();
			}

			// Apply the configured design resolution as the default for all scenes and to this scene.
			// We operate on loadedScene directly (not Core.Scene) since a runtime switch defers the swap.
			var design = settings?.DesignResolution;
			if (design != null && design.Width > 0 && design.Height > 0)
			{
				SetDefaultDesignResolution(design.Width, design.Height, design.ResolutionPolicy,
					design.HorizontalBleed, design.VerticalBleed);

				loadedScene.SetDesignResolution(design.Width, design.Height, design.ResolutionPolicy,
					design.HorizontalBleed, design.VerticalBleed);
			}

			return loadedScene;
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
				var sceneData = BuildSceneData();
				sceneData.FilePath = scenePath;
				sceneData.ModifiedAt = DateTime.Now;

				var jsonSettings = new Voltage.Persistence.JsonSettings
				{
					PrettyPrint = true,
					TypeNameHandling = Voltage.Persistence.TypeNameHandling.Auto,
					PreserveReferencesHandling = false
				};

				var jsonContent = Voltage.Persistence.Json.ToJson(sceneData, jsonSettings);

				var directory = Path.GetDirectoryName(scenePath);
				if (!string.IsNullOrEmpty(directory))
					Directory.CreateDirectory(directory);

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
				sceneData.Name = GetType().Name;
			}

			if (string.IsNullOrWhiteSpace(sceneData.Name))
				sceneData.Name = "Untitled Scene";

			sceneData.ClearColor = ClearColor;
			sceneData.LetterboxColor = LetterboxColor;
			sceneData.ResolutionPolicy = _resolutionPolicy.ToString();
			sceneData.DesignResolutionWidth = _designResolutionSize.X;
			sceneData.DesignResolutionHeight = _designResolutionSize.Y;
			sceneData.HorizontalBleed = _designBleedSize.X;
			sceneData.VerticalBleed = _designBleedSize.Y;
			sceneData.EnablePostProcessing = EnablePostProcessing;

			sceneData.Entities.Clear();

			foreach (var entity in Entities)
			{
				if (entity.Type == Entity.InstanceType.NonSerialized)
					continue;

				var entityData = BuildEntityData(entity);
				sceneData.Entities.Add(entityData);
			}

			sceneData.SceneComponents.Clear();

			for (var i = 0; i < _sceneComponents.Length; i++)
			{
				var sc = _sceneComponents.Buffer[i];
				if (!sc.IsSerialized)
					continue;

				var isEngineType = sc.GetType().Assembly == typeof(Component).Assembly;

				var entry = new SceneComponentDataEntry
				{
					ComponentId        = ComponentIdRegistry.GetIdForType(sc.GetType()),
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

		#region Entity capture / instantiate (copy-paste, cross-scene)

		/// <summary>
		/// Captures <paramref name="roots"/> and every descendant as scene-format data, detached from this scene:
		/// parent links point only INSIDE the captured set, so the result re-instantiates into any scene - or
		/// another process - through <see cref="InstantiateEntityData"/>.
		/// </summary>
		/// <remarks>
		/// An entity whose parent was not captured comes back as a root. Component references to entities outside
		/// the set cannot survive the move and resolve to null in the target scene.
		/// </remarks>
		public List<SceneData.SceneEntityData> CaptureEntityData(IEnumerable<Entity> roots)
		{
			var captured = new List<SceneData.SceneEntityData>();
			if (roots == null)
				return captured;

			// Parents must precede their children so instantiation can wire up in one forward pass.
			var ordered = new List<Entity>();
			var seen = new HashSet<Entity>();

			void Collect(Entity entity)
			{
				if (entity == null || entity.Type == Entity.InstanceType.NonSerialized || !seen.Add(entity))
					return;

				ordered.Add(entity);

				var children = entity.Transform.Children;
				for (var i = 0; i < children.Count; i++)
					Collect(children[i].Entity);
			}

			foreach (var root in roots)
				Collect(root);

			foreach (var entity in ordered)
			{
				var data = BuildEntityData(entity);

				// BuildEntityData derives ParentId from the SAVED SceneData by name, which is stale (or absent)
				// for a live capture. Restate it from the live parent so it agrees with Id, and drop it entirely
				// when the parent is not coming along - the link would dangle in the target scene.
				var parent = entity.Transform.Parent?.Entity;

				if (parent != null && seen.Contains(parent))
				{
					data.ParentId = parent.PersistentId;
					data.ParentEntityName = parent.Name;
				}
				else
				{
					data.ParentId = null;
					data.ParentEntityName = null;

					// Stored transform is local while parented; as a new root it must read as world.
					data.Position = entity.Transform.Position;
					data.Rotation = entity.Transform.Rotation;
					data.Scale = entity.Transform.Scale;
				}

				captured.Add(data);
			}

			return captured;
		}

		/// <summary>
		/// Instantiates data from <see cref="CaptureEntityData"/> into this scene under fresh identities, so it can
		/// be pasted alongside the originals or into a different scene. Returns the newly created root entities.
		/// </summary>
		/// <param name="entities">
		/// Captured data. Mutated in place (ids rewritten), so pass a freshly deserialized copy per paste.
		/// </param>
		/// <param name="offset">World offset applied to the roots, to keep a same-scene paste from landing exactly on top.</param>
		public List<Entity> InstantiateEntityData(List<SceneData.SceneEntityData> entities, Vector2 offset = default)
		{
			var roots = new List<Entity>();
			if (entities == null || entities.Count == 0)
				return roots;

			// One map for the WHOLE paste, not one per root: a reference from one copied entity to another must
			// land on the new copy, otherwise it silently keeps pointing at the original.
			var idMap = new Dictionary<Guid, Guid>();
			foreach (var data in entities)
			{
				if (data.Id != Guid.Empty)
					idMap[data.Id] = Guid.NewGuid();
			}

			var created = new List<(Entity Entity, SceneData.SceneEntityData Data, Guid ParentId)>();

			foreach (var data in entities)
			{
				var originalParentId = data.ParentId ?? Guid.Empty;

				data.Id = idMap.TryGetValue(data.Id, out var newId) ? newId : Guid.NewGuid();

				// Parenting is done below against the live entities we are creating. Clearing it here keeps
				// LoadEntityData from resolving a same-named entity that already exists in the target scene.
				data.ParentId = null;
				data.ParentEntityName = null;

				var entity = new Entity(data.Name);
				AddEntity(entity);
				LoadEntityData(entity, data);

				// AddEntity only queues, so earlier entities in this same paste are not in Entities yet - without
				// passing them, two pasted siblings sharing a name would both be given the same "unique" one.
				entity.Name = GetUniqueEntityName(data.Name, entity, created.Select(c => c.Entity));

				created.Add((entity, data, originalParentId));
			}

			// Keyed by the NEW id: entities[i].Id was rewritten above.
			var byNewId = new Dictionary<Guid, Entity>();
			for (var i = 0; i < created.Count; i++)
				byNewId[entities[i].Id] = created[i].Entity;

			// Re-link the hierarchy, restoring each child's stored LOCAL transform.
			foreach (var (entity, data, parentId) in created)
			{
				if (parentId != Guid.Empty && idMap.TryGetValue(parentId, out var newParentId) &&
				    byNewId.TryGetValue(newParentId, out var parentEntity))
				{
					entity.Transform.SetParent(parentEntity.Transform);
					entity.Transform.SetLocalPosition(data.Position);
					entity.Transform.SetLocalRotation(data.Rotation);
					entity.Transform.SetLocalScale(data.Scale);
				}
				else
				{
					entity.Transform.Position = data.Position + offset;
					roots.Add(entity);
				}

				// Left over from LoadEntityData's deferred-parent path; nothing consumes them outside a scene load.
				entity.RemoveData("_PendingParentId");
				entity.RemoveData("_PendingParentName");
				entity.RemoveData("_PendingLocalPosition");
				entity.RemoveData("_PendingLocalRotation");
				entity.RemoveData("_PendingLocalScale");
			}

			foreach (var root in roots)
			{
				Serialization.ComponentReferenceResolver.RemapEntitySubtree(root, idMap);
				Serialization.ComponentReferenceResolver.ResolveEntitySubtree(root, this);
			}

			return roots;
		}

		#endregion

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
				OriginalPrefabName = entity.OriginalPrefabName,
				OriginalPrefabGuid = entity.OriginalPrefabGuid != Guid.Empty
					? entity.OriginalPrefabGuid
					: (Guid?)null
			};

			// Get entity-specific data
			var entData = entity.GetEntityData();
			if (entData != null)
			{
				// Capture entries whose component type is unknown in this process (plugin not
				// installed, script missing) BEFORE clearing: they have no live component to rebuild
				// from, so a rebuild-from-live save would permanently destroy that data — e.g. a
				// teammate without a private plugin saving the scene. Unresolvable entries round-trip
				// raw; they are only ever dropped by an explicit remove in the editor.
				var preservedEntries = CollectUnresolvableEntries(entData.ComponentDataList);

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
							ComponentId = ComponentIdRegistry.GetIdForType(component.GetType()),
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
							ComponentId = ComponentIdRegistry.GetIdForType(component.GetType()),
							ComponentTypeName = component.GetType().FullName,
							ComponentName = component.Name,
							DataTypeName = null,
							Json = null
						});
					}
				}

				// Re-append the raw entries of components this process cannot resolve (see above).
				entData.ComponentDataList.AddRange(preservedEntries);

				entityData.EntityData = entData;
			}

			return entityData;
		}

		/// <summary>
		/// True when neither the entry's stable ComponentId nor its type name resolves to a loaded
		/// type — the component belongs to a plugin or script assembly that is not present in this
		/// process. Such entries are preserved verbatim across saves instead of being dropped.
		/// </summary>
		public static bool IsUnresolvableComponentEntry(ComponentDataEntry entry)
		{
			if (!string.IsNullOrEmpty(entry.ComponentId) &&
			    Serialization.ComponentIdRegistry.TryGetType(entry.ComponentId, out _))
				return false;

			if (!string.IsNullOrEmpty(entry.ComponentTypeName) && ResolveType(entry.ComponentTypeName) != null)
				return false;

			// An entry with no identity at all is garbage, not a missing plugin — don't preserve it.
			return !string.IsNullOrEmpty(entry.ComponentId) || !string.IsNullOrEmpty(entry.ComponentTypeName);
		}

		/// <summary>Returns copies of the unresolvable entries in <paramref name="entries"/> (never null).</summary>
		internal static List<ComponentDataEntry> CollectUnresolvableEntries(List<ComponentDataEntry> entries)
		{
			var preserved = new List<ComponentDataEntry>();
			if (entries == null)
				return preserved;

			foreach (var entry in entries)
			{
				if (IsUnresolvableComponentEntry(entry))
					preserved.Add(entry);
			}

			return preserved;
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

		/// <summary>
		/// Resolves the source prefab for a <see cref="Entity.InstanceType.SerializedPrefab"/> entity
		/// and returns its deserialized <see cref="PrefabData"/>, or null when the prefab cannot be found.
		/// <para>
		/// Resolution order:
		/// <list type="number">
		///   <item><description><see cref="PrefabPathResolver"/> delegate — set by the editor to use AssetDatabase GUID lookup.</description></item>
		///   <item><description>Name-based search under <see cref="PrefabsDirectory"/> (subdirectories included).</description></item>
		/// </list>
		/// </para>
		/// </summary>
		private static PrefabData? TryLoadSourcePrefab(string prefabName, Guid prefabGuid)
		{
			if (string.IsNullOrEmpty(prefabName))
				return null;

			// 1. Delegate path (editor wires AssetDatabase GUID resolver here).
			string resolvedPath = null;
			if (PrefabPathResolver != null)
				resolvedPath = PrefabPathResolver(prefabGuid, prefabName);

			// 1b. Runtime GUID lookup via the baked asset manifest (published builds, where the
			// editor's PrefabPathResolver is not wired). Resolves the prefab by its stable GUID, so
			// renaming/moving the .vprefab is transparent — the GUID never changes.
			if (string.IsNullOrEmpty(resolvedPath) && prefabGuid != Guid.Empty &&
			    Assets.AssetManifest.TryGetAbsolutePath(prefabGuid, out var byGuid))
			{
				resolvedPath = byGuid;
			}

			// 2. Name-based fallback under PrefabsDirectory (same as LoadPrefab).
			if (string.IsNullOrEmpty(resolvedPath))
			{
				var baseDir = string.IsNullOrEmpty(PrefabsDirectory)
					? Path.Combine(AppContext.BaseDirectory, "Data", "Prefabs")
					: PrefabsDirectory;

				// Direct match
				var direct = Path.Combine(baseDir, prefabName + ".vprefab");
				if (File.Exists(direct))
				{
					resolvedPath = direct;
				}
				else
				{
					// Search one level of sub-directories (mirrors SerializationManager.LoadPrefabData).
					if (Directory.Exists(baseDir))
					{
						foreach (var sub in Directory.GetDirectories(baseDir))
						{
							var candidate = Path.Combine(sub, prefabName + ".vprefab");
							if (File.Exists(candidate)) { resolvedPath = candidate; break; }
							candidate = Path.Combine(sub, prefabName + ".prefab");
							if (File.Exists(candidate)) { resolvedPath = candidate; break; }
						}
					}
				}
			}

			if (string.IsNullOrEmpty(resolvedPath) || !File.Exists(resolvedPath))
				return null;

			try
			{
				var json = File.ReadAllText(resolvedPath);
				var pd = Serialization.AotDeserializers.DeserializePrefabData(json);
				return pd.Name != null ? pd : (PrefabData?)null;
			}
			catch (Exception ex)
			{
				Debug.Warn($"[Scene.LoadEntityData] Could not load source prefab '{resolvedPath}' for override overlay: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Builds a merged <see cref="EntityData"/> for a prefab instance that uses the Phase 4b delta model.
		/// <para>
		/// Algorithm:
		/// <list type="bullet">
		///   <item><description>Start from the source prefab's component list (provides defaults).</description></item>
		///   <item><description>Remove any entries whose ComponentName is in <paramref name="removed"/>.</description></item>
		///   <item><description>Replace or append entries whose ComponentName is in <paramref name="overrides"/> with
		///     the instance's stored data.</description></item>
		///   <item><description>Components not listed in either set are inherited verbatim from the prefab.</description></item>
		/// </list>
		/// </para>
		/// </summary>
		private static EntityData MergeEntityDataWithPrefab(
			EntityData prefabEntityData,
			EntityData instanceEntityData,
			HashSet<string> overrides,
			List<string> removed)
		{
			var merged = new EntityData();

			// Build a quick lookup of the instance's override entries.
			var instanceByName = new Dictionary<string, ComponentDataEntry>(StringComparer.Ordinal);
			if (instanceEntityData?.ComponentDataList != null)
			{
				foreach (var e in instanceEntityData.ComponentDataList)
					instanceByName[e.ComponentName] = e;
			}

			var removedSet = removed != null && removed.Count > 0
				? new HashSet<string>(removed, StringComparer.Ordinal)
				: null;

			// Walk prefab entries: keep as-is, remove, or replace with override.
			if (prefabEntityData?.ComponentDataList != null)
			{
				foreach (var prefabEntry in prefabEntityData.ComponentDataList)
				{
					// Removed on instance — skip.
					if (removedSet != null && removedSet.Contains(prefabEntry.ComponentName))
						continue;

					// Overridden on instance — use instance data.
					if (overrides != null && overrides.Contains(prefabEntry.ComponentName) &&
					    instanceByName.TryGetValue(prefabEntry.ComponentName, out var overrideEntry))
					{
						merged.ComponentDataList.Add(overrideEntry);
					}
					else
					{
						// Inherited from prefab — clone to avoid shared references.
						merged.ComponentDataList.Add(new ComponentDataEntry
						{
							ComponentId        = prefabEntry.ComponentId,
								ComponentTypeName = prefabEntry.ComponentTypeName,
							ComponentName     = prefabEntry.ComponentName,
							DataTypeName      = prefabEntry.DataTypeName,
							Json              = prefabEntry.Json
						});
					}
				}
			}

			// Append instance-only added components (in overrides but not in prefab).
			var prefabNames = prefabEntityData?.ComponentDataList != null
				? new HashSet<string>(prefabEntityData.ComponentDataList.Select(e => e.ComponentName), StringComparer.Ordinal)
				: new HashSet<string>();

			if (overrides != null)
			{
				foreach (var name in overrides)
				{
					if (!prefabNames.Contains(name) && instanceByName.TryGetValue(name, out var addedEntry))
						merged.ComponentDataList.Add(addedEntry);
				}
			}

			return merged;
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
			{
				entity.OriginalPrefabName = entityData.OriginalPrefabName;
				// OriginalPrefabGuid is additive — absent in pre-Phase-3 scenes (null → Empty).
				entity.OriginalPrefabGuid = entityData.OriginalPrefabGuid ?? Guid.Empty;
			}
			else
			{
				entity.OriginalPrefabName = null;
				entity.OriginalPrefabGuid = Guid.Empty;
			}

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

			// Load entity-specific data and components.
			// Phase 4b: when PrefabOverrides is non-null this is a delta entry — merge with
			// the source prefab before applying.  Null means legacy full-data (pre-4b) entry.
			EntityData effectiveEntityData = entityData.EntityData;

			if (entityData.InstanceType == Entity.InstanceType.SerializedPrefab &&
			    entityData.PrefabOverrides != null)
			{
				var sourceGuid = entityData.OriginalPrefabGuid ?? Guid.Empty;
				var sourcePrefab = TryLoadSourcePrefab(entityData.OriginalPrefabName, sourceGuid);

				if (sourcePrefab.HasValue)
				{
					effectiveEntityData = MergeEntityDataWithPrefab(
						sourcePrefab.Value.EntityData,
						entityData.EntityData,
						entityData.PrefabOverrides,
						entityData.RemovedPrefabComponents);
				}
				else
				{
					// Source prefab missing — fall back to the stored (possibly partial) data.
					// This is graceful degradation: non-overridden fields will be absent but
					// the overridden components will load correctly.
					Debug.Warn($"[Scene] Prefab '{entityData.OriginalPrefabName}' not found for override-delta entity '{entityData.Name}'. " +
					           "Loading with stored data only. Re-save the scene after relocating the prefab.");
				}
			}

			if (effectiveEntityData != null)
			{
				// The EntityData was already deserialized by AotDeserializers — no need
				// to round-trip through Json.ToJson/FromJson. Just clone the
				// ComponentDataList entries directly to avoid shared references.
				var clonedEntityData = effectiveEntityData.Clone();
				
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
							var component = CreateComponentInstance(componentEntry.ComponentTypeName, componentEntry.ComponentId);
							if (component == null)
							{
								// Not fatal: the entity loads without it, and the raw entry is preserved
								// across saves (see CollectUnresolvableEntries) until explicitly removed.
								Debug.Warn(
									$"Could not create component '{componentEntry.ComponentTypeName}' (id: {componentEntry.ComponentId ?? "-"}) — " +
									"it may come from a plugin that is not installed. The entity loads without it; its data is preserved.");
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
		private static Component CreateComponentInstance(string componentTypeName, string componentId = null)
		{
			// GUID-first resolution: the stable [ComponentId] identity survives class/namespace
			// renames, so it takes priority over the (possibly stale) stored type name. Falls
			// through to name-based resolution for legacy entries that have no GUID.
			if (!string.IsNullOrEmpty(componentId) &&
			    Serialization.ComponentIdRegistry.TryGetType(componentId, out var guidType) &&
			    guidType?.FullName != null)
			{
				if (ComponentAotFactory.IsRegistered(guidType.FullName))
					return (Component)ComponentAotFactory.Create(guidType.FullName);
				return (Component)Activator.CreateInstance(guidType);
			}

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
		private static SceneComponent CreateSceneComponentInstance(string typeName, string componentId = null)
		{
			// GUID-first resolution — immune to class/namespace renames.
			if (!string.IsNullOrEmpty(componentId) &&
			    Serialization.ComponentIdRegistry.TryGetType(componentId, out var guidType) &&
			    guidType?.FullName != null)
			{
				if (ComponentAotFactory.IsRegistered(guidType.FullName))
					return (SceneComponent)ComponentAotFactory.Create(guidType.FullName);
				return (SceneComponent)Activator.CreateInstance(guidType);
			}

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
						var aotDataTypeName = entry.DataTypeName;

						// If the stored DataTypeName is unknown to the AOT registry, check whether
						// it is a renamed type and use the current name instead (Phase 4a rename path).
						if (!Serialization.ComponentDataAotDeserializer.IsRegistered(aotDataTypeName) &&
						    Serialization.TypeRenameRegistry.TryResolve(aotDataTypeName, out var renamedDataType))
						{
							aotDataTypeName = renamedDataType.FullName ?? aotDataTypeName;
						}

						// GUID-resolved components may carry a stale DataTypeName after a rename.
						// The live component's Data property always reports the current data type,
						// so prefer it when the stored name no longer resolves.
						if (!Serialization.ComponentDataAotDeserializer.IsRegistered(aotDataTypeName))
						{
							var liveDataType = component.Data?.GetType();
							if (liveDataType?.FullName != null &&
							    Serialization.ComponentDataAotDeserializer.IsRegistered(liveDataType.FullName))
							{
								aotDataTypeName = liveDataType.FullName;
							}
						}

						if (Serialization.ComponentDataAotDeserializer.IsRegistered(aotDataTypeName))
						{
							var cleanJson = StripTypeFieldFromJson(entry.Json);
							data = Serialization.ComponentDataAotDeserializer.TryDeserialize(aotDataTypeName, cleanJson);
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
		/// On a final miss, consults <see cref="Serialization.TypeRenameRegistry"/> so that
		/// component classes whose fully-qualified name changed (class or namespace rename) still
		/// resolve correctly from serialized scene/prefab data.
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

			// Last resort: check the rename registry (populated by [FormerlyKnownAs] at module init).
			// This is the only path that changes behavior for renamed types — all direct lookups
			// above are identical to the pre-Phase-4a code.
			if (Serialization.TypeRenameRegistry.TryResolve(typeName, out var renamedType))
			{
				Debug.Warn($"[Scene] Type '{typeName}' resolved via TypeRenameRegistry → '{renamedType.FullName}'. " +
					"Re-save the scene/prefab to update the stored type name.");
				return renamedType;
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
				if (string.IsNullOrEmpty(entry.ComponentTypeName) && string.IsNullOrEmpty(entry.ComponentId))
					continue;

				try
				{
					// Check if a SceneComponent of this type is already on the scene
					// (e.g. added via code in OnStart). If so, just apply data to it.
					// Also accept renamed types via the TypeRenameRegistry (Phase 4a).
					SceneComponent existing = null;
					for (var i = 0; i < _sceneComponents.Length; i++)
					{
						var scFull = _sceneComponents.Buffer[i].GetType().FullName;
						bool nameMatch = scFull == entry.ComponentTypeName;

						// GUID match takes priority — survives renames without any registry of old names.
						if (!nameMatch &&
						    !string.IsNullOrEmpty(entry.ComponentId) &&
						    Serialization.ComponentIdRegistry.TryGetType(entry.ComponentId, out var guidScType) &&
						    scFull == guidScType.FullName)
						{
							nameMatch = true;
						}

						// Rename-registry check: the stored name is an old name whose current type
						// matches the live SceneComponent's type.
						if (!nameMatch &&
						    Serialization.TypeRenameRegistry.TryResolve(entry.ComponentTypeName, out var resolvedScType) &&
						    scFull == resolvedScType.FullName)
						{
							nameMatch = true;
						}

						if (nameMatch && _sceneComponents.Buffer[i].Name == entry.ComponentName)
						{
							existing = _sceneComponents.Buffer[i];
							break;
						}
					}

					if (existing == null)
					{
						existing = CreateSceneComponentInstance(entry.ComponentTypeName, entry.ComponentId);
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

						// Resolve DataTypeName through the rename registry if needed (Phase 4a).
						var scDataTypeName = entry.DataTypeName;
						if (!Serialization.ComponentDataAotDeserializer.IsRegistered(scDataTypeName) &&
						    Serialization.TypeRenameRegistry.TryResolve(scDataTypeName, out var renamedScDataType))
						{
							scDataTypeName = renamedScDataType.FullName ?? scDataTypeName;
						}

						// GUID-resolved scene components may carry a stale DataTypeName after a rename;
						// the live component's Data type is always current.
						if (!Serialization.ComponentDataAotDeserializer.IsRegistered(scDataTypeName))
						{
							var liveScDataType = existing.Data?.GetType();
							if (liveScDataType?.FullName != null &&
							    Serialization.ComponentDataAotDeserializer.IsRegistered(liveScDataType.FullName))
							{
								scDataTypeName = liveScDataType.FullName;
							}
						}

						if (Serialization.ComponentDataAotDeserializer.IsRegistered(scDataTypeName))
						{
							var cleanJson = StripTypeFieldFromJson(entry.Json);
							data = Serialization.ComponentDataAotDeserializer.TryDeserialize(scDataTypeName, cleanJson);
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
