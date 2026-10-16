using System;
using System.IO;
using System.Text;
using ImGuiNET;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.Tools;
using Num = System.Numerics;

namespace Voltage.Editor.Assets;

internal sealed class AssetTextPreview
{
    private const int CharacterLimit = 256 * 1024;
    private string _path, _text;
    private Num.Vector2 _anchor;
    private int _openedFrame;
    private bool _place;
    private bool _showHint;

    internal static bool CanPreview(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".cs" or ".fx" or ".txt" or ".md" or ".xml" or ".csv" or ".yaml" or ".yml"
        or ".ini" or ".toml" or ".log";

    internal static string Read(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, true);
        var buffer = new char[CharacterLimit];
        var count = reader.ReadBlock(buffer, 0, buffer.Length);
        var text = new string(buffer, 0, count);
        if (text.Contains('\0')) return "This file contains binary data and cannot be previewed as text.";
        return text.Replace("\t", "    ") + (reader.Peek() >= 0 ? "\n\n[Preview truncated at 256 KiB]" : "");
    }

    public void Open(string path, Num.Vector2 anchor)
    {
        Close();
        if (!EditorSettingsWindow.AssetTextPreviews || !CanPreview(path)) return;
        try { _text = Read(path); }
        catch (Exception ex) { _text = "Cannot read this file: " + ex.Message; }
        _path = path;
        _anchor = anchor;
        _openedFrame = ImGui.GetFrameCount();
        _place = true;
        _showHint = !EditorSettingsWindow.PreviewHintSeen;
        EditorSettingsWindow.PreviewHintSeen = true;
    }

    public void Close() { _path = null; _text = null; }

    public void Draw()
    {
        if (!EditorSettingsWindow.AssetTextPreviews) Close();
        if (_path == null) return;
        var viewport = ImGui.GetMainViewport();
        if (_place)
        {
            var scale = Core.GetGlobalManager<ImGuiManager>()?.FontSizeMultiplier ?? 1;
            var size = Num.Vector2.Min(new Num.Vector2(560, 300) * scale, viewport.WorkSize - new Num.Vector2(16));
            var position = Num.Vector2.Clamp(_anchor - new Num.Vector2(0, size.Y + 8),
                viewport.WorkPos, viewport.WorkPos + viewport.WorkSize - size);
            ImGui.SetNextWindowPos(position);
            ImGui.SetNextWindowSize(size);
            ImGui.SetNextWindowFocus();
            _place = false;
        }
        var open = true;
        if (Gui.Begin(Path.GetFileName(_path) + " - Preview###AssetTextPreview", ref open,
            ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoCollapse))
        {
            ImGui.TextDisabled("Read only  |  Esc or click outside to close");
            if (_showHint)
                ImGui.TextWrapped("Turn previews off below, or change them in Editor Settings > Interface.");
            ImGui.PushFont(Core.GetGlobalManager<ImGuiManager>().LogFont);
            if (Gui.SmallButton("Don't show this again"))
            {
                EditorSettingsWindow.AssetTextPreviews = false;
                open = false;
            }
            ImGui.PopFont();
            ImGui.Separator();
            var manager = Core.GetGlobalManager<ImGuiManager>();
            ImGui.PushFont(manager.LogFont);
            ImGui.BeginChild("Preview text", Num.Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);
            ImGui.TextUnformatted(_text);
            ImGui.EndChild();
            ImGui.PopFont();
        }
        // A just-selected asset can still own ImGui's active id on the first click in this window.
        // Keep internal presses alive through release so small buttons and scrollbars can activate.
        var mouse = ImGui.GetIO().MousePos;
        var origin = ImGui.GetWindowPos();
        var end = origin + ImGui.GetWindowSize();
        var inside = mouse.X >= origin.X && mouse.Y >= origin.Y && mouse.X < end.X && mouse.Y < end.Y;
        Gui.End();
        if (!open || ImGui.IsKeyPressed(ImGuiKey.Escape)
            || (ImGui.GetFrameCount() > _openedFrame && !inside
                && (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseClicked(ImGuiMouseButton.Right)))) Close();
    }
}
