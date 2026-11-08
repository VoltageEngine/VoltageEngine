using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Voltage.Editor.Assets;
using Voltage.Editor.FilePickers;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.Lighting;
using Voltage.Editor.Utils;
using Num = System.Numerics;

namespace Voltage.Editor.Windows;

/// <summary>Lights any Aseprite sprite through the real deferred renderer, with any light type the engine or game defines.</summary>
public static class LightPreviewWindow
{
    public static bool IsOpen;

    private const string DefaultMaterial = "Default";
    private static LightPreviewSession _session;
    private static AssetFileBrowser _browser;
    private static PreviewLight _selected, _dragged;
    private static IntPtr _textureId;
    private static Microsoft.Xna.Framework.Graphics.RenderTarget2D _boundTarget;
    private static bool _playing;
    private static float _elapsed, _checkTimer;
    private static int _zoom = 4;
    private static int _lightTypeIndex;

    internal static LightPreviewSession Session => _session ??= new LightPreviewSession();

    public static void Draw()
    {
        if (!IsOpen)
        {
            Release();
            return;
        }
        EditorLayout.WindowSize(new Num.Vector2(980, 640));
        if (Gui.Begin("Light Preview", ref IsOpen))
        {
            var session = Session;
            Tick(session);
            if (ImGui.BeginTable("##LightPreview", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableSetupColumn("Settings", ImGuiTableColumnFlags.WidthFixed, 320 * EditorLayout.Scale);
                ImGui.TableSetupColumn("View", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableNextColumn();
                if (ImGui.BeginChild("##Settings")) DrawSettings(session);
                ImGui.EndChild();
                ImGui.TableNextColumn();
                DrawView(session);
                ImGui.EndTable();
            }
        }
        Gui.End();
    }

    private static void Tick(LightPreviewSession session)
    {
        float delta = Voltage.Utils.Time.UnscaledDeltaTime;
        _checkTimer += delta;
        if (_checkTimer >= 0.5f)
        {
            _checkTimer = 0;
            session.ReloadIfChanged();
        }
        if (_playing && session.FrameCount > 0)
        {
            _elapsed += delta;
            float step = 1f / Math.Max(1f, session.FrameRate);
            if (_elapsed >= step)
            {
                _elapsed -= step;
                session.Frame = (session.Frame + 1) % session.FrameCount;
            }
        }
        session.Render();
    }

    private static void DrawSettings(LightPreviewSession session)
    {
        ImGui.TextUnformatted("Sprite");
        DrawFilePicker(session);
        if (session.Error != null) ImGui.TextColored(new Num.Vector4(1, 0.5f, 0.4f, 1), session.Error);
        if (session.Path == null) return;

        bool changed = false;
        changed |= LayerList("Diffuse layers", session.Layers, session.Diffuse);
        changed |= LayerList("Normals layers", session.NormalChoices, session.Normals);
        changed |= LayerList("Glow layers", session.GlowChoices, session.Glow);
        if (changed) session.Rebuild();

        ImGui.Separator();
        ImGui.SetNextItemWidth(-1);
        if (Gui.BeginCombo("##Tag", session.Tag))
        {
            foreach (var tag in session.Tags)
                if (Gui.Selectable(tag, tag == session.Tag)) { session.Tag = tag; session.Frame = 0; }
            ImGui.EndCombo();
        }
        int frame = session.Frame;
        ImGui.SetNextItemWidth(-90 * EditorLayout.Scale);
        if (ImGui.SliderInt("##Frame", ref frame, 0, Math.Max(0, session.FrameCount - 1))) session.Frame = frame;
        ImGui.SameLine();
        if (Gui.Button(_playing ? "Pause" : "Play")) _playing = !_playing;

        ImGui.Separator();
        var ambient = session.Ambient.ToVector3().ToNumerics();
        if (ImGui.ColorEdit3("Ambient", ref ambient)) session.Ambient = new Color(ambient.ToXNA());
        var background = session.Background.ToVector3().ToNumerics();
        if (ImGui.ColorEdit3("Background", ref background)) session.Background = new Color(background.ToXNA());
        DrawMaterialPicker(session);

        ImGui.Separator();
        DrawLights(session);
    }

    private static void DrawFilePicker(LightPreviewSession session)
    {
        var name = session.Path != null ? System.IO.Path.GetFileName(session.Path) : "Drop an Aseprite file here";
        ImGui.Button(name, new Num.Vector2(-70 * EditorLayout.Scale, 0));
        if (ImGui.BeginDragDropTarget())
        {
            var dragged = AssetBrowserWindow.DraggedReference;
            var payload = !dragged.IsEmpty ? ImGui.AcceptDragDropPayload(AssetBrowserWindow.DragDropPayloadId) : default;
            bool accepted;
            unsafe { accepted = payload.NativePtr != null; }
            if (accepted)
            {
                var path = AssetDatabase.Instance?.Resolve(dragged);
                AssetBrowserWindow.DraggedReference = AssetReference.Empty;
                if (path != null && (path.EndsWith(".aseprite", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".ase", StringComparison.OrdinalIgnoreCase)))
                    session.Open(path);
            }
            ImGui.EndDragDropTarget();
        }
        ImGui.SameLine();
        _browser ??= new AssetFileBrowser("LightPreview-browser", new[] { ".aseprite", ".ase" }, "Aseprite");
        if (Gui.Button("Pick")) _browser.Open("Select Aseprite File", session.Path, typeof(LightPreviewWindow));
        _browser.Draw("Select Aseprite File");
        if (_browser.TryTakeResult(out var file)) session.Open(file);
    }

    private static bool LayerList(string title, List<string> names, HashSet<string> picked)
    {
        if (names.Count == 0) return false;
        bool changed = false;
        if (ImGui.TreeNodeEx(title, ImGuiTreeNodeFlags.DefaultOpen))
        {
            foreach (var name in names)
            {
                bool on = picked.Contains(name);
                if (Gui.Checkbox(name, ref on))
                {
                    if (on) picked.Add(name); else picked.Remove(name);
                    changed = true;
                }
            }
            ImGui.TreePop();
        }
        return changed;
    }

    private static void DrawMaterialPicker(LightPreviewSession session)
    {
        var materials = LightPreviewSession.Materials();
        if (materials.Count == 0) return;
        if (Gui.BeginCombo("Material", session.Material?.Name ?? DefaultMaterial))
        {
            if (Gui.Selectable(DefaultMaterial, session.Material == null)) { session.Material = null; session.Rebuild(); }
            foreach (var material in materials)
                if (Gui.Selectable(material.Name, session.Material?.Name == material.Name)) { session.Material = material; session.Rebuild(); }
            ImGui.EndCombo();
        }
    }

    private static void DrawLights(LightPreviewSession session)
    {
        ImGui.TextUnformatted("Lights");
        var types = LightPreviewSession.LightTypes();
        _lightTypeIndex = Math.Clamp(_lightTypeIndex, 0, Math.Max(0, types.Count - 1));
        ImGui.SetNextItemWidth(-60 * EditorLayout.Scale);
        if (types.Count > 0 && Gui.BeginCombo("##LightType", types[_lightTypeIndex].Name))
        {
            for (int i = 0; i < types.Count; i++)
                if (Gui.Selectable(types[i].Name, i == _lightTypeIndex)) _lightTypeIndex = i;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (Gui.Button("Add") && types.Count > 0) _selected = session.AddLight(types[_lightTypeIndex].Type);

        if (_selected != null && !session.Lights.Contains(_selected)) _selected = null;
        PreviewLight removed = null;
        for (int i = 0; i < session.Lights.Count; i++)
        {
            var light = session.Lights[i];
            ImGui.PushID(i);
            if (Gui.Selectable(light.Name, light == _selected, ImGuiSelectableFlags.None, new Num.Vector2(-60 * EditorLayout.Scale, 0))) _selected = light;
            ImGui.SameLine();
            if (Gui.Button("Remove")) removed = light;
            ImGui.PopID();
        }
        if (removed != null)
        {
            session.RemoveLight(removed);
            if (_selected == removed) _selected = null;
        }
        if (_selected == null) return;
        var position = _selected.Entity.Position.ToNumerics();
        if (ImGui.DragFloat2("Position", ref position)) _selected.Entity.Position = position.ToXNA();
        foreach (var inspector in _selected.Inspectors) inspector.Draw();
    }

    private static void DrawView(LightPreviewSession session)
    {
        var target = session.Target;
        ImGui.SetNextItemWidth(120 * EditorLayout.Scale);
        ImGui.SliderInt("Zoom", ref _zoom, 1, 16);
        if (target == null) return;
        var manager = Core.GetGlobalManager<ImGuiManager>();
        if (_boundTarget != target)
        {
            if (_textureId != IntPtr.Zero) manager.UnbindTexture(_textureId);
            _textureId = manager.BindTexture(target);
            _boundTarget = target;
        }
        if (ImGui.BeginChild("##View", Num.Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
        {
            var size = new Num.Vector2(target.Width, target.Height) * _zoom;
            var origin = ImGui.GetCursorScreenPos();
            ImGui.Image(_textureId, size);
            var center = origin + size / 2;
            var draw = ImGui.GetWindowDrawList();
            foreach (var light in session.Lights)
            {
                var at = center + light.Entity.Position.ToNumerics() * _zoom;
                draw.AddCircle(at, 7, ImGui.GetColorU32(light == _selected ? new Num.Vector4(1, 0.7f, 0.2f, 1) : new Num.Vector4(1, 1, 1, 0.8f)), 16, 2);
            }
            var mouse = ImGui.GetMousePos();
            if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _dragged = session.Lights.OrderBy(l => Num.Vector2.DistanceSquared(center + l.Entity.Position.ToNumerics() * _zoom, mouse)).FirstOrDefault();
                if (_dragged != null && Num.Vector2.Distance(center + _dragged.Entity.Position.ToNumerics() * _zoom, mouse) > 14) _dragged = null;
                if (_dragged != null) _selected = _dragged;
            }
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) _dragged = null;
            if (_dragged != null) _dragged.Entity.Position = ((mouse - center) / _zoom).ToXNA();
        }
        ImGui.EndChild();
    }

    private static void Release()
    {
        if (_session == null) return;
        if (_textureId != IntPtr.Zero) Core.GetGlobalManager<ImGuiManager>()?.UnbindTexture(_textureId);
        _textureId = IntPtr.Zero;
        _boundTarget = null;
        _session.Dispose();
        _session = null;
        _selected = _dragged = null;
        _playing = false;
    }
}
