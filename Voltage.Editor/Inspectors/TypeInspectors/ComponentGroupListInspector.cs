using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using ImGuiNET;
using Voltage.Editor.Undo.Core;
using Voltage.Persistence;
using Voltage.Utils;

namespace Voltage.Editor.Inspectors.TypeInspectors;

/// <summary>Edits lists of serialized groups using the existing member inspectors.</summary>
public class ComponentGroupListInspector : AbstractTypeInspector
{
    private readonly ConditionalWeakTable<object, List<AbstractTypeInspector>> _entries = new();
    private Type _elementType;

    public override void Initialize()
    {
        base.Initialize();
        _elementType = _valueType.GetGenericArguments()[0];
    }

    public override void DrawMutable()
    {
        var list = GetValue() as IList;
        if (!Gui.CollapsingHeader($"{_name} [{list?.Count ?? 0}]")) return;
        ImGui.Indent();
        if (Gui.Button("Add Element"))
        {
            var next = CopyList(list);
            next.Add(Activator.CreateInstance(_elementType));
            SetValueWithUndo(next);
            list = next;
        }
        if (list != null)
            for (int index = 0; index < list.Count; index++)
            {
                var item = list[index];
                ImGui.PushID(index);
                if (Gui.SmallButton("Remove"))
                {
                    var next = CopyList(list);
                    next.RemoveAt(index);
                    SetValueWithUndo(next);
                    ImGui.PopID();
                    break;
                }
                ImGui.SameLine();
                if (Gui.CollapsingHeader($"Element {index}"))
                {
                    if (item == null)
                        ImGui.TextDisabled("Empty element");
                    else
                        DrawEntry(item);
                }
                ImGui.PopID();
            }
        ImGui.Unindent();
    }

    public override void DrawReadOnly()
    {
        ImGui.BeginDisabled();
        DrawMutable();
        ImGui.EndDisabled();
    }

    private IList CopyList(IList current)
    {
        var copy = (IList)Activator.CreateInstance(_valueType);
        if (current != null) foreach (var item in current) copy.Add(item);
        return copy;
    }

    private void DrawEntry(object item)
    {
        var inspectors = _entries.GetValue(item, value =>
        {
            var entries = TypeInspectorUtils.GetInspectableProperties(value);
            foreach (var inspector in entries) inspector.IsUndoDisabled = true;
            return entries;
        });
        var before = Json.ToJson(item);
        int writes = WriteCountFor(item);
        ImGui.Indent();
        foreach (var inspector in inspectors) inspector.Draw();
        ImGui.Unindent();
        if (WriteCountFor(item) == writes) return;
        NoteValueWritten();
        var root = GetRootTarget();
        var description = $"{_name} element modified";
        if (!UndoSuppressed)
            EditorChangeTracker.PushUndo(new EntryAction(root, item, before, Json.ToJson(item), description), root, description);
        EditorChangeTracker.MarkChanged(root, description);
    }

    private sealed class EntryAction : EditorChangeTracker.IEditorAction
    {
        private readonly object _root, _item;
        private readonly string _before, _after;
        public string Description { get; }

        public EntryAction(object root, object item, string before, string after, string description)
        {
            _root = root; _item = item; _before = before; _after = after; Description = description;
        }

        public void Undo() => Apply(_before);
        public void Redo() => Apply(_after);

        private void Apply(string snapshot)
        {
            var type = _item.GetType();
            var restored = Json.FromJson(snapshot, type);
            foreach (var field in ReflectionUtils.GetFields(type))
                if (!field.IsStatic && !field.IsInitOnly && (field.IsPublic || field.IsDefined(typeof(SerializeAttribute))))
                    field.SetValue(_item, field.GetValue(restored));
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (property.GetIndexParameters().Length == 0 && property.GetMethod?.IsPublic == true && property.SetMethod?.IsPublic == true)
                    property.SetValue(_item, property.GetValue(restored));
            NoteExternalValueWrite(_item);
            NoteExternalValueWrite(_root);
        }
    }
}
