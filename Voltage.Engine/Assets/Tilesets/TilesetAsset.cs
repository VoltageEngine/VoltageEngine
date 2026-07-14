using System.Collections.Generic;
using Voltage.Serialization;

namespace Voltage.Tilesets
{
	/// <summary>How the source image for a tileset (or its normal map) is authored.</summary>
	public enum TilesetImageSource
	{
		Png,
		Aseprite,
	}

	/// <summary>Optional per-tile metadata. Only tiles that carry non-default data are stored.</summary>
	public class TilesetTileInfo
	{
		public int Index;
		public bool Solid;
		public string Name;
	}

	/// <summary>A grid slicing of one source image into equally sized tiles, plus an optional normal-map atlas.</summary>
	public class TilesetAsset
	{
		public string Name;

		public int TileWidth = 16;
		public int TileHeight = 16;

		public int Spacing;
		public int Margin;

		public AssetReference Texture;
		public TilesetImageSource TextureSource = TilesetImageSource.Png;

		/// <summary>Aseprite only: layers to flatten into the atlas. Empty = all visible layers.</summary>
		public List<string> TextureLayers = new();

		/// <summary>Aseprite only: zero-based frame to flatten.</summary>
		public int TextureFrame;

		/// <summary>Parallel atlas: must have identical pixel dimensions and grid layout as <see cref="Texture"/>.</summary>
		public AssetReference NormalMap;
		public TilesetImageSource NormalMapSource = TilesetImageSource.Png;

		/// <summary>Empty = all visible layers.</summary>
		public List<string> NormalMapLayers = new();
		public int NormalMapFrame;

		public int Columns;
		public int Rows;

		public List<TilesetTileInfo> Tiles = new();

		public bool HasNormalMap => NormalMap.IsValid;

		public int TileCount => Columns * Rows;

		public TilesetTileInfo GetTileInfo(int index)
		{
			for (var i = 0; i < Tiles.Count; i++)
			{
				if (Tiles[i].Index == index)
					return Tiles[i];
			}

			return null;
		}

		public TilesetTileInfo GetOrCreateTileInfo(int index)
		{
			var info = GetTileInfo(index);
			if (info == null)
			{
				info = new TilesetTileInfo { Index = index };
				Tiles.Add(info);
			}

			return info;
		}

		public bool IsSolid(int index) => GetTileInfo(index)?.Solid ?? false;
	}
}
