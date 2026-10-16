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
    [TestCase("Project root"), TestCase("ProjectRootGap:Data")]
    public void Root_folder_drops_preserve_defaults_current_scene_and_asset_guids(string target)
    {
        Call("ui.resize", new { width = 1600, height = 1200 });
        Call("ui.click", new { path = "View/Interface scale/75%" });
        Zoom(false);
        var browser = Call("ui.windows").EnumerateArray().Single(w => w.GetProperty("name").GetString() == "Asset Browser");
        Call("input.drag", new { x1 = 800, y1 = browser.GetProperty("y").GetInt32() - 3, x2 = 800, y2 = 550, frames = 20 });
        var project = Path.GetDirectoryName(ProjectFile);
        Call("scene.create", new { name = "RootDragScene" });
        Call("entity.create", new { name = "RootDragActor" });
        var prefab = Call("prefab.create", new { entity = "RootDragActor", keepOriginal = false });
        var prefabGuid = prefab.GetProperty("guid").GetString();
        var actorGuid = prefab.GetProperty("instance").GetProperty("guid").GetString();
        Call("asset.refresh");
        var sceneMeta = File.ReadAllText(Path.Combine(project, "Scenes/RootDragScene.vscene.meta"));
        Directory.CreateDirectory(Path.Combine(project, "Data"));
        File.Copy(Path.Combine(project, "Scenes/MainScene.vscene"), Path.Combine(project, "Data/RootDragMarker.vscene"), true);
        Call("scene.save");
        Call("asset.refresh");
        try
        {
            Search("RootDrag");
            foreach (var folder in new[] { "Scenes", "Prefabs" })
            {
                DragFolder(folder, "Data");
                WaitForFolder(Path.Combine(project, "Data", folder));
                if (Call("ui.find", new { label = folder, window = "Asset Browser", exact = true }).GetArrayLength() == 0)
                {
                    var data = WaitForWidget("Data", "Asset Browser");
                    Call("input.click", new { x = data.GetProperty("x").GetInt32() + 18, y = data.GetProperty("y").GetInt32() + data.GetProperty("height").GetInt32() / 2 });
                }
                DragFolder(folder, target);
                WaitForFolder(Path.Combine(project, folder));
            }
            using var metadata = JsonDocument.Parse(File.ReadAllText(ProjectFile));
            Assert.That(metadata.RootElement.GetProperty("ScenesFolder").GetString(), Is.EqualTo("Scenes"));
            Assert.That(metadata.RootElement.GetProperty("PrefabsFolder").GetString(), Is.EqualTo("Prefabs"));
            Assert.That(File.ReadAllText(Path.Combine(project, "Scenes/RootDragScene.vscene.meta")), Is.EqualTo(sceneMeta));
            Assert.That(Call("prefab.list").EnumerateArray().Any(p => p.GetProperty("guid").GetString() == prefabGuid && p.GetProperty("path").GetString().Replace('\\', '/').Contains("/Prefabs/")), Is.True);
            Assert.That(Call("status").GetProperty("scene").GetProperty("path").GetString().Replace('\\', '/'), Does.EndWith("/Scenes/RootDragScene.vscene"));
            Call("scene.save");
            Call("scene.reload");
            Assert.That(Call("prefab.info", new { entity = actorGuid }).GetProperty("guid").GetString(), Is.EqualTo(prefabGuid));
            Assert.That(Directory.Exists(Path.Combine(project, "Data/Scenes")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(project, "Data/Prefabs")), Is.False);
        }
        finally
        {
            Call("input.release");
            Search("");
            Call("scene.load", new { name = "TestScene", force = true });
            foreach (var file in new[] { "Scenes/RootDragScene.vscene", "Prefabs/RootDragActor.vprefab", "Data/RootDragMarker.vscene" })
            {
                File.Delete(Path.Combine(project, file));
                File.Delete(Path.Combine(project, file + ".meta"));
            }
            Call("asset.refresh");
            Call("ui.click", new { path = "View/Interface scale/100%" });
            Call("ui.resize", new { width = 1600, height = 900 });
        }
    }

    private static void DragFolder(string folder, string destination)
    {
        var source = WaitForWidget(folder, "Asset Browser");
        var target = WaitForWidget(destination, "Asset Browser");
        Call("input.drag", new { x1 = source.GetProperty("x").GetInt32() + 45, y1 = source.GetProperty("y").GetInt32() + source.GetProperty("height").GetInt32() / 2,
            x2 = target.GetProperty("x").GetInt32() + 45, y2 = target.GetProperty("y").GetInt32() + target.GetProperty("height").GetInt32() / 2, frames = 15 });
    }

    private static void WaitForFolder(string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!Directory.Exists(path) && DateTime.UtcNow < deadline) Thread.Sleep(50);
        Assert.That(Directory.Exists(path), Is.True, "Folder was not relocated: " + path);
        Call("asset.refresh");
        Call("input.wait", new { frames = 3 });
    }

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
            WaitForWidget("Overall UI scale", "Editor Settings");
            WaitForWidget("Text size", "Editor Settings");
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
