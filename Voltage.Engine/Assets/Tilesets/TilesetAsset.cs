using System.Collections.Generic;
using Microsoft.Xna.Framework;
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
		Circle,         // inscribed circle, radius = half the shorter tile edge
		Custom,         // a named polygon from the tileset's CustomColliders, referenced by CustomColliderName
	}

	/// <summary>A named custom collision polygon, reusable across tiles. Points are normalized 0..1, clockwise.</summary>
	public class TilesetCollider
	{
		public string Name;
		public List<Vector2> Points = new();
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

		/// <summary>Which <see cref="TilesetAsset.CustomColliders"/> entry to use when <see cref="CollisionShape"/> is Custom.</summary>
		public string CustomColliderName;

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

		/// <summary>Named custom collision polygons, reusable across this tileset's tiles.</summary>
		public List<TilesetCollider> CustomColliders = new();

		public bool HasNormalMap => NormalMap.IsValid;

		public int TileCount => Columns * Rows;

		// O(1) lookup index, rebuilt when Tiles.Count changes. The editor overlays call this per atlas slot per frame.
		private Dictionary<int, TilesetTileInfo> _tileInfoIndex;
		private int _tileInfoIndexCount = -1;

		public TilesetTileInfo GetTileInfo(int index)
		{
			if (_tileInfoIndex == null || _tileInfoIndexCount != Tiles.Count)
			{
				_tileInfoIndex ??= new Dictionary<int, TilesetTileInfo>(Tiles.Count);
				_tileInfoIndex.Clear();
				for (var i = 0; i < Tiles.Count; i++)
					_tileInfoIndex[Tiles[i].Index] = Tiles[i];

				_tileInfoIndexCount = Tiles.Count;
			}

			return _tileInfoIndex.TryGetValue(index, out var info) ? info : null;
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

		public TilesetCollider GetCustomCollider(string name)
		{
			if (string.IsNullOrEmpty(name))
				return null;

			for (var i = 0; i < CustomColliders.Count; i++)
			{
				if (CustomColliders[i].Name == name)
					return CustomColliders[i];
			}

			return null;
		}
	}
}
