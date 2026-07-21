using System.Collections.Generic;
using Voltage.Editor.Undo.Core;

namespace Voltage.Editor.Undo.ComponentActions;

/// <summary>
/// One undo step for a whole-region tile edit (move, rotate). Unlike a paint stroke this snapshots the tile
/// stack, orientation AND collision flag of every touched cell, because moving a block carries all three.
/// </summary>
public class TileRegionUndoAction : EditorChangeTracker.IEditorAction
{
	public readonly struct CellState
	{
		public readonly int X;
		public readonly int Y;
		public readonly int[] Stack;
		public readonly byte Orientation;
		public readonly bool Solid;

		public CellState(int x, int y, int[] stack, byte orientation, bool solid)
		{
			X = x;
			Y = y;
			Stack = stack;
			Orientation = orientation;
			Solid = solid;
		}
	}

	private readonly TilemapRenderer _map;
	private readonly List<CellState> _before;
	private readonly List<CellState> _after;
	private readonly string _description;

	public string Description => _description;

	public TileRegionUndoAction(TilemapRenderer map, List<CellState> before, List<CellState> after, string description)
	{
		_map = map;
		_before = new List<CellState>(before);
		_after = new List<CellState>(after);
		_description = description;
	}

	public void Undo() => Apply(_before);

	public void Redo() => Apply(_after);

	private void Apply(List<CellState> states)
	{
		if (_map == null)
			return;

		foreach (var state in states)
		{
			_map.SetCellStack(state.X, state.Y, state.Stack);
			_map.SetOrientation(state.X, state.Y, state.Orientation);
			_map.SetCollision(state.X, state.Y, state.Solid);
		}

		_map.RebuildColliders();
	}
}
