using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class AssetBrowserUiTests
{
    private static void Search(string text)
    {
        Call("ui.focus", new { window = "Asset Browser" });
        Call("ui.click", new { label = "AssetSearch", window = "Asset Browser" });
        Call("input.key", new { key = "a", modifiers = new[] { "ctrl" } });
        if (text.Length == 0) Call("input.key", new { key = "backspace" });
        else Call("input.type", new { text });
        Thread.Sleep(150);
    }

    private static void Zoom(bool thumbnails)
    {
        var slider = WaitForWidget("Zoom Level", "Asset Browser");
        Call("input.click", new { x = slider.GetProperty("x").GetInt32() + (thumbnails ? slider.GetProperty("width").GetInt32() - 8 : 8),
            y = slider.GetProperty("y").GetInt32() + slider.GetProperty("height").GetInt32() / 2 });
        Thread.Sleep(150);
    }

    [TestCase(1280, 800, 75), TestCase(1600, 900, 100), TestCase(1920, 1080, 150)]
    public void Playback_controls_follow_the_game_view_center_at_every_scale(int width, int height, int scale)
    {
        try
        {
            Call("ui.resize", new { width, height });
            Call("ui.click", new { path = $"View/Interface scale/{scale}%" });
            Thread.Sleep(150);
            var game = Call("ui.windows").EnumerateArray().Single(w => w.GetProperty("id").GetString() == "GameWindow");
            var play = WaitForWidget("EditorPlay", "Game");
            var reset = WaitForWidget("EditorReset", "Game");
            var center = (play.GetProperty("x").GetInt32() + reset.GetProperty("x").GetInt32() + reset.GetProperty("width").GetInt32()) / 2f;
            Assert.That(center, Is.EqualTo(game.GetProperty("x").GetInt32() + game.GetProperty("width").GetInt32() / 2f).Within(2));
            Assert.That(Call("ui.find", new { label = "EditorPause", window = "Game", exact = true }).GetArrayLength(), Is.EqualTo(1));
            Assert.That(Call("ui.find", new { label = "EditorPlay", window = "Editor Tools", exact = true }).GetArrayLength(), Is.Zero);
        }
        finally
        {
            Call("ui.click", new { path = "View/Interface scale/100%" });
            Call("ui.resize", new { width = 1600, height = 900 });
            Call("input.release");
        }
    }

    [Test]
    public void Asset_zoom_switches_spaced_rows_to_preview_tiles_and_retains_drag_selection()
    {
        Call("ui.click", new { path = "View/Interface scale/75%" });
        var content = Path.Combine(Path.GetDirectoryName(ProjectFile), "Content");
        var icon = Path.Combine(Path.GetDirectoryName(EditorExe), "DefaultContent", "UI", "AssetTypes", "Texture.png");
        File.Copy(icon, Path.Combine(content, "PreviewIconA.png"), true);
        File.Copy(icon, Path.Combine(content, "PreviewIconB.png"), true);
        Call("asset.refresh");
        try
        {
            Search("PreviewIcon");
            Zoom(false);
            var a = WaitForWidget("PreviewIconA.png", "Asset Browser");
            var b = WaitForWidget("PreviewIconB.png", "Asset Browser");
            // ImGui includes the between-row hit spacing in Selectable's registered bounds.
            Assert.That(b.GetProperty("y").GetInt32() - a.GetProperty("y").GetInt32(), Is.GreaterThanOrEqualTo(a.GetProperty("height").GetInt32()));
            Assert.That(a.GetProperty("height").GetInt32(), Is.GreaterThan(30));
            var rowHeight = a.GetProperty("height").GetInt32();
            Zoom(true);
            a = WaitForWidget("PreviewIconA.png", "Asset Browser");
            Assert.That(a.GetProperty("height").GetInt32(), Is.GreaterThan(rowHeight + 15));
            Call("input.click", new { x = a.GetProperty("x").GetInt32() + 12,
                y = a.GetProperty("y").GetInt32() + 12, button = "right" });
            Thread.Sleep(100);
            Assert.That(Call("ui.find", new { label = "Copy", kind = "menuItem", exact = true }).GetArrayLength(), Is.EqualTo(1));
            Call("input.key", new { key = "esc" });
        }
        finally { Zoom(false); Search(""); Call("ui.click", new { path = "View/Interface scale/100%" }); Call("input.release"); }
    }

    [Test]
    public void Readable_asset_preview_keeps_internal_clicks_and_closes_on_outside_click_or_escape()
    {
        Call("ui.click", new { path = "View/Interface scale/75%" });
        var content = Path.Combine(Path.GetDirectoryName(ProjectFile), "Content");
        File.WriteAllText(Path.Combine(content, "PreviewReadme.txt"), string.Join("\n", Enumerable.Range(0, 100).Select(i => i + " " + new string('x', 200))));
        Call("asset.refresh");
        try
        {
            Search("PreviewReadme");
            Call("ui.click", new { label = "PreviewReadme.txt", window = "Asset Browser", exact = true });
            Thread.Sleep(100);
            JsonElement Preview() => Call("ui.windows").EnumerateArray().Single(w => w.GetProperty("id").GetString() == "AssetTextPreview");
            var preview = Preview();
            Assert.That(preview.GetProperty("docked").GetBoolean(), Is.False);
            var x = preview.GetProperty("x").GetInt32() + 100;
            var y = preview.GetProperty("y").GetInt32() + 100;
            Call("input.click", new { x, y });
            Call("input.scroll", new { x, y, notches = -5 });
            Assert.That(Preview().ValueKind, Is.EqualTo(JsonValueKind.Object));
            // The floating preview can cover the search bar. Click outside its actual rectangle.
            Call("ui.click", new { label = "Normal", window = "Editor Tools", exact = true });
            Thread.Sleep(150);
            Assert.That(Call("ui.windows").EnumerateArray().Any(w => w.GetProperty("id").GetString() == "AssetTextPreview"), Is.False);
            Call("ui.click", new { label = "PreviewReadme.txt", window = "Asset Browser", exact = true });
            Call("input.key", new { key = "esc" });
            Thread.Sleep(150);
            Assert.That(Call("ui.windows").EnumerateArray().Any(w => w.GetProperty("id").GetString() == "AssetTextPreview"), Is.False);
        }
        finally { Search(""); Call("ui.click", new { path = "View/Interface scale/100%" }); Call("input.release"); }
    }

    [Test]
    public void Structured_assets_stay_closed_and_preview_preference_can_be_changed_from_both_places()
    {
        Call("ui.click", new { path = "View/Interface scale/75%" });
        var content = Path.Combine(Path.GetDirectoryName(ProjectFile), "Content");
        File.WriteAllText(Path.Combine(content, "PreviewSettings.json"), "{\"data\":{}}");
        File.WriteAllText(Path.Combine(content, "PreviewSettings.txt"), "A readable note");
        Call("asset.refresh");
        bool PreviewOpen() => Call("ui.windows").EnumerateArray().Any(w => w.GetProperty("id").GetString() == "AssetTextPreview");
        try
        {
            Search("PreviewSettings"); Zoom(false);
            Call("ui.click", new { label = "PreviewSettings.json", window = "Asset Browser", exact = true });
            Thread.Sleep(100);
            Assert.That(PreviewOpen(), Is.False);
            Call("ui.click", new { label = "PreviewSettings.txt", window = "Asset Browser", exact = true });
            Thread.Sleep(150);
            Call("ui.click", new { label = "Don't show this again", exact = true });
            Thread.Sleep(100);
            Assert.That(PreviewOpen(), Is.False);
            Call("ui.click", new { label = "PreviewSettings.txt", window = "Asset Browser", exact = true });
            Assert.That(PreviewOpen(), Is.False);
            Call("ui.click", new { path = "File/Settings" });
            Assert.That(Call("ui.find", new { label = "Overall UI scale", window = "Editor Settings", exact = true }).GetArrayLength(), Is.EqualTo(1));
            Assert.That(Call("ui.find", new { label = "Text size", window = "Editor Settings", exact = true }).GetArrayLength(), Is.EqualTo(1));
            Call("ui.click", new { label = "Preview scripts and text on selection", window = "Editor Settings", exact = true });
        }
        finally
        {
            var settings = Call("ui.windows").EnumerateArray().FirstOrDefault(w => w.GetProperty("name").GetString() == "Editor Settings");
            if (settings.ValueKind == JsonValueKind.Object)
                Call("input.click", new { x = settings.GetProperty("x").GetInt32() + settings.GetProperty("width").GetInt32() - 16,
                    y = settings.GetProperty("y").GetInt32() + 15 });
            Search(""); Call("ui.click", new { path = "View/Interface scale/100%" }); Call("input.release");
        }
    }

    [TestCase("LeftControl"), TestCase("LeftWindows")]
    public void Modifier_scroll_zooms_assets_without_scrolling_the_file_list(string key)
    {
        Call("ui.click", new { path = "View/Interface scale/75%" });
        try
        {
            var content = Path.Combine(Path.GetDirectoryName(ProjectFile), "Content");
            File.Copy(Path.Combine(Path.GetDirectoryName(EditorExe), "DefaultContent", "UI", "AssetTypes", "Texture.png"),
                Path.Combine(content, "PreviewIconA.png"), true);
            Call("asset.refresh");
            Search("PreviewIcon"); Zoom(false);
            var row = WaitForWidget("PreviewIconA.png", "Asset Browser");
            var x = row.GetProperty("x").GetInt32() + 15;
            var y = row.GetProperty("y").GetInt32() + 15;
            Call("input.key", new { key, action = "down" });
            Call("input.scroll", new { x, y, notches = 5 });
            Thread.Sleep(150);
            var tile = WaitForWidget("PreviewIconA.png", "Asset Browser");
            Assert.That(tile.GetProperty("width").GetInt32(), Is.LessThan(row.GetProperty("width").GetInt32()));
            Call("input.scroll", new { x, y, notches = -30 });
            Thread.Sleep(150);
            Assert.That(WaitForWidget("PreviewIconA.png", "Asset Browser").GetProperty("height").GetInt32(), Is.EqualTo(row.GetProperty("height").GetInt32()));
        }
        finally { Call("input.release"); Zoom(false); Search(""); Call("ui.click", new { path = "View/Interface scale/100%" }); }
    }
}
