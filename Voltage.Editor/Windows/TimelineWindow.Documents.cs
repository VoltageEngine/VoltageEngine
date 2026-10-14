using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImGuiNET;
using Voltage.Cinematics;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.ProjectFile;
using Num = System.Numerics;

namespace Voltage.Editor.Windows;

public partial class TimelineWindow
{
    private readonly TimelineDocument _document = new();
    private TimelinePreviewSession _preview;
    private List<Entity> _documentSelection = new();
    private bool _locked;
    private bool _standalone;
    internal bool HasUnsavedChanges => _document.IsDirty || _document.HasPendingSwitch;
    internal bool HasActivePreview => _preview != null;

    internal bool PrepareExit(Action continuation)
    {
        DisposePreview();
        if (!_document.IsDirty) return !_document.HasPendingSwitch;
        _imgui ??= Core.GetGlobalManager<ImGuiManager>();
        IsOpen = true;
        _imgui.ShowTimelineWindow = true;
        _document.RequestTransition(() => { _asset = _document.Asset; continuation(); });
        return false;
    }

    internal void SetVisible(bool visible)
    {
        if (visible) IsOpen = true;
        else if (IsOpen) RequestClose();
    }

    private void RequestClose()
    {
        DisposePreview();
        _document.RequestTransition(() => { _asset = _document.Asset; IsOpen = false; });
        if (_document.HasPendingSwitch) IsOpen = true;
    }

    private static bool IsCurrentSceneEntity(Entity entity) => entity != null && Core.Scene != null &&
        ReferenceEquals(entity.Scene, Core.Scene) && Core.Scene.Entities.Contains(entity);

    private void SelectOwner(Entity entity)
    {
        var pane = _imgui?.SceneGraphWindow?.EntityPane;
        if (pane == null) return;
        pane.DeselectAllEntities();
        if (IsCurrentSceneEntity(entity)) pane.SelectForNavigation(entity);
        _imgui.MainEntityInspectorWindow?.DelayedSetEntity(entity);
    }

    private void RestoreDocumentSelection()
    {
        var pane = _imgui?.SceneGraphWindow?.EntityPane;
        if (pane == null) return;
        pane.DeselectAllEntities();
        foreach (var entity in _documentSelection.Where(IsCurrentSceneEntity)) pane.SetSelectedEntity(entity, true);
        _imgui.MainEntityInspectorWindow?.DelayedSetEntity(pane.SelectedEntities.LastOrDefault());
    }

    private void RequestDirector(TimelineDirector director, bool selectOwner)
    {
        try
        {
            var asset = director != null ? LoadOrNull(director) : null;
            var path = director?.Timeline.ResolvePath();
            var requestedSelection = _imgui.SceneGraphWindow.EntityPane.SelectedEntities.ToList();
            _document.Request(asset, path, () =>
            {
                StopPreview();
                _director = IsCurrentSceneEntity(director?.Entity) ? director : null;
                _asset = _document.Asset;
                _standalone = false;
                if (selectOwner && _director != null) SelectOwner(_director.Entity);
                else
                {
                    var pane = _imgui.SceneGraphWindow.EntityPane;
                    pane.DeselectAllEntities();
                    foreach (var entity in requestedSelection.Where(IsCurrentSceneEntity)) pane.SetSelectedEntity(entity, true);
                    _imgui.MainEntityInspectorWindow?.DelayedSetEntity(pane.SelectedEntities.LastOrDefault());
                }
                _documentSelection = _imgui.SceneGraphWindow.EntityPane.SelectedEntities.Where(IsCurrentSceneEntity).ToList();
                CancelDrag();
            });
            if (_document.HasPendingSwitch) { DisposePreview(); RestoreDocumentSelection(); }
        }
        catch (Exception ex) { _status = "Load failed: " + ex.Message; RestoreDocumentSelection(); }
    }

    private void OpenWorkingCopy(TimelineAsset asset, string path, TimelineDirector director, bool assign)
    {
        _document.Request(asset, path, () =>
        {
            StopPreview();
            _asset = _document.Asset;
            _director = IsCurrentSceneEntity(director?.Entity) ? director : null;
            if (assign && _director != null) AssignTimelineToDirector(path);
            CancelDrag();
        });
    }

    internal bool RequestAsset(string path)
    {
        _imgui ??= Core.GetGlobalManager<ImGuiManager>();
        var loaded = _document.RequestOpen(path, ProjectManager.Instance.CurrentProject?.ProjectPath, () =>
        {
            StopPreview();
            _asset = _document.Asset;
            _director = null;
            _standalone = true;
            _documentSelection = _imgui.SceneGraphWindow.EntityPane.SelectedEntities.Where(IsCurrentSceneEntity).ToList();
            CancelDrag();
            _status = "Loaded " + Path.GetFileName(_document.Path) + ".";
        });
        if (_document.Error != null) _status = _document.Error;
        if (_document.HasPendingSwitch) DisposePreview();
        return loaded;
    }

    private void DrawSceneTimelines()
    {
        ImGui.TextWrapped("Select a cutscene attached to this scene, or drop a .vtimeline asset here.");
        var directors = Core.Scene?.Entities.SelectMany(e => e.GetComponents<TimelineDirector>()).ToList() ?? new List<TimelineDirector>();
        if (directors.Count == 0) ImGui.TextColored(Muted, "This scene has no Timeline Directors.");
        foreach (var director in directors.OrderBy(d => d.Entity.Name).ThenBy(d => d.Entity.PersistentId))
        {
            var name = director.Timeline.AssetName;
            var label = string.IsNullOrEmpty(name) ? director.Entity.Name : director.Entity.Name + " / " + name;
            ImGui.PushID(director.Entity.PersistentId.ToString());
            if (Gui.Selectable(label)) RequestDirector(director, true);
            ImGui.PopID();
        }
        DrawFileButtons();
    }

    private void DrawDocumentHeader()
    {
        Gui.Checkbox("Lock timeline", ref _locked);
        ImGui.TextWrapped((Path.GetFileName(_document.Path) ?? "Unsaved timeline") + (_document.IsDirty ? " *" : ""));
        if (_director == null)
        {
            ImGui.TextWrapped("Asset tracks are open. Choose a compatible director in this scene to preview its bindings.");
            foreach (var director in Core.Scene?.Entities.SelectMany(e => e.GetComponents<TimelineDirector>()) ?? Enumerable.Empty<TimelineDirector>())
            {
                if (string.IsNullOrEmpty(_document.Path) || !string.Equals(director.Timeline.ResolvePath(), _document.Path, StringComparison.OrdinalIgnoreCase)) continue;
                ImGui.PushID(director.Entity.PersistentId.ToString());
                if (Gui.Button("Preview with " + director.Entity.Name))
                {
                    _director = director;
                    _standalone = false;
                    SelectOwner(director.Entity);
                    _documentSelection = _imgui.SceneGraphWindow.EntityPane.SelectedEntities.ToList();
                }
                ImGui.PopID();
            }
        }
        else
        {
            var missing = _asset.Roles.Where(r => RoleEntity(r.Name) == null).Select(r => r.Name).ToArray();
            if (missing.Length > 0) ImGui.TextWrapped("Missing scene bindings: " + string.Join(", ", missing));
        }
        if (Gui.Button("Scene cutscenes"))
            _document.Request(null, null, () => { StopPreview(); _asset = null; _director = null; _standalone = false; _locked = false; _status = null; SelectOwner(null); });
        if (_preview?.UnsupportedEffects.Count > 0)
            ImGui.TextWrapped("Preview omits: " + string.Join(", ", _preview.UnsupportedEffects));
    }

    private void DrawAssetDropTarget()
    {
        Gui.Button("Drop timeline asset here", new Num.Vector2(-1, 0));
        if (!ImGui.BeginDragDropTarget()) return;
        var payload = ImGui.AcceptDragDropPayload(AssetBrowserWindow.DragDropPayloadId);
        bool accepted;
        unsafe { accepted = payload.NativePtr != null; }
        if (accepted && !AssetBrowserWindow.DraggedReference.IsEmpty)
        {
            var reference = AssetBrowserWindow.DraggedReference;
            AssetBrowserWindow.DraggedReference = Assets.AssetReference.Empty;
            RequestAsset(Assets.AssetDatabase.Instance?.Resolve(reference) ?? reference.HintPath);
        }
        ImGui.EndDragDropTarget();
    }

    private void DrawUnsavedChangesPrompt()
    {
        if (!_document.HasPendingSwitch) return;
        ImGui.OpenPopup("Unsaved timeline changes");
        var open = true;
        if (!Gui.BeginPopupModal("Unsaved timeline changes", ref open, ImGuiWindowFlags.AlwaysAutoResize)) return;
        ImGui.TextWrapped("Save the current timeline before switching or closing?");
        if (_document.Error != null) ImGui.TextWrapped(_document.Error);
        foreach (var choice in new[] { TimelineSwitchChoice.Save, TimelineSwitchChoice.Discard, TimelineSwitchChoice.Cancel })
        {
            if (choice != TimelineSwitchChoice.Save) ImGui.SameLine();
            if (!Gui.Button(choice.ToString())) continue;
            var savedDirector = _director;
            var savedPath = _document.Path;
            var resolved = _document.Resolve(choice);
            if (resolved && choice == TimelineSwitchChoice.Save && Core.IsEditMode && IsCurrentSceneEntity(savedDirector?.Entity))
                savedDirector.SetAsset(TimelineAssetIO.Load(savedPath));
            _asset = _document.Asset;
            if (resolved || choice == TimelineSwitchChoice.Cancel) ImGui.CloseCurrentPopup();
        }
        Gui.EndPopup();
    }

    private void EvaluatePreview()
    {
        if (_asset == null || !IsCurrentSceneEntity(_director?.Entity) || !Core.IsEditMode) { DisposePreview(); return; }
        _preview ??= new TimelinePreviewSession(_director, _asset);
        _preview.Evaluate(_scrub);
    }

    internal bool PrepareSceneSwitch(Action continueSwitch)
    {
        DisposePreview();
        if (!_document.IsDirty)
        {
            _document.Request(null, null);
            _asset = null; _director = null; _standalone = false; _locked = false;
            return true;
        }
        IsOpen = true;
        _imgui.ShowTimelineWindow = true;
        _document.RequestTransition(() =>
        {
            _document.Request(null, null);
            _asset = null; _director = null; _standalone = false; _locked = false;
            continueSwitch();
        });
        return false;
    }

    internal object EditorState() => new { path = _document.Path, dirty = _document.IsDirty, pending = _document.HasPendingSwitch,
        director = IsCurrentSceneEntity(_director?.Entity) ? _director.Entity.PersistentId.ToString() : null,
        standalone = _standalone, locked = _locked, time = _scrub, playing = _previewPlaying, previewActive = HasActivePreview,
        duration = _asset?.Duration, error = _document.Error, unsupported = _preview?.UnsupportedEffects.ToArray() ?? Array.Empty<string>() };
}
