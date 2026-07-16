using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Voltage.DeferredLighting;
using Voltage.Materials;
using Voltage.Serialization;
using Voltage.Tilesets;

namespace Voltage
{
	/// <summary>Renders one layer of a tilemap: tiles from a single tileset, in one texture, material and batch.</summary>
	/// <remarks>Cells store <c>tileIndex + 1</c>, so <c>0</c> means empty.</remarks>
	[ComponentId("tilemap_renderer")]
	public partial class TilemapRenderer : RenderableComponent
	{
		/// <summary>Tiles per chunk edge. A chunk holds ChunkSize * ChunkSize cells.</summary>
		public const int ChunkSize = 32;

		public AssetReference Tileset
		{
			get => _data.Tileset;
			set
			{
				_data.Tileset = value;
				_tileset = null;
				_tilesetResolved = false;
				_areBoundsDirty = true;

				ApplyDeferredMaterial();
			}
		}

		public TilesetRuntime ResolvedTileset
		{
			get
			{
				if (!_tilesetResolved)
				{
					_tileset = TilesetRuntime.Get(_data.Tileset);
					_tilesetResolved = true;
				}

				return _tileset;
			}
		}

		public int TileWidth => ResolvedTileset?.TileWidth ?? _data.FallbackTileWidth;
		public int TileHeight => ResolvedTileset?.TileHeight ?? _data.FallbackTileHeight;

		/// <summary>Number of non-empty cells.</summary>
		public int TileCount { get; private set; }

		/// <summary>Inclusive bounding box of the painted cells, in tile coordinates.</summary>
		public Rectangle TileExtents
		{
			get
			{
				EnsureExtents();

				return _hasTiles
					? new Rectangle(_minTileX, _minTileY, _maxTileX - _minTileX + 1, _maxTileY - _minTileY + 1)
					: Rectangle.Empty;
			}
		}

		private TilemapRendererComponentData _data = new();

		private readonly Dictionary<long, int[]> _chunks = new();

		// Tiles stacked above a cell's base tile, bottom-to-top. Only stacked cells appear here. Cells hold a
		// stack because a partially transparent tile must composite with what is under it, not replace it.
		private readonly Dictionary<long, List<int>> _stacks = new();

		/// <summary>How many tiles may sit in one cell, base included.</summary>
		public const int MaxStackHeight = 8;

		private TilesetRuntime _tileset;
		private bool _tilesetResolved;

		private bool _hasTiles;
		private bool _extentsDirty;
		private int _minTileX, _minTileY, _maxTileX, _maxTileY;

		/// <summary>Serialized form of a <see cref="TilemapRenderer"/>.</summary>
		public class TilemapRendererComponentData : ComponentData
		{
			public AssetReference Tileset;

			/// <summary>Tile size used only while the tileset is unresolved.</summary>
			public int FallbackTileWidth = 16;
			public int FallbackTileHeight = 16;

			/// <summary>Chunk coordinates, packed x0,y0,x1,y1,… — parallel to <see cref="ChunkData"/>.</summary>
			public int[] ChunkCoords = Array.Empty<int>();

			/// <summary>RLE cells, one string per chunk.</summary>
			public string[] ChunkData = Array.Empty<string>();

			/// <summary>Cell coordinates of stacked cells, packed x0,y0,x1,y1,… — parallel to <see cref="StackTiles"/>.</summary>
			public int[] StackCoords = Array.Empty<int>();

			/// <summary>Tiles stacked above the base of each stacked cell, comma-separated, bottom-to-top.</summary>
			public string[] StackTiles = Array.Empty<string>();

			/// <summary>Cell coordinates of oriented cells, packed x0,y0,… — parallel to <see cref="OrientationValues"/>.</summary>
			public int[] OrientationCoords = Array.Empty<int>();

			/// <summary>Orientation byte per oriented cell (rotation + flip flags).</summary>
			public byte[] OrientationValues = Array.Empty<byte>();

			/// <summary>Chunk coordinates of collision cells, packed x0,y0,… — parallel to <see cref="CollisionData"/>.</summary>
			public int[] CollisionCoords = Array.Empty<int>();

			/// <summary>RLE collision bitmask (0/1), one string per chunk.</summary>
			public string[] CollisionData = Array.Empty<string>();

			public int PhysicsLayer = 1 << 0;
			public int CollidesWithLayers = Physics.AllLayers;
			public bool IsTrigger;
			public bool AutoBuildColliders = true;

			public float LayerDepth;
			public int RenderLayer;
			public Vector2 LocalOffset;
			public Color Color = Color.White;
		}

		/// <summary>Physics layer the generated colliders sit on.</summary>
		public int PhysicsLayer
		{
			get => _data.PhysicsLayer;
			set => _data.PhysicsLayer = value;
		}

		/// <summary>Layer mask the generated colliders collide with.</summary>
		public int CollidesWithLayers
		{
			get => _data.CollidesWithLayers;
			set => _data.CollidesWithLayers = value;
		}

		/// <summary>Whether the generated colliders are triggers (events only, no blocking).</summary>
		public bool IsTrigger
		{
			get => _data.IsTrigger;
			set => _data.IsTrigger = value;
		}

		/// <summary>Whether colliders are built automatically when the layer starts.</summary>
		public bool AutoBuildColliders
		{
			get => _data.AutoBuildColliders;
			set => _data.AutoBuildColliders = value;
		}

		public override ComponentData Data
		{
			get
			{
				_data ??= new TilemapRendererComponentData();

				_data.Enabled = Enabled;
				_data.LayerDepth = LayerDepth;
				_data.RenderLayer = RenderLayer;
				_data.LocalOffset = LocalOffset;
				_data.Color = Color;

				if (_tileset != null)
				{
					_data.FallbackTileWidth = _tileset.TileWidth;
					_data.FallbackTileHeight = _tileset.TileHeight;
				}

				SaveChunksTo(_data);
				return _data;
			}
			set
			{
				if (value is not TilemapRendererComponentData data)
					return;

				_data = data;

				Enabled = data.Enabled;
				LayerDepth = data.LayerDepth;
				RenderLayer = data.RenderLayer;
				LocalOffset = data.LocalOffset;
				Color = data.Color;

				_tileset = null;
				_tilesetResolved = false;

				LoadChunksFrom(data);
			}
		}

		public override RectangleF Bounds
		{
			get
			{
				if (_areBoundsDirty)
				{
					EnsureExtents();

					if (!_hasTiles)
					{
						// An empty layer still needs a non-degenerate box so it stays pickable in the editor.
						_bounds.CalculateBounds(Entity.Transform.Position, _localOffset, Vector2.Zero,
							Entity.Transform.Scale, Entity.Transform.Rotation, TileWidth, TileHeight);
					}
					else
					{
						var tw = TileWidth;
						var th = TileHeight;

						// CalculateBounds scales `origin` but not `localOffset`, so the content's top-left is
						// expressed as a negative origin.
						var origin = new Vector2(-_minTileX * tw, -_minTileY * th);
						var width = (_maxTileX - _minTileX + 1) * tw;
						var height = (_maxTileY - _minTileY + 1) * th;

						_bounds.CalculateBounds(Entity.Transform.Position, _localOffset, origin,
							Entity.Transform.Scale, Entity.Transform.Rotation, width, height);
					}

					_areBoundsDirty = false;
				}

				return _bounds;
			}
		}

		public override void OnStart()
		{
			ApplyDeferredMaterial();
			_areBoundsDirty = true;

			if (AutoBuildColliders)
				RebuildColliders();
		}

		public override void OnRemovedFromEntity() => RemoveColliders();

		public override void OnEntityTransformChanged(Transform.Component comp)
		{
			_areBoundsDirty = true;

			// Colliders are placed relative to the entity via LocalOffset, but rotation/scale changes need a
			// fresh merge; a plain move is handled by the collider following the transform.
			if (_colliders != null)
				RebuildColliders();
		}

		/// <summary>Keeps the material in step with the scene's renderer and the tileset's normal atlas.</summary>
		/// <remarks>
		/// In a deferred scene tiles must ALWAYS get a <see cref="DeferredSpriteMaterial"/> — falling back to
		/// <see cref="DeferredLightingRenderer.NullNormalMapTexture"/> when the tileset has no normal atlas.
		/// Without a normal in the g-buffer the tiles render black.
		/// </remarks>
		public void ApplyDeferredMaterial()
		{
			if (Entity?.Scene == null)
				return;

			if (Entity.Scene.GetRenderer<DeferredLightingRenderer>() == null)
			{
				if (Material is DeferredSpriteMaterial)
					Material = null;

				return;
			}

			var normalMap = ResolvedTileset?.NormalMap ?? DeferredLightingRenderer.NullNormalMapTexture;

			SetMaterial(new DeferredSpriteMaterial(normalMap));
		}

		/// <summary>Forces the tileset to be resolved again — call after the tileset asset is re-saved.</summary>
		public void ReloadTileset()
		{
			_tileset = null;
			_tilesetResolved = false;
			_areBoundsDirty = true;
			ApplyDeferredMaterial();
		}

		#region Tile access

		private static long Key(int chunkX, int chunkY) =>
			((long)chunkX << 32) ^ (uint)chunkY;

		// Floor-divide, so negative tile coordinates map to the correct chunk.
		private static int ChunkOf(int tile) =>
			tile >= 0 ? tile / ChunkSize : (tile - ChunkSize + 1) / ChunkSize;

		private static int LocalOf(int tile)
		{
			var local = tile % ChunkSize;
			return local < 0 ? local + ChunkSize : local;
		}

		// Packs a cell coordinate (not a chunk coordinate) into a stack-dictionary key.
		private static long CellKey(int tileX, int tileY) =>
			((long)tileX << 32) ^ (uint)tileY;

		/// <summary>The base (bottom-most) tile of a cell, or -1 when empty.</summary>
		public int GetBaseTile(int tileX, int tileY)
		{
			if (!_chunks.TryGetValue(Key(ChunkOf(tileX), ChunkOf(tileY)), out var chunk))
				return -1;

			return chunk[LocalOf(tileY) * ChunkSize + LocalOf(tileX)] - 1;
		}

		/// <summary>The top-most tile of a cell, or -1 when empty.</summary>
		public int GetTile(int tileX, int tileY)
		{
			if (_stacks.Count > 0 && _stacks.TryGetValue(CellKey(tileX, tileY), out var stack) && stack.Count > 0)
				return stack[^1];

			return GetBaseTile(tileX, tileY);
		}

		public bool HasTile(int tileX, int tileY) => GetBaseTile(tileX, tileY) >= 0;

		/// <summary>Everything in a cell, bottom-to-top. Empty array when the cell is empty.</summary>
		public int[] GetCellStack(int tileX, int tileY)
		{
			var baseTile = GetBaseTile(tileX, tileY);
			if (baseTile < 0)
				return Array.Empty<int>();

			if (_stacks.Count == 0 || !_stacks.TryGetValue(CellKey(tileX, tileY), out var stack) || stack.Count == 0)
				return new[] { baseTile };

			var result = new int[stack.Count + 1];
			result[0] = baseTile;
			stack.CopyTo(result, 1);
			return result;
		}

		/// <summary>Replaces a cell's entire contents. An empty/null stack erases the cell.</summary>
		public void SetCellStack(int tileX, int tileY, int[] tiles)
		{
			if (tiles == null || tiles.Length == 0)
			{
				SetTile(tileX, tileY, -1);
				return;
			}

			SetTile(tileX, tileY, tiles[0]);

			if (tiles.Length == 1)
				return;

			var stack = new List<int>(tiles.Length - 1);
			for (var i = 1; i < tiles.Length; i++)
				stack.Add(tiles[i]);

			_stacks[CellKey(tileX, tileY)] = stack;
		}

		/// <summary>Lays a tile on top of whatever is already in the cell. Falls back to <see cref="SetTile"/> when the cell is empty.</summary>
		public void PushTile(int tileX, int tileY, int tileIndex)
		{
			if (tileIndex < 0)
				return;

			if (GetBaseTile(tileX, tileY) < 0)
			{
				SetTile(tileX, tileY, tileIndex);
				return;
			}

			var key = CellKey(tileX, tileY);
			if (!_stacks.TryGetValue(key, out var stack))
			{
				stack = new List<int>(1);
				_stacks[key] = stack;
			}

			if (stack.Count > 0 ? stack[^1] == tileIndex : GetBaseTile(tileX, tileY) == tileIndex)
				return;

			if (stack.Count + 1 >= MaxStackHeight)
				return;

			stack.Add(tileIndex);
		}

		/// <summary>Paints a cell, discarding anything stacked in it. A <paramref name="tileIndex"/> below zero erases.</summary>
		public void SetTile(int tileX, int tileY, int tileIndex)
		{
			var key = Key(ChunkOf(tileX), ChunkOf(tileY));
			var slot = LocalOf(tileY) * ChunkSize + LocalOf(tileX);

			if (_stacks.Count > 0)
				_stacks.Remove(CellKey(tileX, tileY));

			if (tileIndex < 0)
			{
				if (_cellOrientation.Count > 0)
					_cellOrientation.Remove(CellKey(tileX, tileY));

				if (!_chunks.TryGetValue(key, out var existing))
					return;

				if (existing[slot] == 0)
					return;

				existing[slot] = 0;
				TileCount--;
				_areBoundsDirty = true;

				// Only mark the extents stale: a rescan is O(tiles), and erasing along an edge would otherwise
				// pay it per cell. Deferring collapses a whole drag into one rescan when Bounds next reads them.
				if (tileX == _minTileX || tileX == _maxTileX || tileY == _minTileY || tileY == _maxTileY)
					_extentsDirty = true;

				return;
			}

			if (!_chunks.TryGetValue(key, out var chunk))
			{
				chunk = new int[ChunkSize * ChunkSize];
				_chunks[key] = chunk;
			}

			if (chunk[slot] == 0)
				TileCount++;

			chunk[slot] = tileIndex + 1;
			GrowExtents(tileX, tileY);
			_areBoundsDirty = true;
		}

		public void EraseTile(int tileX, int tileY) => SetTile(tileX, tileY, -1);

		public void ClearAllTiles()
		{
			_chunks.Clear();
			_stacks.Clear();
			_cellOrientation.Clear();
			TileCount = 0;
			_hasTiles = false;
			_extentsDirty = false;
			_areBoundsDirty = true;
		}

		/// <summary>Every painted cell, as (tileX, tileY, tileIndex).</summary>
		public IEnumerable<(int X, int Y, int TileIndex)> EnumerateTiles()
		{
			foreach (var (key, chunk) in _chunks)
			{
				var baseX = (int)(key >> 32) * ChunkSize;
				var baseY = (int)(uint)key * ChunkSize;

				for (var i = 0; i < chunk.Length; i++)
				{
					if (chunk[i] == 0)
						continue;

					yield return (baseX + i % ChunkSize, baseY + i / ChunkSize, chunk[i] - 1);
				}
			}
		}

		private void GrowExtents(int tileX, int tileY)
		{
			if (!_hasTiles)
			{
				_hasTiles = true;
				_minTileX = _maxTileX = tileX;
				_minTileY = _maxTileY = tileY;
				return;
			}

			if (tileX < _minTileX) _minTileX = tileX;
			if (tileX > _maxTileX) _maxTileX = tileX;
			if (tileY < _minTileY) _minTileY = tileY;
			if (tileY > _maxTileY) _maxTileY = tileY;
		}

		private void EnsureExtents()
		{
			if (!_extentsDirty)
				return;

			_extentsDirty = false;
			RecomputeExtents();
		}

		private void RecomputeExtents()
		{
			_hasTiles = false;
			TileCount = 0;

			foreach (var (key, chunk) in _chunks)
			{
				var baseX = (int)(key >> 32) * ChunkSize;
				var baseY = (int)(uint)key * ChunkSize;

				for (var i = 0; i < chunk.Length; i++)
				{
					if (chunk[i] == 0)
						continue;

					TileCount++;
					GrowExtents(baseX + i % ChunkSize, baseY + i / ChunkSize);
				}
			}
		}

		#endregion

		#region Coordinate conversion

		/// <summary>World point => tile coordinate.</summary>
		public Point WorldToTile(Vector2 worldPosition)
		{
			var local = WorldToLocal(worldPosition);
			return new Point(
				(int)Math.Floor(local.X / Math.Max(1, TileWidth)),
				(int)Math.Floor(local.Y / Math.Max(1, TileHeight)));
		}

		/// <summary>World position of a tile's top-left corner.</summary>
		public Vector2 TileToWorld(int tileX, int tileY) =>
			LocalToWorld(new Vector2(tileX * TileWidth, tileY * TileHeight));

		/// <summary>Maps a point in the map's local tile-pixel space to world space.</summary>
		public Vector2 LocalTileSpaceToWorld(Vector2 localTilePixels) => LocalToWorld(localTilePixels);

		private Vector2 WorldToLocal(Vector2 world)
		{
			var t = Entity.Transform;
			var v = world - t.Position;

			if (t.Rotation != 0f)
			{
				var cos = (float)Math.Cos(-t.Rotation);
				var sin = (float)Math.Sin(-t.Rotation);
				v = new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
			}

			v -= _localOffset;

			var scale = t.Scale;
			return new Vector2(
				scale.X != 0f ? v.X / scale.X : 0f,
				scale.Y != 0f ? v.Y / scale.Y : 0f);
		}

		private Vector2 LocalToWorld(Vector2 local)
		{
			var t = Entity.Transform;
			var v = local * t.Scale + _localOffset;

			if (t.Rotation != 0f)
			{
				var cos = (float)Math.Cos(t.Rotation);
				var sin = (float)Math.Sin(t.Rotation);
				v = new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
			}

			return v + t.Position;
		}

		#endregion

		#region Rendering

		// The entity transform, snapshotted once per frame: Transform.Position/Rotation/Scale each run a
		// hierarchy dirty-check on every get, which is pure overhead when read per tile.
		private readonly struct TileTransform
		{
			private readonly Vector2 _position;
			private readonly Vector2 _offset;
			private readonly Vector2 _scale;
			private readonly float _cos;
			private readonly float _sin;
			private readonly bool _rotated;

			public TileTransform(Vector2 position, Vector2 offset, Vector2 scale, float rotation)
			{
				_position = position;
				_offset = offset;
				_scale = scale;
				_rotated = rotation != 0f;
				_cos = _rotated ? (float)Math.Cos(rotation) : 1f;
				_sin = _rotated ? (float)Math.Sin(rotation) : 0f;
			}

			public Vector2 ToWorld(float localX, float localY)
			{
				var x = localX * _scale.X + _offset.X;
				var y = localY * _scale.Y + _offset.Y;

				if (_rotated)
				{
					var rx = x * _cos - y * _sin;
					y = x * _sin + y * _cos;
					x = rx;
				}

				return new Vector2(x + _position.X, y + _position.Y);
			}
		}

		public override void Render(Batcher batcher, Camera camera)
		{
			var tileset = ResolvedTileset;
			if (tileset?.Texture == null || _chunks.Count == 0)
				return;

			var t = Entity.Transform;
			var rotation = t.Rotation;
			var scale = t.Scale;
			var xform = new TileTransform(t.Position, _localOffset, scale, rotation);

			var tw = tileset.TileWidth;
			var th = tileset.TileHeight;
			var cameraBounds = camera.Bounds;

			var texture = tileset.Texture;
			var rects = tileset.SourceRects;
			var rectCount = rects.Length;
			var color = Color;
			var layerDepth = LayerDepth;

			// Animated tilesets remap each placed index to its current frame; sampled once per frame.
			var animated = tileset.HasAnimations;
			var animTime = animated ? Voltage.Utils.Time.TotalTime : 0f;

			if (rotation != 0f)
			{
				// Rotated maps have no axis-aligned tile range, so test every chunk.
				foreach (var (key, chunk) in _chunks)
				{
					var baseX = (int)(key >> 32) * ChunkSize;
					var baseY = (int)(uint)key * ChunkSize;

					if (!ChunkIntersectsCamera(xform, baseX, baseY, tw, th, cameraBounds))
						continue;

					DrawChunk(batcher, xform, chunk, baseX, baseY, tw, th,
						0, ChunkSize, 0, ChunkSize,
						tileset, animated, animTime,
						texture, rects, rectCount, color, rotation, scale, layerDepth);
				}

				return;
			}

			var visibleMin = WorldToTile(new Vector2(cameraBounds.X, cameraBounds.Y));
			var visibleMax = WorldToTile(new Vector2(cameraBounds.Right, cameraBounds.Bottom));

			var chunkMinX = ChunkOf(visibleMin.X);
			var chunkMinY = ChunkOf(visibleMin.Y);
			var chunkMaxX = ChunkOf(visibleMax.X);
			var chunkMaxY = ChunkOf(visibleMax.Y);

			for (var cy = chunkMinY; cy <= chunkMaxY; cy++)
			{
				for (var cx = chunkMinX; cx <= chunkMaxX; cx++)
				{
					if (!_chunks.TryGetValue(Key(cx, cy), out var chunk))
						continue;

					var baseX = cx * ChunkSize;
					var baseY = cy * ChunkSize;

					var xStart = Math.Max(0, visibleMin.X - baseX);
					var yStart = Math.Max(0, visibleMin.Y - baseY);
					var xEnd = Math.Min(ChunkSize, visibleMax.X - baseX + 1);
					var yEnd = Math.Min(ChunkSize, visibleMax.Y - baseY + 1);

					DrawChunk(batcher, xform, chunk, baseX, baseY, tw, th,
						xStart, xEnd, yStart, yEnd,
						tileset, animated, animTime,
						texture, rects, rectCount, color, rotation, scale, layerDepth);
				}
			}
		}

		private void DrawChunk(Batcher batcher, in TileTransform xform, int[] chunk, int baseX, int baseY,
			int tw, int th, int xStart, int xEnd, int yStart, int yEnd,
			TilesetRuntime tileset, bool animated, float animTime,
			Texture2D texture, Rectangle[] rects, int rectCount, Color color, float rotation, Vector2 scale,
			float layerDepth)
		{
			var hasStacks = _stacks.Count > 0;
			var hasOrientation = _cellOrientation.Count > 0;

			for (var y = yStart; y < yEnd; y++)
			{
				var row = y * ChunkSize;
				var tileY = baseY + y;
				var worldY = tileY * th;

				for (var x = xStart; x < xEnd; x++)
				{
					var cell = chunk[row + x];
					if (cell == 0)
						continue;

					var tileIndex = cell - 1;
					var tileX = baseX + x;

					// Oriented cells are rare, so the common path stays a plain top-left draw with no per-cell
					// dictionary lookup.
					byte orientation = 0;
					if (hasOrientation)
						_cellOrientation.TryGetValue(CellKey(tileX, tileY), out orientation);

					var world = xform.ToWorld(tileX * tw, worldY);

					var baseFrame = animated ? tileset.ResolveFrame(tileIndex, animTime) : tileIndex;
					if ((uint)baseFrame < (uint)rectCount)
						DrawTile(batcher, texture, rects[baseFrame], world, xform, tileX, tileY, tw, th,
							color, rotation, scale, orientation, layerDepth);

					if (!hasStacks || !_stacks.TryGetValue(CellKey(tileX, tileY), out var stack))
						continue;

					for (var i = 0; i < stack.Count; i++)
					{
						var stacked = animated ? tileset.ResolveFrame(stack[i], animTime) : stack[i];
						if ((uint)stacked >= (uint)rectCount)
							continue;

						DrawTile(batcher, texture, rects[stacked], world, xform, tileX, tileY, tw, th,
							color, rotation, scale, orientation, layerDepth);
					}
				}
			}
		}

		private static bool ChunkIntersectsCamera(in TileTransform xform, int baseX, int baseY, int tw, int th,
			RectangleF cameraBounds)
		{
			var a = xform.ToWorld(baseX * tw, baseY * th);
			var b = xform.ToWorld((baseX + ChunkSize) * tw, baseY * th);
			var c = xform.ToWorld(baseX * tw, (baseY + ChunkSize) * th);
			var d = xform.ToWorld((baseX + ChunkSize) * tw, (baseY + ChunkSize) * th);

			var minX = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
			var minY = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
			var maxX = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
			var maxY = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));

			return new RectangleF(minX, minY, maxX - minX, maxY - minY).Intersects(cameraBounds);
		}

		#endregion

		#region Chunk serialization

		// Chunks are run-length text: "value*count" runs joined by ','. A bare value is a run of one.
		private static string EncodeChunk(int[] chunk)
		{
			var sb = new StringBuilder();
			var runValue = chunk[0];
			var runLength = 1;

			void Flush()
			{
				if (sb.Length > 0)
					sb.Append(',');

				sb.Append(runValue);
				if (runLength > 1)
					sb.Append('*').Append(runLength);
			}

			for (var i = 1; i < chunk.Length; i++)
			{
				if (chunk[i] == runValue)
				{
					runLength++;
					continue;
				}

				Flush();
				runValue = chunk[i];
				runLength = 1;
			}

			Flush();
			return sb.ToString();
		}

		private static int[] DecodeChunk(string encoded)
		{
			var chunk = new int[ChunkSize * ChunkSize];
			if (string.IsNullOrEmpty(encoded))
				return chunk;

			var at = 0;
			foreach (var run in encoded.Split(','))
			{
				if (at >= chunk.Length)
					break;

				var star = run.IndexOf('*');
				int value, count;

				if (star < 0)
				{
					if (!int.TryParse(run, out value))
						continue;
					count = 1;
				}
				else
				{
					if (!int.TryParse(run.AsSpan(0, star), out value) ||
					    !int.TryParse(run.AsSpan(star + 1), out count))
						continue;
				}

				count = Math.Min(count, chunk.Length - at);
				for (var i = 0; i < count; i++)
					chunk[at++] = value;
			}

			return chunk;
		}

		private void SaveChunksTo(TilemapRendererComponentData data)
		{
			var coords = new List<int>();
			var payloads = new List<string>();

			foreach (var (key, chunk) in _chunks)
			{
				if (IsChunkEmpty(chunk))
					continue;

				coords.Add((int)(key >> 32));
				coords.Add((int)(uint)key);
				payloads.Add(EncodeChunk(chunk));
			}

			data.ChunkCoords = coords.ToArray();
			data.ChunkData = payloads.ToArray();

			SaveStacksTo(data);
			SaveOrientationTo(data);
			SaveCollisionTo(data);
		}

		private void SaveStacksTo(TilemapRendererComponentData data)
		{
			if (_stacks.Count == 0)
			{
				data.StackCoords = Array.Empty<int>();
				data.StackTiles = Array.Empty<string>();
				return;
			}

			var coords = new List<int>(_stacks.Count * 2);
			var payloads = new List<string>(_stacks.Count);

			foreach (var (key, stack) in _stacks)
			{
				if (stack.Count == 0)
					continue;

				coords.Add((int)(key >> 32));
				coords.Add((int)(uint)key);
				payloads.Add(string.Join(',', stack));
			}

			data.StackCoords = coords.ToArray();
			data.StackTiles = payloads.ToArray();
		}

		private void LoadStacksFrom(TilemapRendererComponentData data)
		{
			_stacks.Clear();

			var coords = data.StackCoords;
			var payloads = data.StackTiles;

			if (coords == null || payloads == null)
				return;

			var count = Math.Min(coords.Length / 2, payloads.Length);
			for (var i = 0; i < count; i++)
			{
				var parts = payloads[i].Split(',', StringSplitOptions.RemoveEmptyEntries);
				var stack = new List<int>(parts.Length);

				foreach (var part in parts)
				{
					if (int.TryParse(part, out var tile))
						stack.Add(tile);
				}

				if (stack.Count > 0)
					_stacks[CellKey(coords[i * 2], coords[i * 2 + 1])] = stack;
			}
		}

		private void LoadChunksFrom(TilemapRendererComponentData data)
		{
			_chunks.Clear();
			_stacks.Clear();

			var coords = data.ChunkCoords;
			var payloads = data.ChunkData;

			if (coords != null && payloads != null)
			{
				var count = Math.Min(coords.Length / 2, payloads.Length);
				for (var i = 0; i < count; i++)
					_chunks[Key(coords[i * 2], coords[i * 2 + 1])] = DecodeChunk(payloads[i]);
			}

			LoadStacksFrom(data);
			LoadOrientationFrom(data);
			LoadCollisionFrom(data);

			RecomputeExtents();
			_extentsDirty = false;
			_areBoundsDirty = true;
		}

		private static bool IsChunkEmpty(int[] chunk)
		{
			for (var i = 0; i < chunk.Length; i++)
			{
				if (chunk[i] != 0)
					return false;
			}

			return true;
		}

		#endregion
	}
}
