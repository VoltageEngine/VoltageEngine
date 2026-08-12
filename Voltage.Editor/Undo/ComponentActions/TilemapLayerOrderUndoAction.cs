using System.Collections.Generic;
using Voltage.Editor.Undo.Core;

namespace Voltage.Editor.Undo.ComponentActions;

/// <summary>
/// One undo step for a tilemap layer reorder, recorded as the render layer and depth each layer held before and
/// after. Draw order is nothing but those two numbers, so putting them back puts the order back - no matter how
/// many layers one move renumbered.
/// </summary>
public class TilemapLayerOrderUndoAction : EditorChangeTracker.IEditorAction
{
	public readonly struct LayerOrder
	{
		public readonly TilemapRenderer Map;
		public readonly int OldRenderLayer;
		public readonly float OldLayerDepth;
		public readonly int NewRenderLayer;
		public readonly float NewLayerDepth;

		public LayerOrder(TilemapRenderer map, int oldRenderLayer, float oldLayerDepth,
			int newRenderLayer, float newLayerDepth)
		{
			Map = map;
			OldRenderLayer = oldRenderLayer;
			OldLayerDepth = oldLayerDepth;
			NewRenderLayer = newRenderLayer;
			NewLayerDepth = newLayerDepth;
		}
	}

	private readonly List<LayerOrder> _changes;
	private readonly string _description;

	public string Description => _description;

	public TilemapLayerOrderUndoAction(List<LayerOrder> changes, string description)
	{
		_changes = new List<LayerOrder>(changes);
		_description = description;
	}

	public void Undo() => Apply(true);

	public void Redo() => Apply(false);

	private void Apply(bool restoreOld)
	{
		foreach (var change in _changes)
		{
			// A layer deleted since the move is skipped: the ones still here are restored regardless, which beats
			// abandoning the whole step over one missing entity.
			if (change.Map?.Entity == null)
				continue;

			change.Map.RenderLayer = restoreOld ? change.OldRenderLayer : change.NewRenderLayer;
			change.Map.LayerDepth = restoreOld ? change.OldLayerDepth : change.NewLayerDepth;
		}
	}
}
