using System;
using System.Collections.Generic;
using Voltage.Editor.Undo.Core;

namespace Voltage.Editor.Undo.ComponentActions;

/// <summary>
/// One undo step for a whole tile-painting stroke. A cell is recorded as its whole STACK of tiles (bottom-to-top),
/// not a single index; an empty array means an empty cell.
/// </summary>
public class TilePaintUndoAction : EditorChangeTracker.IEditorAction
{
	public readonly struct CellChange
	{
		public readonly int X;
		public readonly int Y;
		public readonly int[] OldStack;
		public readonly int[] NewStack;

		public CellChange(int x, int y, int[] oldStack, int[] newStack)
		{
			X = x;
			Y = y;
			OldStack = oldStack ?? Array.Empty<int>();
			NewStack = newStack ?? Array.Empty<int>();
		}
	}

	private readonly TilemapRenderer _map;
	private readonly List<CellChange> _changes;
	private readonly string _description;

	public string Description => _description;

	public TilePaintUndoAction(TilemapRenderer map, List<CellChange> changes, string description)
	{
		_map = map;
		_changes = new List<CellChange>(changes);
		_description = description;
	}

	public void Undo()
	{
		if (_map == null)
			return;

		for (var i = _changes.Count - 1; i >= 0; i--)
		{
			var change = _changes[i];
			_map.SetCellStack(change.X, change.Y, change.OldStack);
		}
	}

	public void Redo()
	{
		if (_map == null)
			return;

		foreach (var change in _changes)
			_map.SetCellStack(change.X, change.Y, change.NewStack);
	}
}
