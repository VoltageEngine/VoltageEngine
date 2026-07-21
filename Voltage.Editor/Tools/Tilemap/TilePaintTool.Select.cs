using System;
using System.Collections.Generic;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Voltage.Editor.Undo.ComponentActions;
using Voltage.Editor.Undo.Core;

namespace Voltage.Editor.Tools.Tilemap
{
	public partial class TilePaintTool
	{
		/// <summary>Marquee boxes over placed tiles, in cell space. Ctrl/Shift add more without clearing.</summary>
		private readonly List<Rectangle> _selectBoxes = new();

		private bool _isMarqueeing;
		private Point _marqueeAnchor;
		private Point _marqueeEnd;
		private bool _marqueeAdditive;

		private bool _isMovingSelection;
		private Point _moveGrabCell;
		private Point _moveOffset;

		public bool HasTileSelection => _selectBoxes.Count > 0;

		public int SelectedCellCount
		{
			get
			{
				var total = 0;
				foreach (var cell in SelectedCells())
					total++;

				return total;
			}
		}

		public void ClearTileSelection()
		{
			_selectBoxes.Clear();
			_isMarqueeing = false;
			_isMovingSelection = false;
			_moveOffset = Point.Zero;
		}

		/// <summary>Every distinct cell across all boxes; overlaps are yielded once.</summary>
		private IEnumerable<Point> SelectedCells()
		{
			var seen = new HashSet<long>();

			foreach (var box in _selectBoxes)
			{
				for (var y = box.Y; y < box.Y + box.Height; y++)
				{
					for (var x = box.X; x < box.X + box.Width; x++)
					{
						if (seen.Add(CellKey(x, y)))
							yield return new Point(x, y);
					}
				}
			}
		}

		private static long CellKey(int x, int y) => (long)x << 32 ^ (uint)y;

		private static Rectangle BoxFrom(Point a, Point b) => new(
			Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
			Math.Abs(a.X - b.X) + 1, Math.Abs(a.Y - b.Y) + 1);

		private bool IsCellSelected(Point cell)
		{
			foreach (var box in _selectBoxes)
			{
				if (box.Contains(cell))
					return true;
			}

			return false;
		}

		/// <summary>Marquee-select placed tiles, then drag the selection to move it a whole number of cells.</summary>
		private void UpdateSelect(Point cell)
		{
			DrawSelectionOutlines();

			if (_isMovingSelection)
			{
				UpdateSelectionDrag(cell);
				return;
			}

			if (_isMarqueeing)
			{
				UpdateMarquee(cell);
				return;
			}

			DrawCellOutline(cell.X, cell.Y, 1, 1, HighlightColor);

			if (!Input.LeftMouseButtonPressed)
				return;

			// Pressing inside the selection grabs it; pressing outside starts a new marquee.
			if (IsCellSelected(cell) && Target?.Entity != null)
			{
				_isMovingSelection = true;
				_moveGrabCell = cell;
				_moveOffset = Point.Zero;
				return;
			}

			var io = ImGui.GetIO();
			_marqueeAdditive = io.KeyCtrl || io.KeySuper || io.KeyShift;

			if (!_marqueeAdditive)
				_selectBoxes.Clear();

			_isMarqueeing = true;
			_marqueeAnchor = cell;
			_marqueeEnd = cell;
		}

		private void UpdateMarquee(Point cell)
		{
			_marqueeEnd = cell;

			var box = BoxFrom(_marqueeAnchor, _marqueeEnd);
			DrawCellOutline(box.X, box.Y, box.Width, box.Height, new Color(0.3f, 0.8f, 1f, 0.9f));

			if (!Input.LeftMouseButtonReleased)
				return;

			_isMarqueeing = false;

			// Keep only boxes that actually cover a tile, so a stray click does not leave an empty selection.
			if (RegionHasTiles(box))
				_selectBoxes.Add(box);
		}

		private bool RegionHasTiles(Rectangle box)
		{
			if (Target?.Entity == null)
				return false;

			for (var y = box.Y; y < box.Y + box.Height; y++)
			{
				for (var x = box.X; x < box.X + box.Width; x++)
				{
					if (Target.HasTile(x, y) || Target.GetCollision(x, y))
						return true;
				}
			}

			return false;
		}

		private void UpdateSelectionDrag(Point cell)
		{
			_moveOffset = new Point(cell.X - _moveGrabCell.X, cell.Y - _moveGrabCell.Y);

			// Ghost of where the block will land.
			foreach (var box in _selectBoxes)
			{
				DrawCellOutline(box.X + _moveOffset.X, box.Y + _moveOffset.Y, box.Width, box.Height,
					new Color(0.4f, 1f, 0.5f, 0.9f));
			}

			if (!Input.LeftMouseButtonReleased)
				return;

			_isMovingSelection = false;

			if (_moveOffset != Point.Zero)
				MoveSelection(_moveOffset);

			_moveOffset = Point.Zero;
		}

		/// <summary>Lifts every selected cell and writes it back shifted by <paramref name="offset"/>, as one undo step.</summary>
		private void MoveSelection(Point offset)
		{
			if (Target?.Entity == null)
				return;

			var cells = new List<Point>(SelectedCells());
			var before = new List<TileRegionUndoAction.CellState>();
			var after = new List<TileRegionUndoAction.CellState>();
			var touched = new HashSet<long>();

			// Snapshot sources and destinations before writing anything, or a move onto overlapping cells
			// would record state we have already clobbered.
			var lifted = new List<TileRegionUndoAction.CellState>();

			foreach (var cell in cells)
			{
				lifted.Add(Snapshot(cell.X, cell.Y));

				if (touched.Add(CellKey(cell.X, cell.Y)))
					before.Add(Snapshot(cell.X, cell.Y));
			}

			foreach (var cell in cells)
			{
				var dx = cell.X + offset.X;
				var dy = cell.Y + offset.Y;

				if (touched.Add(CellKey(dx, dy)))
					before.Add(Snapshot(dx, dy));
			}

			// Clear the sources first so a partial overlap does not erase what we just wrote.
			foreach (var cell in cells)
				WriteCell(cell.X, cell.Y, Array.Empty<int>(), 0, false);

			foreach (var state in lifted)
				WriteCell(state.X + offset.X, state.Y + offset.Y, state.Stack, state.Orientation, state.Solid);

			foreach (var key in touched)
				after.Add(Snapshot((int)(key >> 32), (int)(uint)key));

			Target.RebuildColliders();

			for (var i = 0; i < _selectBoxes.Count; i++)
			{
				var box = _selectBoxes[i];
				_selectBoxes[i] = new Rectangle(box.X + offset.X, box.Y + offset.Y, box.Width, box.Height);
			}

			EditorChangeTracker.PushUndo(
				new TileRegionUndoAction(Target, before, after, $"Move {cells.Count} tiles"),
				Target.Entity,
				$"Move {cells.Count} tiles");
		}

		/// <summary>
		/// Rotates the selected block 90° about its own centre, exactly as turning the whole bounding box would:
		/// a W x H block becomes H x W, re-centred on the same point. Each tile's own orientation turns with it.
		/// </summary>
		public void RotateSelection(int quarterTurns)
		{
			var turns = ((quarterTurns % 4) + 4) % 4;

			if (Target?.Entity == null || _selectBoxes.Count == 0 || turns == 0)
				return;

			var bounds = SelectionBounds();

			// A quarter turn swaps the box's dimensions; keeping the centre fixed is what makes it rotate in place.
			var rotatedWidth = turns % 2 == 1 ? bounds.Height : bounds.Width;
			var rotatedHeight = turns % 2 == 1 ? bounds.Width : bounds.Height;
			var originX = bounds.X + (bounds.Width - rotatedWidth) / 2;
			var originY = bounds.Y + (bounds.Height - rotatedHeight) / 2;

			Point Map(int x, int y)
			{
				var local = RotateLocal(new Point(x - bounds.X, y - bounds.Y), bounds.Width, bounds.Height, turns);
				return new Point(originX + local.X, originY + local.Y);
			}

			var cells = new List<Point>(SelectedCells());
			var lifted = new List<TileRegionUndoAction.CellState>();

			foreach (var cell in cells)
				lifted.Add(Snapshot(cell.X, cell.Y));

			var before = new List<TileRegionUndoAction.CellState>();
			var touched = new HashSet<long>();

			foreach (var cell in cells)
			{
				if (touched.Add(CellKey(cell.X, cell.Y)))
					before.Add(Snapshot(cell.X, cell.Y));
			}

			var rotated = new List<TileRegionUndoAction.CellState>();

			foreach (var state in lifted)
			{
				var target = Map(state.X, state.Y);
				var orientation = TilemapRenderer.MakeOrientation(
					(state.Orientation & TilemapRenderer.OrientRotationMask) + turns,
					(state.Orientation & TilemapRenderer.OrientFlipX) != 0,
					(state.Orientation & TilemapRenderer.OrientFlipY) != 0);

				if (touched.Add(CellKey(target.X, target.Y)))
					before.Add(Snapshot(target.X, target.Y));

				rotated.Add(new TileRegionUndoAction.CellState(
					target.X, target.Y, state.Stack, orientation, state.Solid));
			}

			foreach (var cell in cells)
				WriteCell(cell.X, cell.Y, Array.Empty<int>(), 0, false);

			foreach (var state in rotated)
				WriteCell(state.X, state.Y, state.Stack, state.Orientation, state.Solid);

			// The boxes must follow the tiles, or the next rotate would only pick up whatever still fell inside
			// the old bounds and the selection would shrink away.
			for (var i = 0; i < _selectBoxes.Count; i++)
			{
				var box = _selectBoxes[i];
				var a = Map(box.X, box.Y);
				var b = Map(box.X + box.Width - 1, box.Y + box.Height - 1);

				_selectBoxes[i] = new Rectangle(
					Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
					Math.Abs(a.X - b.X) + 1, Math.Abs(a.Y - b.Y) + 1);
			}

			var after = new List<TileRegionUndoAction.CellState>();
			foreach (var key in touched)
				after.Add(Snapshot((int)(key >> 32), (int)(uint)key));

			Target.RebuildColliders();

			var description = $"Rotate {cells.Count} tiles";
			EditorChangeTracker.PushUndo(
				new TileRegionUndoAction(Target, before, after, description), Target.Entity, description);
		}

		/// <summary>Maps a cell inside a W x H block to its position after <paramref name="turns"/> quarter turns.</summary>
		private static Point RotateLocal(Point local, int width, int height, int turns) => turns switch
		{
			1 => new Point(height - 1 - local.Y, local.X),
			2 => new Point(width - 1 - local.X, height - 1 - local.Y),
			3 => new Point(local.Y, width - 1 - local.X),
			_ => local,
		};

		/// <summary>Clears every selected cell, tiles and collision alike, as one undo step.</summary>
		public void DeleteSelection()
		{
			if (Target?.Entity == null || _selectBoxes.Count == 0)
				return;

			var cells = new List<Point>(SelectedCells());
			var before = new List<TileRegionUndoAction.CellState>();
			var changed = false;

			foreach (var cell in cells)
			{
				var state = Snapshot(cell.X, cell.Y);
				before.Add(state);

				if (state.Stack.Length > 0 || state.Solid)
					changed = true;
			}

			// Deleting an already-empty region would otherwise push a no-op undo entry.
			if (!changed)
				return;

			foreach (var cell in cells)
				WriteCell(cell.X, cell.Y, Array.Empty<int>(), 0, false);

			var after = new List<TileRegionUndoAction.CellState>();
			foreach (var cell in cells)
				after.Add(Snapshot(cell.X, cell.Y));

			Target.RebuildColliders();

			var description = $"Delete {cells.Count} tiles";
			EditorChangeTracker.PushUndo(
				new TileRegionUndoAction(Target, before, after, description), Target.Entity, description);
		}

		/// <summary>Delete / Backspace clears the selected tiles while the Select tool owns a selection.</summary>
		public void UpdateSelectionHotkeys()
		{
			if (Tool != TileTool.Select || !HasTileSelection)
				return;

			// Only WantTextInput: WantCaptureKeyboard is true whenever any editor window has focus.
			if (ImGui.GetIO().WantTextInput)
				return;

			if (ImGui.IsKeyPressed(ImGuiKey.Delete, false) || ImGui.IsKeyPressed(ImGuiKey.Backspace, false))
				DeleteSelection();
		}

		private Rectangle SelectionBounds()
		{
			int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

			foreach (var box in _selectBoxes)
			{
				minX = Math.Min(minX, box.X);
				minY = Math.Min(minY, box.Y);
				maxX = Math.Max(maxX, box.X + box.Width - 1);
				maxY = Math.Max(maxY, box.Y + box.Height - 1);
			}

			return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
		}

		private TileRegionUndoAction.CellState Snapshot(int x, int y) => new(
			x, y, Target.GetCellStack(x, y), Target.GetOrientation(x, y), Target.GetCollision(x, y));

		private void WriteCell(int x, int y, int[] stack, byte orientation, bool solid)
		{
			Target.SetCellStack(x, y, stack);
			Target.SetOrientation(x, y, orientation);
			Target.SetCollision(x, y, solid);
		}

		private void DrawSelectionOutlines()
		{
			if (_isMovingSelection)
				return;

			foreach (var box in _selectBoxes)
				DrawCellOutline(box.X, box.Y, box.Width, box.Height, new Color(0.3f, 0.8f, 1f, 0.9f));
		}
	}
}
