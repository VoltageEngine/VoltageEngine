using System.Collections.Generic;

namespace Voltage.Editor.Undo.Core;

/// <summary>
/// Several actions folded into one undo step, so a multi-selection edit reverts in a single Ctrl+Z rather than
/// one press per affected object. Undo runs in reverse order; redo replays in the original order.
/// </summary>
public sealed class CompositeUndoAction : EditorChangeTracker.IEditorAction
{
	private readonly List<EditorChangeTracker.IEditorAction> _actions;
	private readonly string _description;

	public string Description => _description;

	public CompositeUndoAction(List<EditorChangeTracker.IEditorAction> actions, string description)
	{
		_actions = actions ?? new List<EditorChangeTracker.IEditorAction>();
		_description = description;
	}

	public void Undo()
	{
		for (var i = _actions.Count - 1; i >= 0; i--)
			_actions[i].Undo();
	}

	public void Redo()
	{
		for (var i = 0; i < _actions.Count; i++)
			_actions[i].Redo();
	}
}
