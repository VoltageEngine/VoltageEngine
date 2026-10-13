using Voltage;
using Voltage.Editor.Gizmos;
using Voltage.Editor.Undo.Core;

namespace Voltage.Editor.Undo.ComponentActions;

public sealed class LightBoundsResizeUndoAction : EditorChangeTracker.IEditorAction
{
    private readonly RenderableComponent _light;
    private readonly LightBoundsResizeState _before, _after;
    public string Description { get; }

    public LightBoundsResizeUndoAction(RenderableComponent light, LightBoundsResizeState before, LightBoundsResizeState after, string description)
    {
        _light = light; _before = before; _after = after; Description = description;
    }

    public void Undo() { if (_light.Entity != null) _before.Restore(); }
    public void Redo() { if (_light.Entity != null) _after.Restore(); }
}
