using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Voltage.Tilesets;

namespace Voltage
{
	public partial class TilemapRenderer
	{
		// Solid cells, keyed like tiles (CellKey). Collision is a per-cell mask independent of tiles — a cell can
		// be solid with no tile, or hold a tile without being solid — so painting collision and painting tiles
		// stay separate concerns.
		private readonly HashSet<long> _collisionCells = new();

		private Collider[] _colliders;

		// Merged collision rectangles, in tile coordinates. Cached: the merge is O(area) and feeds both the
		// generated colliders and the editor overlay, so it must not run per frame.
		private List<Rectangle> _collisionRectsCache;
		private bool _collisionRectsDirty = true;

		public int CollisionCellCount => _collisionCells.Count;

		public bool GetCollision(int tileX, int tileY) => _collisionCells.Contains(CellKey(tileX, tileY));

		public void SetCollision(int tileX, int tileY, bool solid)
		{
			var key = CellKey(tileX, tileY);
			var changed = solid ? _collisionCells.Add(key) : _collisionCells.Remove(key);

			if (changed)
				_collisionRectsDirty = true;
		}

		public void ClearCollision()
		{
			if (_collisionCells.Count == 0)
				return;

			_collisionCells.Clear();
			_collisionRectsDirty = true;
		}

		public IEnumerable<(int X, int Y)> EnumerateCollision()
		{
			foreach (var key in _collisionCells)
				yield return ((int)(key >> 32), (int)(uint)key);
		}

		/// <summary>Marks every cell holding a tile as solid. <paramref name="onlyTilesetSolidFlagged"/> restricts it
		/// to tiles whose tileset entry has the Solid flag set.</summary>
		public int GenerateCollisionFromTiles(bool onlyTilesetSolidFlagged)
		{
			var tileset = onlyTilesetSolidFlagged ? ResolvedTileset : null;
			var added = 0;

			foreach (var (x, y, tileIndex) in EnumerateTiles())
			{
				if (onlyTilesetSolidFlagged && tileset?.Asset?.IsSolid(tileIndex) != true)
					continue;

				if (_collisionCells.Add(CellKey(x, y)))
					added++;
			}

			if (added > 0)
				_collisionRectsDirty = true;

			return added;
		}

		#region Merge

		/// <summary>
		/// Greedy rectangle merge of the solid cells (the algorithm the Tiled importer uses), so a wall of
		/// thousands of solid cells becomes a handful of large rectangles rather than one collider per cell.
		/// Result is in tile coordinates; cached until the collision mask changes.
		/// </summary>
		/// <summary>Merged rectangles of ALL solid cells, for the editor overlay. Cached until the mask changes.</summary>
		public List<Rectangle> GetCollisionRects()
		{
			if (!_collisionRectsDirty && _collisionRectsCache != null)
				return _collisionRectsCache;

			_collisionRectsCache = BuildCollisionRects(_collisionCells);
			_collisionRectsDirty = false;
			return _collisionRectsCache;
		}

		private List<Rectangle> BuildCollisionRects(HashSet<long> cells)
		{
			var rects = new List<Rectangle>();
			if (cells.Count == 0)
				return rects;

			// Bound the scan to the solid cells' extents; the map itself is unbounded.
			int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
			foreach (var key in cells)
			{
				var x = (int)(key >> 32);
				var y = (int)(uint)key;
				if (x < minX) minX = x;
				if (x > maxX) maxX = x;
				if (y < minY) minY = y;
				if (y > maxY) maxY = y;
			}

			var width = maxX - minX + 1;
			var height = maxY - minY + 1;
			var checkedCells = new bool[width * height];

			bool Solid(int lx, int ly) => cells.Contains(CellKey(minX + lx, minY + ly));

			for (var y = 0; y < height; y++)
			{
				var startCol = -1;

				for (var x = 0; x < width; x++)
				{
					var idx = y * width + x;

					if (Solid(x, y) && !checkedCells[idx])
					{
						if (startCol < 0)
							startCol = x;

						checkedCells[idx] = true;
					}
					else if (startCol >= 0)
					{
						rects.Add(FindBoundsRect(cells, startCol, x, y, width, height, minX, minY, checkedCells));
						startCol = -1;
					}
				}

				if (startCol >= 0)
					rects.Add(FindBoundsRect(cells, startCol, width, y, width, height, minX, minY, checkedCells));
			}

			return rects;
		}

		// Extends the horizontal run [startX, endX) at row startY downward as far as every column stays solid,
		// then emits a tile-space rectangle. Cells of a failing row are un-checked so they can be re-scanned.
		private Rectangle FindBoundsRect(HashSet<long> cells, int startX, int endX, int startY, int width, int height,
			int originX, int originY, bool[] checkedCells)
		{
			for (var y = startY + 1; y < height; y++)
			{
				for (var x = startX; x < endX; x++)
				{
					var idx = y * width + x;

					if (!cells.Contains(CellKey(originX + x, originY + y)) || checkedCells[idx])
					{
						for (var rx = startX; rx < x; rx++)
							checkedCells[y * width + rx] = false;

						return new Rectangle(originX + startX, originY + startY, endX - startX, y - startY);
					}

					checkedCells[idx] = true;
				}
			}

			return new Rectangle(originX + startX, originY + startY, endX - startX, height - startY);
		}

		#endregion

		#region Colliders

		/// <summary>
		/// Rebuilds the physics colliders from the collision mask, registered directly with <see cref="Physics"/>.
		/// Plain solid cells greedy-merge into a few large <see cref="BoxCollider"/>s; slope tiles emit an
		/// individual triangle <see cref="PolygonCollider"/>; one-way tiles emit an individual collider carrying a
		/// <c>OneWayNormal</c>. Cells whose tile shape is None contribute nothing.
		/// </summary>
		public void RebuildColliders()
		{
			RemoveColliders();

			if (Entity == null || _collisionCells.Count == 0)
				return;

			var tw = TileWidth;
			var th = TileHeight;
			var tileset = ResolvedTileset?.Asset;

			// Partition: plain box cells merge; everything else (slopes, one-way) is emitted per cell.
			var boxCells = new HashSet<long>();
			var special = new List<(int X, int Y)>();

			foreach (var (x, y) in EnumerateCollision())
			{
				var shape = CellShape(tileset, x, y, out var oneWay);

				if (shape == TileCollisionShape.None)
					continue;

				if (shape == TileCollisionShape.Box && !oneWay)
					boxCells.Add(CellKey(x, y));
				else
					special.Add((x, y));
			}

			var result = new List<Collider>();

			foreach (var r in BuildCollisionRects(boxCells))
			{
				var box = new BoxCollider(r.X * tw + _localOffset.X, r.Y * th + _localOffset.Y,
					r.Width * tw, r.Height * th);
				ConfigureCollider(box, Vector2.Zero);
				result.Add(box);
			}

			foreach (var (x, y) in special)
			{
				var shape = CellShape(tileset, x, y, out var oneWay);
				var oneWayNormal = oneWay ? new Vector2(0f, -1f) : Vector2.Zero;
				var originPx = new Vector2(x * tw + _localOffset.X, y * th + _localOffset.Y);

				Collider collider = shape == TileCollisionShape.Box
					? new BoxCollider(originPx.X, originPx.Y, tw, th)
					: new PolygonCollider(SlopePoints(shape, originPx, tw, th));

				ConfigureCollider(collider, oneWayNormal);
				result.Add(collider);
			}

			_colliders = result.ToArray();
			foreach (var c in _colliders)
				Physics.AddCollider(c);
		}

		private void ConfigureCollider(Collider collider, Vector2 oneWayNormal)
		{
			collider.Layer = PhysicsLayer;
			collider.CollidesWithLayers = CollidesWithLayers;
			collider.IsTrigger = IsTrigger;
			collider.OneWayNormal = oneWayNormal;
			collider.Entity = Entity;
		}

		private TileCollisionShape CellShape(TilesetAsset tileset, int x, int y, out bool oneWay)
		{
			oneWay = false;

			// The base tile defines the cell's collision. A collision cell with no tile is a plain box.
			var tile = GetBaseTile(x, y);
			if (tileset == null || tile < 0)
				return TileCollisionShape.Box;

			var info = tileset.GetTileInfo(tile);
			if (info == null)
				return TileCollisionShape.Box;

			oneWay = info.OneWay;
			return info.CollisionShape;
		}

		// Triangle vertices (clockwise, tile-pixel space) for a slope shape. See TileCollisionShape.
		private static Vector2[] SlopePoints(TileCollisionShape shape, Vector2 o, int tw, int th)
		{
			return shape switch
			{
				TileCollisionShape.SlopeUpRight => new[]
					{ new Vector2(o.X, o.Y + th), new Vector2(o.X + tw, o.Y), new Vector2(o.X + tw, o.Y + th) },
				TileCollisionShape.SlopeUpLeft => new[]
					{ new Vector2(o.X, o.Y), new Vector2(o.X + tw, o.Y + th), new Vector2(o.X, o.Y + th) },
				TileCollisionShape.SlopeDownRight => new[]
					{ new Vector2(o.X, o.Y), new Vector2(o.X + tw, o.Y), new Vector2(o.X, o.Y + th) },
				TileCollisionShape.SlopeDownLeft => new[]
					{ new Vector2(o.X, o.Y), new Vector2(o.X + tw, o.Y), new Vector2(o.X + tw, o.Y + th) },
				_ => new[]
					{ o, new Vector2(o.X + tw, o.Y), new Vector2(o.X + tw, o.Y + th), new Vector2(o.X, o.Y + th) },
			};
		}

		/// <summary>
		/// Draws the generated colliders. They are not entity components (they are registered directly with
		/// Physics), so the entity's component debug sweep never reaches them — this override is what makes them
		/// visible whenever debug rendering is on, exactly like <c>TiledMapRenderer.DebugRender</c>.
		/// </summary>
		public override void DebugRender(Batcher batcher)
		{
			base.DebugRender(batcher);

			if (_colliders == null)
				return;

			foreach (var collider in _colliders)
				collider?.DebugRender(batcher);
		}

		public void RemoveColliders()
		{
			if (_colliders == null)
				return;

			foreach (var collider in _colliders)
			{
				if (collider != null)
					Physics.RemoveCollider(collider);
			}

			_colliders = null;
		}

		#endregion

		#region Serialization

		private void SaveCollisionTo(TilemapRendererComponentData data)
		{
			if (_collisionCells.Count == 0)
			{
				data.CollisionCoords = Array.Empty<int>();
				data.CollisionData = Array.Empty<string>();
				return;
			}

			// Group solid cells into chunks and RLE each, matching the tile-chunk format — compact for the large
			// contiguous regions collision masks usually are.
			var chunks = new Dictionary<long, int[]>();

			foreach (var (x, y) in EnumerateCollision())
			{
				var key = Key(ChunkOf(x), ChunkOf(y));
				if (!chunks.TryGetValue(key, out var chunk))
				{
					chunk = new int[ChunkSize * ChunkSize];
					chunks[key] = chunk;
				}

				chunk[LocalOf(y) * ChunkSize + LocalOf(x)] = 1;
			}

			var coords = new List<int>(chunks.Count * 2);
			var payloads = new List<string>(chunks.Count);

			foreach (var (key, chunk) in chunks)
			{
				coords.Add((int)(key >> 32));
				coords.Add((int)(uint)key);
				payloads.Add(EncodeChunk(chunk));
			}

			data.CollisionCoords = coords.ToArray();
			data.CollisionData = payloads.ToArray();
		}

		private void LoadCollisionFrom(TilemapRendererComponentData data)
		{
			_collisionCells.Clear();
			_collisionRectsDirty = true;

			var coords = data.CollisionCoords;
			var payloads = data.CollisionData;

			if (coords == null || payloads == null)
				return;

			var count = Math.Min(coords.Length / 2, payloads.Length);
			for (var i = 0; i < count; i++)
			{
				var chunk = DecodeChunk(payloads[i]);
				var baseX = coords[i * 2] * ChunkSize;
				var baseY = coords[i * 2 + 1] * ChunkSize;

				for (var c = 0; c < chunk.Length; c++)
				{
					if (chunk[c] != 0)
						_collisionCells.Add(CellKey(baseX + c % ChunkSize, baseY + c / ChunkSize));
				}
			}
		}

		#endregion
	}
}
