using System;
using System.Collections.Generic;
using System.Reflection;
using Voltage.Editor.Undo.Core;
using Voltage.Persistence;

namespace Voltage.Editor.Undo.ComponentActions;

/// <summary>
/// Undo/Redo action for a field implementing <see cref="Voltage.IComponentGroup"/>.
/// Stores JSON snapshots of the group before and after the edit, then restores
/// them by traversing the member path from the root object.
/// </summary>
public sealed class ComponentGroupUndoAction : EditorChangeTracker.IEditorAction
{
    private readonly object _root;
    private readonly List<string> _path;
    private readonly string _snapshotBefore;
    private readonly string _snapshotAfter;
    private readonly Type _groupType;
    private readonly string _description;

    public string Description => _description;

    public ComponentGroupUndoAction(
        object root,
        List<string> path,
        string snapshotBefore,
        string snapshotAfter,
        Type groupType,
        string description)
    {
        _root = root;
        _path = path;
        _snapshotBefore = snapshotBefore;
        _snapshotAfter = snapshotAfter;
        _groupType = groupType;
        _description = description;
    }

    public void Undo() => ApplySnapshot(_snapshotBefore);
    public void Redo() => ApplySnapshot(_snapshotAfter);

    private void ApplySnapshot(string snapshot)
    {
        if (string.IsNullOrEmpty(snapshot))
            return;

        // Deserialize the snapshot into a fresh group instance
        var restored = Json.FromJson(snapshot, _groupType);
        if (restored == null)
            return;

        // Traverse the path to the parent object that owns the group field
        object current = _root;
        Type currentType = current.GetType();

        for (int i = 0; i < _path.Count - 1; i++)
        {
            var member = currentType.GetProperty(_path[i]) as MemberInfo
                         ?? currentType.GetField(_path[i]);

            current = member switch
            {
                PropertyInfo p => p.GetValue(current),
                FieldInfo f    => f.GetValue(current),
                _              => throw new InvalidOperationException($"Member '{_path[i]}' not found on '{currentType.Name}'.")
            };

            if (current == null)
                throw new InvalidOperationException($"Null at path segment '{_path[i]}'.");

            currentType = current.GetType();
        }

        // Get the existing group instance and copy all fields from the snapshot into it
        // (we mutate in-place to preserve the reference held by the component)
        string lastMemberName = _path[^1];
        var lastMember = currentType.GetProperty(lastMemberName) as MemberInfo
                         ?? currentType.GetField(lastMemberName);

        object groupInstance = lastMember switch
        {
            PropertyInfo p => p.GetValue(current),
            FieldInfo f    => f.GetValue(current),
            _              => throw new InvalidOperationException($"Member '{lastMemberName}' not found on '{currentType.Name}'.")
        };

        if (groupInstance == null)
            return;

        // Copy all public fields from restored → groupInstance
        foreach (var field in _groupType.GetFields(BindingFlags.Public | BindingFlags.Instance))
            field.SetValue(groupInstance, field.GetValue(restored));

        // Copy all public writable properties from restored → groupInstance
        foreach (var prop in _groupType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.CanRead && prop.CanWrite && prop.GetSetMethod() != null)
                prop.SetValue(groupInstance, prop.GetValue(restored));
        }
    }
}