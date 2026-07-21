using System;
using System.Collections.Generic;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Voltage.Editor.Hotkeys;
using Voltage.Editor.Undo.ComponentActions;
using Voltage.Editor.Undo.Core;
using EngineAssetReference = Voltage.Serialization.AssetReference;

namespace Voltage.Editor.Tools.Tilemap
{
	public enum TileTool
	{
		Brush,
		Eraser,
		Rectangle,
		Fill,
		Picker,
		Select,
	}

	/// <summary>Whether the brush paints tiles or the collision mask.</summary>
	public enum TileEditMode
	{
		Tiles,
		Collision,
	}

	/// <summary>Turns mouse input in the game view into tilemap edits, and draws the grid/cursor overlays.</summary>
	public partial class TilePaintTool
	{
		public TilemapRenderer Target;

		/// <summary>Tileset staged in the palette; the first stroke creates a layer bound to it if there is no Target.</summary>
		public EngineAssetReference StagedTileset;

		public int FallbackTileWidth = 16;
		public int FallbackTileHeight = 16;

		public TileTool Tool = TileTool.Brush;

		public TileEditMode EditMode = TileEditMode.Tiles;

		/// <summary>Orientation applied to freshly painted tiles: 2-bit rotation + flipX/flipY. See TilemapRenderer.</summary>
		public byte CurrentOrientation;

		public Color CollisionColor = new(1f, 0.3f, 0.3f, 0.35f);

		/// <summary>On: a drag lets stamps overlap. Off: a drag steps by the stamp size. A single click is always free-form.</summary>
		public bool StackWhileDragging;

		public bool ShowGrid = true;
		public bool AlwaysShowGrid;

		public Color GridColor = new(0x24, 0x24, 0x24, 0x21);
		public Color HighlightColor = new(0.2f, 0.7f, 1f, 1f);

		// Grid/cursor thickness are in SCREEN pixels; divided by camera zoom before drawing (Debug lines are world-space).
		public float GridThickness = 1f;
		public float HighlightThickness = 2f;

		/// <summary>A stamp cell covered by no selection box: leave whatever is already there untouched.</summary>
		public const int NoTile = int.MinValue;

		/// <summary>Block of tile indices picked in the palette (-1 = erase, NoTile = hole), anchored at the hovered cell.</summary>
		public int[] Selection = Array.Empty<int>();
		public int SelectionWidth;
		public int SelectionHeight;

		public bool HasSelection => Selection.Length > 0 && SelectionWidth > 0 && SelectionHeight > 0;

		public Point HoveredCell { get; private set; }
		public bool IsHovering { get; private set; }

		private const int MaxFillCells = 20000;

		private static readonly Point[] _neighbourOffsets =
		{
			new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
		};

		private readonly List<TilePaintUndoAction.CellChange> _strokeChanges = new();
		private bool _isStroking;
		private bool _strokeErases;

		private bool _isDraggingRect;
		private Point _rectAnchor;

		private Point _strokeAnchor;

		private readonly List<TileCollisionUndoAction.CellChange> _collisionChanges = new();
		private bool _isCollisionStroking;
		private bool _collisionErases;

		// The block exactly as picked in the palette. Selection is always derived from it, so orientation
		// changes never accumulate rounding or mirror the wrong axis.
		private int[] _baseSelection = Array.Empty<int>();
		private int _baseWidth;
		private int _baseHeight;

		public void SetSelection(int[] tiles, int width, int height)
		{
			_baseSelection = tiles ?? Array.Empty<int>();
			_baseWidth = width;
			_baseHeight = height;

			ApplyOrientationToStamp();
		}

		public void SetSingleSelection(int tileIndex) => SetSelection(new[] { tileIndex }, 1, 1);

		public int OrientationRotation => CurrentOrientation & TilemapRenderer.OrientRotationMask;
		public bool OrientationFlipX => (CurrentOrientation & TilemapRenderer.OrientFlipX) != 0;
		public bool OrientationFlipY => (CurrentOrientation & TilemapRenderer.OrientFlipY) != 0;

		/// <summary>
		/// Turns the brush: each tile spins in place AND the stamp's layout turns with it, so a 4x2 block becomes
		/// 2x4 the way rotating the whole block would. Mirrors what RotateSelection does to placed tiles.
		/// </summary>
		public void RotateBrush(int quarterTurns)
		{
			CurrentOrientation = TilemapRenderer.MakeOrientation(
				OrientationRotation + quarterTurns, OrientationFlipX, OrientationFlipY);

			ApplyOrientationToStamp();
		}

		/// <summary>Mirrors the brush: each tile flips AND the block's cell order flips with it.</summary>
		public void ToggleFlipX()
		{
			CurrentOrientation = TilemapRenderer.MakeOrientation(
				OrientationRotation, !OrientationFlipX, OrientationFlipY);

			ApplyOrientationToStamp();
		}

		public void ToggleFlipY()
		{
			CurrentOrientation = TilemapRenderer.MakeOrientation(
				OrientationRotation, OrientationFlipX, !OrientationFlipY);

			ApplyOrientationToStamp();
		}

		public void ResetOrientation()
		{
			CurrentOrientation = 0;
			ApplyOrientationToStamp();
		}

		/// <summary>
		/// Rebuilds the stamp from the picked block: mirror first, then rotate - the same order the renderer
		/// applies to a single tile, so the block and the artwork on it always agree. Holes stay holes.
		/// </summary>
		private void ApplyOrientationToStamp()
		{
			var width = _baseWidth;
			var height = _baseHeight;

			if (width <= 0 || height <= 0 || _baseSelection.Length == 0)
			{
				Selection = _baseSelection;
				SelectionWidth = width;
				SelectionHeight = height;
				return;
			}

			var source = _baseSelection;

			if (OrientationFlipX || OrientationFlipY)
			{
				var mirrored = new int[source.Length];

				for (var y = 0; y < height; y++)
				{
					for (var x = 0; x < width; x++)
					{
						var sx = OrientationFlipX ? width - 1 - x : x;
						var sy = OrientationFlipY ? height - 1 - y : y;
						mirrored[y * width + x] = source[sy * width + sx];
					}
				}

				source = mirrored;
			}

			var turns = OrientationRotation & 3;
			if (turns == 0)
			{
				Selection = source;
				SelectionWidth = width;
				SelectionHeight = height;
				return;
			}

			var rotatedWidth = turns % 2 == 1 ? height : width;
			var rotatedHeight = turns % 2 == 1 ? width : height;

			var rotated = new int[rotatedWidth * rotatedHeight];
			for (var i = 0; i < rotated.Length; i++)
				rotated[i] = NoTile;

			for (var y = 0; y < height; y++)
			{
				for (var x = 0; x < width; x++)
				{
					var moved = RotateLocal(new Point(x, y), width, height, turns);
					rotated[moved.Y * rotatedWidth + moved.X] = source[y * width + x];
				}
			}

			Selection = rotated;
			SelectionWidth = rotatedWidth;
			SelectionHeight = rotatedHeight;
		}

		// Bindings live in the Tile Palette category. Only fires while that window is focused.
		public void UpdateOrientationHotkeys()
		{
			if (EditMode != TileEditMode.Tiles)
				return;

			// With a live tile selection the rotate keys turn the placed block instead of the brush.
			var rotatesSelection = Tool == TileTool.Select && HasTileSelection;

			if (EditorHotkeys.Pressed(EditorHotkeys.TileRotateLeft))
			{
				if (rotatesSelection) RotateSelection(-1);
				else RotateBrush(-1);
			}

			if (EditorHotkeys.Pressed(EditorHotkeys.TileRotateRight))
			{
				if (rotatesSelection) RotateSelection(1);
				else RotateBrush(1);
			}
			if (EditorHotkeys.Pressed(EditorHotkeys.TileFlipX)) ToggleFlipX();
			if (EditorHotkeys.Pressed(EditorHotkeys.TileFlipY)) ToggleFlipY();
		}

		/// <summary>
		/// Drops the target if its entity no longer belongs to a live scene. The tool lives on the ImGuiManager and
		/// outlives a scene, so a layer from the scene we just left must not linger.
		/// </summary>
		public void DropDeadTarget()
		{
			if (Target == null)
				return;

			if (Target.Entity != null && Target.Entity.Scene != null)
				return;

			Target = null;
			AbandonStroke();
		}

		/// <summary>Clears scene-scoped state on scene load. The staged tileset and selection are asset-scoped and survive.</summary>
		public void OnSceneChanged()
		{
			Target = null;
			AbandonStroke();
		}

		private void AbandonStroke()
		{
			AbandonCollisionStroke();
			_strokeChanges.Clear();
			_isStroking = false;
			_isDraggingRect = false;
		}

		public void ValidateTarget(Entity selectedEntity)
		{
			DropDeadTarget();

			if (Target == null && selectedEntity != null)
				Target = selectedEntity.GetComponent<TilemapRenderer>();

			if (Target == null)
			{
				var maps = TilemapSceneUtils.FindTilemaps();
				if (maps.Count > 0)
					Target = maps[0];
			}
		}

		private bool EnsurePaintTarget()
		{
			if (Target?.Entity != null)
				return true;

			if (!StagedTileset.IsValid)
				return false;

			Target = TilemapSceneUtils.CreateTilemapLayer(StagedTileset, Vector2.Zero);
			return Target != null;
		}

		public void Update(Vector2 worldMouse, Camera camera)
		{
			IsHovering = false;

			var cell = WorldToCell(worldMouse);
			HoveredCell = cell;
			IsHovering = true;

			var eraseModifier = Input.RightMouseButtonDown || ImGui.GetIO().KeyShift;

			// The collision overlay (existing solid cells) is always drawn in collision mode so you can see what
			// you are editing.
			if (EditMode == TileEditMode.Collision)
			{
				DrawCollisionOverlay(camera);

				// Select moves tiles and collision together, so it works in either edit mode.
				if (Tool == TileTool.Select)
					UpdateSelect(cell);
				else
					UpdateCollision(cell, eraseModifier);

				return;
			}

			if (IsAutotiling && Tool != TileTool.Select)
			{
				UpdateAutotile(cell, Tool == TileTool.Eraser || eraseModifier);
				return;
			}

			switch (Tool)
			{
				case TileTool.Select:
					UpdateSelect(cell);
					break;

				case TileTool.Picker:
					DrawCellOutline(cell.X, cell.Y, 1, 1, HighlightColor);
					if (Input.LeftMouseButtonPressed)
						PickTile(cell);
					break;

				case TileTool.Fill:
					DrawCellOutline(cell.X, cell.Y, 1, 1, HighlightColor);
					if (Input.LeftMouseButtonPressed)
						FloodFill(cell);
					break;

				case TileTool.Rectangle:
					UpdateRectangle(cell, eraseModifier);
					break;

				case TileTool.Eraser:
					UpdateFreehand(cell, forceErase: true);
					break;

				default:
					UpdateFreehand(cell, forceErase: eraseModifier);
					break;
			}
		}

		/// <summary>
		/// Commits an in-flight stroke when the cursor leaves the game view: Update stops running out there, so a drag
		/// released outside would never reach the undo stack.
		/// </summary>
		public void CommitPendingStroke()
		{
			_isDraggingRect = false;

			if (_isStroking)
				CommitStroke();

			CommitPendingCollisionStroke();
		}

		/// <summary>Cancels an in-progress stroke without committing it, rolling its cells back.</summary>
		public void Reset()
		{
			ClearTileSelection();
			AbandonCollisionStroke();

			if (_isStroking)
			{
				for (var i = _strokeChanges.Count - 1; i >= 0; i--)
				{
					var change = _strokeChanges[i];
					Target?.SetCellStack(change.X, change.Y, change.OldStack);
				}
			}

			_strokeChanges.Clear();
			_isStroking = false;
			_isDraggingRect = false;
		}

		#region Tools

		private void UpdateFreehand(Point cell, bool forceErase)
		{
			var footprintW = forceErase ? Math.Max(1, SelectionWidth) : SelectionWidth;
			var footprintH = forceErase ? Math.Max(1, SelectionHeight) : SelectionHeight;

			if (!forceErase && !HasSelection)
			{
				DrawCellOutline(cell.X, cell.Y, 1, 1, HighlightColor);
				return;
			}

			// A drag snaps to a stamp-sized lattice so blocks tile edge-to-edge instead of smearing a copy at every
			// cell crossed; a fresh click is free-form. StackWhileDragging opts out and lets stamps overlap.
			var paintCell = _isStroking && !StackWhileDragging ? SnapToStrokeLattice(cell) : cell;

			DrawCellOutline(paintCell.X, paintCell.Y, footprintW, footprintH,
				forceErase ? new Color(1f, 0.35f, 0.35f, 0.9f) : HighlightColor);

			var pressed = Input.LeftMouseButtonPressed || (forceErase && Input.RightMouseButtonPressed);
			var down = Input.LeftMouseButtonDown || (forceErase && Input.RightMouseButtonDown);
			var released = Input.LeftMouseButtonReleased || Input.RightMouseButtonReleased;

			if (pressed)
			{
				if (Target?.Entity == null && (forceErase || !EnsurePaintTarget()))
					return;

				BeginStroke(forceErase);
				_strokeAnchor = cell;
				StampAt(cell, forceErase);
			}
			else if (down && _isStroking)
			{
				StampAt(paintCell, forceErase);
			}

			if (released && _isStroking)
				CommitStroke();
		}

		private Point SnapToStrokeLattice(Point cell)
		{
			var strideX = Math.Max(1, SelectionWidth);
			var strideY = Math.Max(1, SelectionHeight);

			var stepsX = (int)Math.Floor((cell.X - _strokeAnchor.X) / (float)strideX);
			var stepsY = (int)Math.Floor((cell.Y - _strokeAnchor.Y) / (float)strideY);

			return new Point(
				_strokeAnchor.X + stepsX * strideX,
				_strokeAnchor.Y + stepsY * strideY);
		}

		private void UpdateRectangle(Point cell, bool erase)
		{
			if (!_isDraggingRect)
			{
				DrawCellOutline(cell.X, cell.Y, 1, 1, HighlightColor);

				if (Input.LeftMouseButtonPressed || Input.RightMouseButtonPressed)
				{
					_isDraggingRect = true;
					_rectAnchor = cell;
					_strokeErases = erase || Input.RightMouseButtonPressed;
				}

				return;
			}

			var minX = Math.Min(_rectAnchor.X, cell.X);
			var minY = Math.Min(_rectAnchor.Y, cell.Y);
			var width = Math.Abs(cell.X - _rectAnchor.X) + 1;
			var height = Math.Abs(cell.Y - _rectAnchor.Y) + 1;

			DrawCellOutline(minX, minY, width, height,
				_strokeErases ? new Color(1f, 0.35f, 0.35f, 0.9f) : HighlightColor);

			if (!Input.LeftMouseButtonReleased && !Input.RightMouseButtonReleased)
				return;

			_isDraggingRect = false;

			if (!_strokeErases && !HasSelection)
				return;

			if (Target?.Entity == null && (_strokeErases || !EnsurePaintTarget()))
				return;

			BeginStroke(_strokeErases);

			for (var y = 0; y < height; y++)
			{
				for (var x = 0; x < width; x++)
				{
					// Tile the selection across the rect rather than stretching it.
					var tile = _strokeErases
						? -1
						: Selection[y % SelectionHeight * SelectionWidth + x % SelectionWidth];

					if (tile == NoTile)
						continue;

					ApplyCell(minX + x, minY + y, tile);
				}
			}

			CommitStroke();
		}

		private void PickTile(Point cell)
		{
			if (Target?.Entity == null)
				return;

			var tile = Target.GetTile(cell.X, cell.Y);
			if (tile < 0)
				return;

			SetSingleSelection(tile);
			Tool = TileTool.Brush;
		}

		/// <summary>
		/// Flood fills the contiguous run matching the clicked cell. Bounded to the painted extents plus a margin, and
		/// capped at <see cref="MaxFillCells"/>, since a sparse map has no natural edge to stop at.
		/// </summary>
		private void FloodFill(Point origin)
		{
			if (!HasSelection)
				return;

			if (Target?.Entity == null && !EnsurePaintTarget())
				return;

			// Fill takes the first real tile of the stamp; holes are not a fill colour.
			var replacement = NoTile;
			foreach (var tile in Selection)
			{
				if (tile == NoTile)
					continue;

				replacement = tile;
				break;
			}

			if (replacement == NoTile)
				return;
			var targetTile = Target.GetTile(origin.X, origin.Y);
			if (targetTile == replacement)
				return;

			var extents = Target.TileExtents;
			var bounds = extents.IsEmpty
				? new Rectangle(origin.X - 32, origin.Y - 32, 64, 64)
				: new Rectangle(extents.X - 32, extents.Y - 32, extents.Width + 64, extents.Height + 64);

			if (!bounds.Contains(origin))
				return;

			BeginStroke(false);

			var visited = new HashSet<long>();
			var queue = new Queue<Point>();
			queue.Enqueue(origin);
			visited.Add((long)origin.X << 32 ^ (uint)origin.Y);

			var filled = 0;
			while (queue.Count > 0 && filled < MaxFillCells)
			{
				var cell = queue.Dequeue();

				if (Target.GetTile(cell.X, cell.Y) != targetTile)
					continue;

				ApplyCell(cell.X, cell.Y, replacement);
				filled++;

				foreach (var offset in _neighbourOffsets)
				{
					var next = new Point(cell.X + offset.X, cell.Y + offset.Y);
					if (!bounds.Contains(next))
						continue;

					var key = (long)next.X << 32 ^ (uint)next.Y;
					if (visited.Add(key))
						queue.Enqueue(next);
				}
			}

			if (filled >= MaxFillCells)
				Debug.Warn($"Tile fill stopped at the {MaxFillCells}-cell safety limit.");

			CommitStroke();
		}

		#endregion

		#region Stroke bookkeeping

		private void BeginStroke(bool erases)
		{
			_isStroking = true;
			_strokeErases = erases;
			_strokeChanges.Clear();
		}

		private void StampAt(Point cell, bool erase)
		{
			if (erase)
			{
				var w = Math.Max(1, SelectionWidth);
				var h = Math.Max(1, SelectionHeight);

				for (var y = 0; y < h; y++)
				for (var x = 0; x < w; x++)
					ApplyCell(cell.X + x, cell.Y + y, -1);

				return;
			}

			for (var y = 0; y < SelectionHeight; y++)
			{
				for (var x = 0; x < SelectionWidth; x++)
				{
					var tile = Selection[y * SelectionWidth + x];
					if (tile == NoTile)
						continue;

					ApplyCell(cell.X + x, cell.Y + y, tile);
				}
			}
		}

		/// <summary>
		/// Writes one cell. Fully transparent tile = a hole in the stamp, skip it. A partially transparent tile
		/// stacks only while "Stack while dragging" is on; otherwise it replaces. Only the eraser clears a cell.
		/// </summary>
		private void ApplyCell(int x, int y, int newTile)
		{
			var tileset = Target.ResolvedTileset;

			if (newTile >= 0 && tileset?.IsBlank(newTile) == true)
				return;

			var oldStack = Target.GetCellStack(x, y);

			if (newTile < 0)
			{
				if (oldStack.Length == 0)
					return;

				Target.SetTile(x, y, -1);
			}
			// Stacking is opt-in: with "Stack while dragging" off a stamp always REPLACES the cell, even when the
			// tile has transparency. Otherwise painting over existing tiles silently piled them up.
			else if (oldStack.Length == 0 || tileset == null || tileset.IsOpaque(newTile) || !StackWhileDragging)
			{
				// A fresh paint carries the brush's current orientation. Rotation is applied but deliberately not
				// tracked by undo, so re-painting the same tile with the same orientation is a no-op.
				if (oldStack.Length == 1 && oldStack[0] == newTile && Target.GetOrientation(x, y) == CurrentOrientation)
					return;

				Target.SetTile(x, y, newTile);
				Target.SetOrientation(x, y, CurrentOrientation);
			}
			else
			{
				Target.PushTile(x, y, newTile);
			}

			var newStack = Target.GetCellStack(x, y);
			if (StacksEqual(oldStack, newStack))
				return;

			_strokeChanges.Add(new TilePaintUndoAction.CellChange(x, y, oldStack, newStack));
		}

		private static bool StacksEqual(int[] a, int[] b)
		{
			if (a.Length != b.Length)
				return false;

			for (var i = 0; i < a.Length; i++)
			{
				if (a[i] != b[i])
					return false;
			}

			return true;
		}

		private void CommitStroke()
		{
			_isStroking = false;

			if (_strokeChanges.Count == 0)
				return;

			var verb = _strokeErases ? "Erase" : "Paint";
			var description = $"{verb} {_strokeChanges.Count} tile{(_strokeChanges.Count == 1 ? "" : "s")}";

			EditorChangeTracker.PushUndo(
				new TilePaintUndoAction(Target, _strokeChanges, description),
				Target.Entity,
				description);

			_strokeChanges.Clear();
		}

		#endregion

		#region Overlays

		/// <summary>Draws the tile grid across the visible camera area. Works before a layer exists, using the staged tile size.</summary>
		public void DrawGrid(Camera camera)
		{
			var tw = GridTileWidth;
			var th = GridTileHeight;
			if (tw <= 0 || th <= 0)
				return;

			// Below a few screen pixels per cell the grid is a grey wash, not a guide.
			var zoom = camera.RawZoom;
			if (tw * zoom < 4f || th * zoom < 4f)
				return;

			var view = camera.Bounds;
			var min = WorldToCell(new Vector2(view.X, view.Y));
			var max = WorldToCell(new Vector2(view.Right, view.Bottom));

			// Under rotation the view corners do not map to an axis-aligned tile range; pad generously.
			if (Rotation != 0f)
			{
				var pad = Math.Max(max.X - min.X, max.Y - min.Y) + 1;
				min = new Point(min.X - pad, min.Y - pad);
				max = new Point(max.X + pad, max.Y + pad);
			}

			if (max.X < min.X || max.Y < min.Y)
				return;

			var thickness = WorldThickness(GridThickness, zoom);

			for (var x = min.X; x <= max.X + 1; x++)
				Debug.DrawLine(CellToWorld(x, min.Y), CellToWorld(x, max.Y + 1), GridColor, thickness, 0f);

			for (var y = min.Y; y <= max.Y + 1; y++)
				Debug.DrawLine(CellToWorld(min.X, y), CellToWorld(max.X + 1, y), GridColor, thickness, 0f);
		}

		/// <summary>
		/// Draws a plain world-origin-aligned grid at an arbitrary pixel spacing. Used for the placement
		/// (snap-to-grid) overlay, independent of any tileset's tile size.
		/// </summary>
		public void DrawPlacementGrid(Camera camera, int spacing)
		{
			if (spacing <= 0 || camera == null)
				return;

			var zoom = camera.RawZoom;
			if (spacing * zoom < 4f)
				return;

			var view = camera.Bounds;
			var minX = (int)Math.Floor(view.X / spacing);
			var maxX = (int)Math.Ceiling(view.Right / spacing);
			var minY = (int)Math.Floor(view.Y / spacing);
			var maxY = (int)Math.Ceiling(view.Bottom / spacing);

			if (maxX < minX || maxY < minY)
				return;

			var thickness = WorldThickness(GridThickness, zoom);
			float top = minY * spacing, bottom = maxY * spacing;
			float left = minX * spacing, right = maxX * spacing;

			for (var x = minX; x <= maxX; x++)
				Debug.DrawLine(new Vector2(x * spacing, top), new Vector2(x * spacing, bottom), GridColor, thickness, 0f);

			for (var y = minY; y <= maxY; y++)
				Debug.DrawLine(new Vector2(left, y * spacing), new Vector2(right, y * spacing), GridColor, thickness, 0f);
		}

		/// <summary>
		/// Outlines a block of cells in world space. The outline is inset inside the cell because Debug.Render iterates
		/// its queue backwards: the grid, queued first, is drawn OVER the outline and erases any edge on a grid line.
		/// </summary>
		private void DrawCellOutline(int tileX, int tileY, int width, int height, Color color)
		{
			width = Math.Max(1, width);
			height = Math.Max(1, height);

			var camera = Core.Scene?.Camera;
			var zoom = camera?.RawZoom ?? 1f;

			var thickness = WorldThickness(HighlightThickness, zoom);

			var insetX = thickness * 0.5f / Math.Max(1, GridTileWidth);
			var insetY = thickness * 0.5f / Math.Max(1, GridTileHeight);

			var topLeft = CellToWorldF(tileX + insetX, tileY + insetY);
			var topRight = CellToWorldF(tileX + width - insetX, tileY + insetY);
			var bottomRight = CellToWorldF(tileX + width - insetX, tileY + height - insetY);
			var bottomLeft = CellToWorldF(tileX + insetX, tileY + height - insetY);

			Debug.DrawLine(topLeft, topRight, color, thickness, 0f);
			Debug.DrawLine(topRight, bottomRight, color, thickness, 0f);
			Debug.DrawLine(bottomRight, bottomLeft, color, thickness, 0f);
			Debug.DrawLine(bottomLeft, topLeft, color, thickness, 0f);
		}

		/// <summary>Screen-pixel width converted to world units, so lines keep a constant on-screen thickness.</summary>
		private static float WorldThickness(float screenPixels, float zoom) =>
			Math.Max(0.01f, screenPixels / Math.Max(0.0001f, zoom));

		#endregion

		#region Cell <-> world

		private int GridTileWidth => Target?.Entity != null ? Target.TileWidth : FallbackTileWidth;
		private int GridTileHeight => Target?.Entity != null ? Target.TileHeight : FallbackTileHeight;
		private float Rotation => Target?.Entity?.Transform.Rotation ?? 0f;

		private Vector2 CellToWorld(int tileX, int tileY) => CellToWorldF(tileX, tileY);

		private Vector2 CellToWorldF(float tileX, float tileY)
		{
			var local = new Vector2(tileX * GridTileWidth, tileY * GridTileHeight);

			if (Target?.Entity == null)
				return local;

			return Target.LocalTileSpaceToWorld(local);
		}

		private Point WorldToCell(Vector2 world)
		{
			if (Target?.Entity != null)
				return Target.WorldToTile(world);

			return new Point(
				(int)Math.Floor(world.X / Math.Max(1, GridTileWidth)),
				(int)Math.Floor(world.Y / Math.Max(1, GridTileHeight)));
		}

		#endregion
	}
}
