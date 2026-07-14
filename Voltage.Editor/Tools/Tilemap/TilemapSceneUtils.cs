using System;
using System.Collections.Generic;
using System.IO;
using Voltage.Editor.Assets;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.ProjectFile;
using Voltage.Editor.Undo.Core;
using Voltage.Editor.Undo.EntityActions;
using Voltage.Tilesets;
using EngineAssetReference = Voltage.Serialization.AssetReference;

namespace Voltage.Editor.Tools.Tilemap
{
	/// <summary>Where tileset files live, and how to pick a non-colliding name.</summary>
	public static class TilesetPaths
	{
		public static string DefaultTilesetFolder()
		{
			var project = ProjectManager.Instance?.CurrentProject;
			if (project == null)
				return null;

			var folder = Path.Combine(project.ContentsFolder, "Tilesets");
			Directory.CreateDirectory(folder);
			return folder;
		}

		/// <summary>Appends a numeric suffix until the path is free.</summary>
		public static string UniquePath(string folder, string baseName, string extension)
		{
			var candidate = Path.Combine(folder, baseName + extension);
			var index = 1;

			while (File.Exists(candidate))
				candidate = Path.Combine(folder, $"{baseName}_{index++}{extension}");

			return candidate;
		}
	}

	/// <summary>Scene-level helpers shared by the palette, the tileset editor and the asset drop handler.</summary>
	public static class TilemapSceneUtils
	{
		public static List<TilemapRenderer> FindTilemaps()
		{
			var results = new List<TilemapRenderer>();
			var scene = Core.Scene;
			if (scene == null)
				return results;

			for (var i = 0; i < scene.Entities.Count; i++)
				results.AddRange(scene.Entities[i].GetComponents<TilemapRenderer>());

			return results;
		}

		/// <summary>Re-resolves the tileset on every live tilemap, so a re-save is picked up without a scene reload.</summary>
		public static void ReloadTilesetsInScene()
		{
			foreach (var map in FindTilemaps())
				map.ReloadTileset();
		}

		/// <summary>Creates an entity carrying one tilemap layer bound to <paramref name="tileset"/>, selects it, and pushes an undo step.</summary>
		/// <param name="worldPosition">
		/// Ignored by design: a layer is always anchored at the world origin, so every layer shares one cell lattice and
		/// the grid sits on the same world coordinates on every machine and window size.
		/// </param>
		public static TilemapRenderer CreateTilemapLayer(EngineAssetReference tileset,
			Microsoft.Xna.Framework.Vector2? worldPosition = null, string name = null)
		{
			var scene = Core.Scene;
			if (scene == null)
				return null;

			var baseName = name
			               ?? (string.IsNullOrEmpty(tileset.AssetName) ? "Tilemap" : $"{tileset.AssetName} Layer");

			var entityName = scene.GetUniqueEntityName(baseName, null);
			var entity = new Entity(entityName, Entity.InstanceType.Serialized);
			entity.Transform.Position = Microsoft.Xna.Framework.Vector2.Zero;

			scene.AddEntity(entity);

			var map = entity.AddComponent<TilemapRenderer>();

			// Component defaults IsSerialized to false and Scene.BuildEntityData SKIPS those, so without this the
			// layer saves as an empty entity and every painted tile is lost.
			map.SetSerialized(true);

			map.Tileset = tileset;
			map.ApplyDeferredMaterial();

			EditorChangeTracker.PushUndo(
				new EntityCreateDeleteUndoAction(scene, entity, wasCreated: true, $"Create Tilemap '{entityName}'"),
				entity,
				$"Create Tilemap '{entityName}'");

			var manager = Core.GetGlobalManager<ImGuiManager>();
			manager?.SceneGraphWindow.EntityPane.SetSelectedEntity(entity, false);
			manager?.MainEntityInspectorWindow.DelayedSetEntity(entity);

			return map;
		}

		/// <summary>Builds an engine asset reference for a tileset file on disk.</summary>
		public static EngineAssetReference ReferenceFor(string absolutePath)
		{
			var db = AssetDatabase.Instance;
			if (db == null)
				return default;

			return TileAssetUtils.ToEngineReference(db.GetReference(absolutePath));
		}
	}
}
