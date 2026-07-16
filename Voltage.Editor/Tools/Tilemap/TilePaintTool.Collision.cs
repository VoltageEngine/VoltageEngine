using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Voltage.Editor.Undo.ComponentActions;
using Voltage.Editor.Undo.Core;

namespace Voltage.Editor.Tools.Tilemap
{
	public partial class TilePaintTool
	{
		// Collision editing reuses the tile brush/rect/fill tools, but writes the per-cell collision mask
		// instead of tiles. Right mouse / Shift erases, as in tile mode.
		private void UpdateCollision(Point cell, bool eraseModifier)
		{
			var erase = Tool == TileTool.Eraser || eraseModifier;

			switch (Tool)
			{
				case TileTool.Rectangle:
					UpdateCollisionRectangle(cell, erase);
					return;

				case TileTool.Fill:
					DrawCellOutline(cell.X, cell.Y, 1, 1, HighlightColor);
					if (Input.LeftMouseButtonPressed)
						CollisionFloodFill(cell);
					return;

				default:
					UpdateCollisionFreehand(cell, erase);
					return;
			}
		}

		private void UpdateCollisionFreehand(Point cell, bool erase)
		{
			DrawCellOutline(cell.X, cell.Y, 1, 1, erase ? new Color(1f, 0.35f, 0.35f, 0.9f) : HighlightColor);

			var pressed = Input.LeftMouseButtonPressed || (erase && Input.RightMouseButtonPressed);
			var down = Input.LeftMouseButtonDown || (erase && Input.RightMouseButtonDown);
			var released = Input.LeftMouseButtonReleased || Input.RightMouseButtonReleased;

			if (pressed)
			{
				if (Target?.Entity == null && (erase || !EnsurePaintTarget()))
					return;

				BeginCollisionStroke(erase);
				ApplyCollisionCell(cell.X, cell.Y, !erase);
			}
			else if (down && _isCollisionStroking)
			{
				ApplyCollisionCell(cell.X, cell.Y, !erase);
			}

			if (released && _isCollisionStroking)
				CommitCollisionStroke();
		}

		private void UpdateCollisionRectangle(Point cell, bool erase)
		{
			if (!_isDraggingRect)
			{
				DrawCellOutline(cell.X, cell.Y, 1, 1, HighlightColor);

				if (Input.LeftMouseButtonPressed || Input.RightMouseButtonPressed)
				{
					_isDraggingRect = true;
					_rectAnchor = cell;
					_collisionErases = erase || Input.RightMouseButtonPressed;
				}

				return;
			}

			var minX = System.Math.Min(_rectAnchor.X, cell.X);
			var minY = System.Math.Min(_rectAnchor.Y, cell.Y);
			var width = System.Math.Abs(cell.X - _rectAnchor.X) + 1;
			var height = System.Math.Abs(cell.Y - _rectAnchor.Y) + 1;

			DrawCellOutline(minX, minY, width, height,
				_collisionErases ? new Color(1f, 0.35f, 0.35f, 0.9f) : HighlightColor);

			if (!Input.LeftMouseButtonReleased && !Input.RightMouseButtonReleased)
				return;

			_isDraggingRect = false;

			if (Target?.Entity == null && (_collisionErases || !EnsurePaintTarget()))
				return;

			BeginCollisionStroke(_collisionErases);

			for (var y = 0; y < height; y++)
			for (var x = 0; x < width; x++)
				ApplyCollisionCell(minX + x, minY + y, !_collisionErases);

			CommitCollisionStroke();
		}

		private void CollisionFloodFill(Point origin)
		{
			if (Target?.Entity == null && !EnsurePaintTarget())
				return;

			var targetSolid = Target.GetCollision(origin.X, origin.Y);
			var replacement = !targetSolid;

			var extents = Target.TileExtents;
			var bounds = extents.IsEmpty
				? new Rectangle(origin.X - 32, origin.Y - 32, 64, 64)
				: new Rectangle(extents.X - 32, extents.Y - 32, extents.Width + 64, extents.Height + 64);

			if (!bounds.Contains(origin))
				return;

			BeginCollisionStroke(targetSolid);

			var visited = new HashSet<long>();
			var queue = new Queue<Point>();
			queue.Enqueue(origin);
			visited.Add((long)origin.X << 32 ^ (uint)origin.Y);

			var filled = 0;
			while (queue.Count > 0 && filled < CollisionFillLimit)
			{
				var c = queue.Dequeue();
				if (Target.GetCollision(c.X, c.Y) != targetSolid)
					continue;

				ApplyCollisionCell(c.X, c.Y, replacement);
				filled++;

				foreach (var off in _neighbourOffsets)
				{
					var next = new Point(c.X + off.X, c.Y + off.Y);
					if (!bounds.Contains(next))
						continue;

					var key = (long)next.X << 32 ^ (uint)next.Y;
					if (visited.Add(key))
						queue.Enqueue(next);
				}
			}

			CommitCollisionStroke();
		}

		private const int CollisionFillLimit = 20000;

		private void BeginCollisionStroke(bool erases)
		{
			_isCollisionStroking = true;
			_collisionErases = erases;
			_collisionChanges.Clear();
		}

		private void ApplyCollisionCell(int x, int y, bool solid)
		{
			var old = Target.GetCollision(x, y);
			if (old == solid)
				return;

			Target.SetCollision(x, y, solid);
			_collisionChanges.Add(new TileCollisionUndoAction.CellChange(x, y, old, solid));
		}

		private void CommitCollisionStroke()
		{
			_isCollisionStroking = false;
			_isDraggingRect = false;

			if (_collisionChanges.Count == 0)
				return;

			var verb = _collisionErases ? "Clear" : "Paint";
			var description = $"{verb} {_collisionChanges.Count} collision cell{(_collisionChanges.Count == 1 ? "" : "s")}";

			EditorChangeTracker.PushUndo(
				new TileCollisionUndoAction(Target, _collisionChanges, description),
				Target.Entity,
				description);

			Target.RebuildColliders();
			_collisionChanges.Clear();
		}

		/// <summary>Commits a collision stroke abandoned by the cursor leaving the viewport. See CommitPendingStroke.</summary>
		private void CommitPendingCollisionStroke()
		{
			_isDraggingRect = false;

			if (_isCollisionStroking)
				CommitCollisionStroke();
		}

		private void AbandonCollisionStroke()
		{
			if (_isCollisionStroking)
			{
				for (var i = _collisionChanges.Count - 1; i >= 0; i--)
					Target?.SetCollision(_collisionChanges[i].X, _collisionChanges[i].Y, _collisionChanges[i].OldSolid);
			}

			_collisionChanges.Clear();
			_isCollisionStroking = false;
		}

		/// <summary>Draws the existing solid cells as translucent fills - the MERGED rects, so it also shows exactly
		/// what colliders will be generated.</summary>
		private void DrawCollisionOverlay(Camera camera)
		{
			if (Target?.Entity == null)
				return;

			var view = camera.Bounds;

			foreach (var rect in Target.GetCollisionRects())
			{
				var worldMin = CellToWorldF(rect.X, rect.Y);
				var worldMax = CellToWorldF(rect.X + rect.Width, rect.Y + rect.Height);

				var box = new RectangleF(worldMin.X, worldMin.Y, worldMax.X - worldMin.X, worldMax.Y - worldMin.Y);
				if (!box.Intersects(view))
					continue;

				Debug.DrawHollowRect(new Rectangle((int)box.X, (int)box.Y, (int)box.Width, (int)box.Height),
					CollisionColor);
			}
		}
	}
}
