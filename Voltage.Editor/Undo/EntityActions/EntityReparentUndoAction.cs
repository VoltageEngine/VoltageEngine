using System.Collections.Generic;
using Voltage.Editor.Undo.Core;

namespace Voltage.Editor.Undo.EntityActions;

/// <summary>
/// Undo/Redo action for reparenting one or more entities, preserving sibling order.
/// </summary>
public class EntityReparentUndoAction : EditorChangeTracker.IEditorAction
{
    private readonly List<(Entity entity, Transform oldParent, int oldIndex, Transform newParent, int newIndex)> _entries;
    private readonly string _description;

    public string Description => _description;

    public EntityReparentUndoAction(
        List<(Entity entity, Transform oldParent, int oldIndex, Transform newParent, int newIndex)> entries,
        string description)
    {
        _entries = entries;
        _description = description;
    }

    public void Undo()
    {
        foreach (var entry in _entries)
        {
            var worldPos = entry.entity.Transform.Position;
            var worldRot = entry.entity.Transform.Rotation;
            var worldScale = entry.entity.Transform.Scale;

            entry.entity.Transform.SetParentAt(entry.oldParent, entry.oldIndex);
            if (entry.oldParent == null)
                entry.entity.Scene?.Entities.MoveEntityToIndex(entry.entity, entry.oldIndex);

            entry.entity.Transform.RecomputeLocalsFromWorld(worldPos, worldRot, worldScale);
        }
    }

    public void Redo()
    {
        foreach (var entry in _entries)
        {
            var worldPos = entry.entity.Transform.Position;
            var worldRot = entry.entity.Transform.Rotation;
            var worldScale = entry.entity.Transform.Scale;

            entry.entity.Transform.SetParentAt(entry.newParent, entry.newIndex);
            if (entry.newParent == null)
                entry.entity.Scene?.Entities.MoveEntityToIndex(entry.entity, entry.newIndex);

            entry.entity.Transform.RecomputeLocalsFromWorld(worldPos, worldRot, worldScale);
        }
    }
}
