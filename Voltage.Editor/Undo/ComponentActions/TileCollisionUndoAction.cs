using System.Collections.Generic;
using Voltage.Editor.Undo.Core;

namespace Voltage.Editor.Undo.ComponentActions;

/// <summary>One undo step for a collision-painting stroke, recorded as per-cell before/after solid flags.</summary>
public class TileCollisionUndoAction : EditorChangeTracker.IEditorAction
{
	public readonly struct CellChange
	{
		public readonly int X;
		public readonly int Y;
		public readonly bool OldSolid;
		public readonly bool NewSolid;

		public CellChange(int x, int y, bool oldSolid, bool newSolid)
		{
			X = x;
			Y = y;
			OldSolid = oldSolid;
			NewSolid = newSolid;
		}
	}

	private readonly TilemapRenderer _map;
	private readonly List<CellChange> _changes;
	private readonly string _description;

	public string Description => _description;

	public TileCollisionUndoAction(TilemapRenderer map, List<CellChange> changes, string description)
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
			_map.SetCollision(_changes[i].X, _changes[i].Y, _changes[i].OldSolid);

		_map.RebuildColliders();
	}

	public void Redo()
	{
		if (_map == null)
			return;

		foreach (var change in _changes)
			_map.SetCollision(change.X, change.Y, change.NewSolid);

		_map.RebuildColliders();
	}
}
