using Microsoft.Xna.Framework;
using Voltage.Editor.Undo.ComponentActions;

namespace Voltage.Editor.Tools.Tilemap
{
	public partial class TilePaintTool
	{
		/// <summary>Active autotile terrain id, or -1. When set, the brush paints this terrain instead of the tile selection.</summary>
		public int ActiveTerrain = -1;

		public bool IsAutotiling => ActiveTerrain >= 0 && EditMode == TileEditMode.Tiles;

		// Neighbour offsets in bit order: 0=N,1=NE,2=E,3=SE,4=S,5=SW,6=W,7=NW (Y down).
		private static readonly Point[] _terrainNeighbours =
		{
			new(0, -1), new(1, -1), new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1),
		};

		private void UpdateAutotile(Point cell, bool erase)
		{
			DrawCellOutline(cell.X, cell.Y, 1, 1, erase ? new Color(1f, 0.35f, 0.35f, 0.9f) : HighlightColor);

			var pressed = Input.LeftMouseButtonPressed || (erase && Input.RightMouseButtonPressed);
			var down = Input.LeftMouseButtonDown || (erase && Input.RightMouseButtonDown);
			var released = Input.LeftMouseButtonReleased || Input.RightMouseButtonReleased;

			if (pressed)
			{
				if (Target?.Entity == null && (erase || !EnsurePaintTarget()))
					return;

				BeginStroke(erase);
				AutotileAt(cell, erase);
			}
			else if (down && _isStroking)
			{
				AutotileAt(cell, erase);
			}

			if (released && _isStroking)
				CommitStroke();
		}

		private void AutotileAt(Point cell, bool erase)
		{
			var tileset = Target.ResolvedTileset;
			if (tileset == null || !tileset.TerrainHasTiles(ActiveTerrain))
				return;

			if (erase)
			{
				if (tileset.IsTerrainMember(ActiveTerrain, Target.GetTile(cell.X, cell.Y)))
					RecordCellSet(cell.X, cell.Y, -1);
			}
			else
			{
				// Make the cell a member, then re-resolve it and every terrain neighbour so their masks account
				// for the change.
				RecordCellSet(cell.X, cell.Y, tileset.ResolveAutotile(ActiveTerrain, ComputeTerrainMask(cell.X, cell.Y)));
			}

			ResolveTerrainCell(cell.X, cell.Y);
			foreach (var off in _terrainNeighbours)
				ResolveTerrainCell(cell.X + off.X, cell.Y + off.Y);
		}

		private void ResolveTerrainCell(int x, int y)
		{
			var tileset = Target.ResolvedTileset;
			if (tileset == null || !tileset.IsTerrainMember(ActiveTerrain, Target.GetTile(x, y)))
				return;

			RecordCellSet(x, y, tileset.ResolveAutotile(ActiveTerrain, ComputeTerrainMask(x, y)));
		}

		private byte ComputeTerrainMask(int x, int y)
		{
			var tileset = Target.ResolvedTileset;
			byte mask = 0;

			for (var bit = 0; bit < 8; bit++)
			{
				var off = _terrainNeighbours[bit];
				if (tileset.IsTerrainMember(ActiveTerrain, Target.GetTile(x + off.X, y + off.Y)))
					mask |= (byte)(1 << bit);
			}

			return mask;
		}

		// Sets a cell to a single tile (no stack, no orientation) and records it in the current stroke for undo.
		private void RecordCellSet(int x, int y, int tileIndex)
		{
			var oldStack = Target.GetCellStack(x, y);

			if (tileIndex < 0)
			{
				Target.SetTile(x, y, -1);
			}
			else
			{
				Target.SetTile(x, y, tileIndex);
				Target.SetOrientation(x, y, 0);
			}

			var newStack = Target.GetCellStack(x, y);

			if (StacksEqual(oldStack, newStack))
				return;

			_strokeChanges.Add(new TilePaintUndoAction.CellChange(x, y, oldStack, newStack));
		}
	}
}
