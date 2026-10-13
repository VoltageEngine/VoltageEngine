using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Voltage.Editor.Gizmos;
using Voltage.Editor.Undo.Core;

namespace Voltage.Editor.Undo.ComponentActions;

public sealed class PolygonBoundsPointEditUndoAction : EditorChangeTracker.IEditorAction
{
    private readonly Component _target;
    private readonly List<Vector2> _before, _after;
    public string Description { get; }

    public PolygonBoundsPointEditUndoAction(Component target, List<Vector2> before, List<Vector2> after, string description)
    {
        _target = target; _before = new(before); _after = new(after); Description = description;
    }

    public void Undo() { if (_target.Entity != null) PolygonBoundsEditor.SetPoints(_target, _before); }
    public void Redo() { if (_target.Entity != null) PolygonBoundsEditor.SetPoints(_target, _after); }
}
