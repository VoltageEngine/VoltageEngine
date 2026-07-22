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
		public static bool Sync(TilesetAsset asset, string tilesetPath)
		{
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

			// Live maps composite from the source too, so drop the resolved tileset and re-resolve them.
			if (!string.IsNullOrEmpty(tilesetPath))
			{
				TilesetRuntime.Invalidate(tilesetPath);
				TilemapSceneUtils.ReloadTilesetsInScene();
			}

			EditorDebug.Log($"Synced '{Path.GetFileName(path)}' from disk.", "Tileset");
			return true;
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
