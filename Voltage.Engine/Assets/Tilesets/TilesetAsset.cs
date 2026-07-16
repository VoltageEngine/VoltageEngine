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

	/// <summary>
	/// Collision shape a solid tile contributes. Box tiles greedy-merge into large rectangles; slope tiles emit
	/// an individual triangle polygon collider (needed for ramps, which axis-aligned boxes can't express).
	/// </summary>
	public enum TileCollisionShape
	{
		Box,
		None,
		SlopeUpRight,   // solid below a hypotenuse rising left→right (low on the left)
		SlopeUpLeft,    // rising right→left (low on the right)
		SlopeDownRight, // ceiling slope: solid above a hypotenuse
		SlopeDownLeft,
	}

	/// <summary>Optional per-tile metadata. Only tiles that carry non-default data are stored.</summary>
	public class TilesetTileInfo
	{
		public int Index;
		public bool Solid;
		public string Name;

		/// <summary>
		/// Frame tile indices this tile cycles through when placed. Empty = static. The placed tile is the
		/// animation's identity; the frames are other tiles in the same atlas shown in sequence.
		/// </summary>
		public List<int> AnimationFrames = new();

		/// <summary>Seconds each animation frame is shown.</summary>
		public float AnimationFrameDuration = 0.15f;

		public bool IsAnimated => AnimationFrames != null && AnimationFrames.Count > 1;

		/// <summary>Collision shape when this tile is part of the collision mask. Box is the default.</summary>
		public TileCollisionShape CollisionShape = TileCollisionShape.Box;

		/// <summary>When true, the generated collider only blocks from its solid-face side (one-way platform).</summary>
		public bool OneWay;

		/// <summary>Autotile terrain this tile belongs to, or -1. Tiles of a terrain are chosen by neighbour match.</summary>
		public int TerrainId = -1;

		/// <summary>
		/// The neighbour signature this tile represents: an 8-bit mask of which of the 8 surrounding cells are
		/// the SAME terrain for this tile to be the right choice. Bit order: 0=N,1=NE,2=E,3=SE,4=S,5=SW,6=W,7=NW.
		/// </summary>
		public byte TerrainMask;
	}

	/// <summary>A named autotile terrain. Its member tiles are picked by <see cref="TilesetTileInfo.TerrainMask"/>.</summary>
	public class TilesetTerrain
	{
		public int Id;
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

		/// <summary>
		/// Aseprite only: when true the source is an ANIMATION — frames [<see cref="TextureAnimStart"/>..
		/// <see cref="TextureAnimEnd"/>] are packed left-to-right into a strip atlas, one tile per frame, and
		/// tile 0 is set up to cycle through them. Overrides <see cref="TextureFrame"/>.
		/// </summary>
		public bool TextureIsAsepriteAnimation;
		public int TextureAnimStart;
		public int TextureAnimEnd;

		/// <summary>Parallel atlas: must have identical pixel dimensions and grid layout as <see cref="Texture"/>.</summary>
		public AssetReference NormalMap;
		public TilesetImageSource NormalMapSource = TilesetImageSource.Png;

		/// <summary>Empty = all visible layers.</summary>
		public List<string> NormalMapLayers = new();
		public int NormalMapFrame;

		public int Columns;
		public int Rows;

		public List<TilesetTileInfo> Tiles = new();

		public List<TilesetTerrain> Terrains = new();

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
