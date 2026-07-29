using System.Collections.Generic;
using System.IO;
using Voltage.Editor.DebugUtils;
using Voltage.Tilesets;

namespace Voltage.Editor.Tools.Tilemap
{
	/// <summary>
	/// Re-reads a tileset's source images from disk without a re-import. Image references are GUID-based, so a
	/// rename never breaks them and no file picking is needed - only the cached parse of the pixels is stale.
	/// </summary>
	public static class TilesetSourceSync
	{
		/// <summary>
		/// Drops the cached parse of <paramref name="asset"/>'s images, then re-resolves the tileset and every live
		/// map using it. Tile metadata is untouched; only the pixels reload. False when there is nothing to sync.
		/// </summary>
		public static bool Sync(TilesetAsset asset, string tilesetPath) =>
			Sync(asset, tilesetPath, saveLayerChanges: true, out _);

		/// <param name="saveLayerChanges">
		/// Whether a rewritten layer list may be saved straight back. False for a caller editing the asset itself -
		/// saving there would commit its unsaved edits too, so it marks itself dirty instead.
		/// </param>
		public static bool Sync(TilesetAsset asset, string tilesetPath, bool saveLayerChanges,
			out bool layerListChanged)
		{
			layerListChanged = false;

			if (asset == null || !asset.Texture.IsValid)
				return false;

			var path = asset.Texture.ResolvePath();
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
			{
				EditorDebug.Log(
					$"Tileset: the source image for '{asset.Name}' could not be found on disk - nothing to sync.",
					"Tileset");

				return false;
			}

			EvictSourceCache(path);
			EvictSourceCache(asset.NormalMap.ResolvePath());

			// Settle the layer lists before anything rebuilds - the reload below re-reads the .vtileset from disk.
			layerListChanged = ReconcileLayers(asset, path);

			if (layerListChanged && saveLayerChanges && !string.IsNullOrEmpty(tilesetPath))
				TilesetAssetIO.Save(asset, tilesetPath);

			// Live maps composite from the source too, so drop the resolved tileset and re-resolve them.
			if (!string.IsNullOrEmpty(tilesetPath))
			{
				TilesetRuntime.Invalidate(tilesetPath);
				TilemapSceneUtils.ReloadTilesetsInScene();
			}

			EditorDebug.Log($"Synced '{Path.GetFileName(path)}' from disk.", "Tileset");
			return true;
		}

		/// <summary>
		/// Settles saved keep-lists against the file. One naming every layer switches to following the file; one
		/// leaving a layer out is only reported, since the omission may be deliberate.
		/// </summary>
		private static bool ReconcileLayers(TilesetAsset asset, string texturePath)
		{
			var changed = false;

			if (asset.TextureSource == TilesetImageSource.Aseprite && !asset.TextureSyncsNewLayers &&
			    FollowAllLayers(asset.TextureLayers, texturePath, asset.Name, "source image"))
			{
				asset.TextureSyncsNewLayers = true;
				changed = true;
			}

			if (asset.NormalMapSource == TilesetImageSource.Aseprite && !asset.NormalMapSyncsNewLayers &&
			    FollowAllLayers(asset.NormalMapLayers, asset.NormalMap.ResolvePath(), asset.Name, "normal map"))
			{
				asset.NormalMapSyncsNewLayers = true;
				changed = true;
			}

			return changed;
		}

		/// <summary>True when <paramref name="layers"/> was cleared because it already named every visible layer.</summary>
		private static bool FollowAllLayers(List<string> layers, string imagePath, string assetName, string slot)
		{
			if (layers == null || layers.Count == 0 || string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
				return false;

			var content = Core.Scene?.Content ?? Core.Content;

			var file = content?.LoadAsepriteFile(imagePath);
			if (file == null)
				return false;

			var unused = new List<string>();
			TilesetRuntime.CollectUnusedLayers(file, layers, unused);

			if (unused.Count == 0)
			{
				layers.Clear();
				EditorDebug.Log(
					$"Tileset '{assetName}': the {slot} layer list named every layer, so it now follows the file - " +
					"layers added later are picked up on their own.",
					"Tileset");

				return true;
			}

			EditorDebug.Log(
				$"Tileset '{assetName}': the {slot} does not include the layer(s) {string.Join(", ", unused)}, so " +
				"anything drawn on them is NOT synced. Tick them under Edit Tileset > Layers, or turn on " +
				"'Sync layers added later' there to follow the file from now on.",
				"Tileset");

			return false;
		}

		/// <summary>Drops the cached parse of a source image. Both managers: TilesetRuntime prefers the scene's.</summary>
		public static void EvictSourceCache(string absolutePath)
		{
			if (string.IsNullOrEmpty(absolutePath))
				return;

			Core.Content?.EvictCachedAsset(absolutePath);
			Core.Scene?.Content?.EvictCachedAsset(absolutePath);
		}
	}
}
