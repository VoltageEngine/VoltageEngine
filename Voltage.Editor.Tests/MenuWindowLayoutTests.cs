using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class MenuWindowLayoutTests
{
    [Test]
    public void Overflow_menus_keep_the_original_gateway_paths_working()
    {
        Configure(1920, 1080, 200);
        try
        {
            Call("ui.click", new { path = "Build/Build Game" });
            WaitForWidget("Cancel", "Build Game");
            Click("Cancel", "Build Game");
            Call("ui.click", new { path = "View/UI font/Classic (ProggyClean)" });
        }
        catch (Exception error)
        {
            TestContext.Progress.WriteLine(error.ToString());
            Call("screenshot", new { path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "menu-overflow-failure.png") });
            throw;
        }
        finally { Restore(); }
    }

    [TestCase(1280, 800, 75), TestCase(1600, 900, 100), TestCase(1280, 800, 150)]
    public void Project_settings_keep_values_sections_and_footer_accessible(int width, int height, int scale)
    {
        Configure(width, height, scale);
        OpenMenu("Project/Settings");
        try
        {
            foreach (var section in new[] { "Project Info", "Display & Audio", "Physics Layers", "Rendering Layers", "Entity Tags" })
            {
                var selector = Call("ui.find", new { label = "ProjectSettingsSection", window = "Project Settings", exact = true });
                if (selector.GetArrayLength() > 0) Click("ProjectSettingsSection", "Project Settings");
                Click(section, "Project Settings");
                AssertControlsFit("Project Settings");
                foreach (var caption in new[] { "Save", "Apply", "Close" })
                {
                    var button = WaitForWidget(caption, "Project Settings");
                    Assert.That(button.GetProperty("y").GetInt32() + button.GetProperty("height").GetInt32(), Is.LessThanOrEqualTo(height));
                }
                if (section == "Display & Audio")
                {
                    var field = WaitForWidget("Width", "Project Settings");
                    var save = WaitForWidget("Save", "Project Settings");
                    Assert.That(field.GetProperty("width").GetInt32(), Is.GreaterThan(save.GetProperty("height").GetInt32() * 3));
                    Scroll("Project Settings", -8);
                    AssertControlsFit("Project Settings");
                }
            }
        }
        finally { Click("Close", "Project Settings"); Restore(); }
    }

    [TestCase(1280, 800, 75), TestCase(1600, 900, 100), TestCase(1280, 800, 150)]
    public void Menu_windows_and_docked_panels_keep_controls_inside_their_content(int width, int height, int scale)
    {
        Configure(width, height, scale);
        var panels = new[] {
            ("CoreWindow", "Core Settings"), ("MainInspectorWindow", "Main Entity Inspector"),
            ("SceneGraphWindow", "Scene Graph"), ("AssetBrowser", "Asset Browser"),
            ("AnimationEventInspector", "Animation Event Inspector"), ("TimelineWindow", "Timeline"),
            ("TilePaletteWindow", "Tile Palette"), ("AudioProfilerWindow", "Audio Profiler"),
            ("PerformanceProfilerWindow", "Performance Profiler") };
        try
        {
            foreach (var (toggle, window) in panels)
            {
                Call("window.show", new { name = toggle, visible = true });
                Call("input.wait", new { frames = 3 });
                Call("ui.focus", new { window });
                AssertControlsFit(window);
                Scroll(window, -4);
                AssertControlsFit(window);
                Call("window.show", new { name = toggle, visible = false });
            }
            foreach (var (path, window) in new[] {
                ("View/Window/Aseprite", "Aseprite"), ("View/Window/Tileset Editor", "Tileset Editor"),
                ("File/Settings", "Editor Settings"), ("Scripting/Scripting Window", "Scripting"),
                ("Build/Asset Build/Settings", "Asset Build Settings") })
            {
                if (Call("ui.windows").EnumerateArray().Any(w => w.GetProperty("name").GetString().StartsWith(window, StringComparison.Ordinal)))
                    Call("ui.focus", new { window });
                else
                {
                    try { OpenMenu(path); }
                    catch
                    {
                        Call("screenshot", new { path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "window-menu-failure.png") });
                        throw;
                    }
                }
                AssertControlsFit(window);
                Scroll(window, -4);
                AssertControlsFit(window);
            }
            // All root menus remain reachable even when their total width exceeds the viewport.
            AssertControlsFit("MainMenuBar");
        }
        finally { Restore(); }
    }

    [Test]
    public void Large_editor_text_and_hotkeys_reflow_instead_of_covering_controls()
    {
        Configure(1280, 800, 100);
        OpenMenu("File/Settings");
        try
        {
            var text = WaitForWidget("Text size", "Editor Settings");
            Call("input.click", new { x = text.GetProperty("x").GetInt32() + text.GetProperty("width").GetInt32() - 4,
                y = text.GetProperty("y").GetInt32() + text.GetProperty("height").GetInt32() / 2 });
            Call("input.wait", new { frames = 4 });
            AssertControlsFit("Editor Settings");
            ScrollUntilVisible("Custom Hotkeys", "Editor Settings");
            Click("Custom Hotkeys", "Editor Settings");
            AssertControlsFit("Editor Settings");
            Scroll("Editor Settings", -8);
            AssertControlsFit("Editor Settings");
        }
        finally
        {
            Scroll("Editor Settings", 40);
            ScrollUntilVisible("Reset interface settings", "Editor Settings");
            Click("Reset interface settings", "Editor Settings");
            Restore();
        }
    }

    [TestCase(75), TestCase(150)]
    public void Creation_dialogs_and_plugin_tools_reflow_at_both_scales(int scale)
    {
        Configure(1280, 800, scale);
        try
        {
            foreach (var (path, window) in new[] {
                ("File/New Project", "create-project-popup"), ("Project/New Scene", "create-scene-popup") })
            {
                OpenMenu(path);
                AssertControlsFit(window);
                ScrollUntilVisible("Cancel", window);
                AssertControlsFit(window);
                Click("Cancel", window);
            }
            OpenMenu("Plugins/Plugin Manager");
            AssertControlsFit("Plugin Manager");
            Click("Create New Plugin", "Plugin Manager");
            AssertControlsFit("create-plugin");
            ScrollUntilVisible("Cancel", "create-plugin");
            AssertControlsFit("create-plugin");
            Click("Cancel", "create-plugin");
        }
        finally { Restore(); }
    }

    [TestCase(75), TestCase(150)]
    public void Populated_timeline_inspector_and_atlas_keep_their_controls_accessible(int scale)
    {
        Configure(1280, 800, scale);
        var name = "LayoutSequence" + scale;
        Call("window.show", new { name = "TimelineWindow", visible = false });
        Call("timeline.create", new { name });
        Call("entity.create", new { name });
        Call("component.add", new { entity = name, type = "TimelineDirector" });
        Call("component.set", new { entity = name, type = "TimelineDirector", member = "Timeline", value = "Data/Timelines/" + name + ".vtimeline" });
        Call("entity.select", new { entity = name });
        try
        {
            Call("ui.focus", new { window = "Main Entity Inspector" });
            Scroll("Main Entity Inspector", 40);
            AssertControlsFit("Main Entity Inspector");
            Scroll("Main Entity Inspector", -6);
            AssertControlsFit("Main Entity Inspector");
            Call("window.show", new { name = "TimelineWindow", visible = true });
            Call("input.wait", new { frames = 3 });
            Call("ui.focus", new { window = "Timeline" });
            Call("input.wait", new { frames = 3 });
            Scroll("Timeline", 40);
            WaitForWidget("Play", "Timeline");
            AssertControlsFit("Timeline");
            Scroll("Timeline", -8);
            AssertControlsFit("Timeline");

            var content = Path.Combine(Path.GetDirectoryName(ProjectFile), "Content");
            File.Copy(Path.Combine(Path.GetDirectoryName(EditorExe), "DefaultContent", "UI", "AssetTypes", "Texture.png"), Path.Combine(content, "LayoutAtlas.png"), true);
            File.WriteAllText(Path.Combine(content, "LayoutAtlas.atlas"), "Frame\n0,0,16,16\n0.5,0.5\n\nIdle\n8\n0\n");
            OpenMenu("File/Open Sprite Atlas Editor");
            Click("File", "Sprite Atlas Editor");
            Click("Load Atlas or PNG", "Sprite Atlas Editor");
            WaitForWidget("LayoutAtlas.png", "open-file");
            AssertControlsFit("open-file");
            Click("LayoutAtlas.png", "open-file");
            Click("Open", "open-file");
            Call("input.wait", new { frames = 5 });
            AssertControlsFit("Sprite Atlas Editor");
            var atlas = Call("ui.windows").EnumerateArray().Single(w => w.GetProperty("name").GetString() == "Sprite Atlas Editor");
            Call("input.drag", new { x1 = atlas.GetProperty("x").GetInt32() + atlas.GetProperty("width").GetInt32() - 3,
                y1 = atlas.GetProperty("y").GetInt32() + atlas.GetProperty("height").GetInt32() - 3,
                x2 = atlas.GetProperty("x").GetInt32() + 650, y2 = atlas.GetProperty("y").GetInt32() + 600, frames = 12 });
            foreach (var tab in new[] { "Origins", "Animations", "Preview" })
            {
                Click(tab, "Sprite Atlas Editor");
                AssertControlsFit("Sprite Atlas Editor");
            }
            var capture = Call("screenshot", new { path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "atlas-layout-" + scale + ".png") }).GetProperty("path").GetString();
            TestContext.AddTestAttachment(capture, "Atlas layout at " + scale + "%");
        }
        catch (Exception error)
        {
            TestContext.Progress.WriteLine(error.ToString());
            Call("screenshot", new { path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "populated-layout-failure.png") });
            throw;
        }
        finally
        {
            foreach (var popup in Call("ui.popups").EnumerateArray().ToArray())
                if (popup.GetProperty("buttons").EnumerateArray().Any(b => b.GetString() == "Cancel"))
                    Click("Cancel", popup.GetProperty("name").GetString());
            var atlas = Call("ui.windows").EnumerateArray().FirstOrDefault(w => w.GetProperty("name").GetString() == "Sprite Atlas Editor");
            if (atlas.ValueKind != JsonValueKind.Undefined)
            {
                var titleHeight = WaitForWidget("File", "Sprite Atlas Editor").GetProperty("height").GetInt32();
                Call("input.click", new { x = atlas.GetProperty("x").GetInt32() + atlas.GetProperty("width").GetInt32() - titleHeight / 2,
                    y = atlas.GetProperty("y").GetInt32() + titleHeight / 2 });
                Call("input.wait", new { frames = 3 });
            }
            if (Call("ui.windows").EnumerateArray().Any(w => w.GetProperty("name").GetString() == "Timeline"))
            {
                Call("ui.focus", new { window = "Timeline" });
                Call("input.wait", new { frames = 3 });
                Scroll("Timeline", 40);
                Call("window.show", new { name = "TimelineWindow", visible = false });
            }
            Call("entity.delete", new { entity = name });
            Call("scene.load", new { name = "TestScene", force = true });
            Restore();
        }
    }

    private static void Configure(int width, int height, int scale)
    {
        Call("ui.resize", new { width, height });
        OpenMenu($"View/Interface scale/{scale}%");
        Call("input.wait", new { frames = 3 });
    }

    private static void Restore()
    {
        Call("input.key", new { key = "escape" });
        foreach (var toggle in new[] { "CoreWindow", "MainInspectorWindow", "SceneGraphWindow", "AssetBrowser", "SeparateGameWindow" })
            Call("window.show", new { name = toggle, visible = true });
        Call("ui.resize", new { width = 1600, height = 900 });
        OpenMenu("View/Interface scale/100%");
        Call("input.wait", new { frames = 3 });
        var titleHeight = WaitForWidget("Scene Graph Sections", "Scene Graph").GetProperty("height").GetInt32();
        foreach (var name in new[] { "Plugin Manager", "Editor Settings", "Scripting", "Asset Build Settings", "Aseprite", "Tileset Editor", "Sprite Atlas Editor" })
        {
            var window = Call("ui.windows").EnumerateArray().FirstOrDefault(w => w.GetProperty("name").GetString().StartsWith(name, StringComparison.Ordinal));
            if (window.ValueKind == JsonValueKind.Undefined || window.GetProperty("docked").GetBoolean()) continue;
            Call("ui.focus", new { window = name });
            Call("input.wait", new { frames = 2 });
            Call("input.click", new { x = window.GetProperty("x").GetInt32() + window.GetProperty("width").GetInt32() - titleHeight / 2,
                y = window.GetProperty("y").GetInt32() + titleHeight / 2 });
            Call("input.wait", new { frames = 3 });
        }
    }

    private static void Click(string label, string window) => Call("ui.click", new { label, window, exact = true });

    private static void OpenMenu(string path)
    {
        try { Call("ui.click", new { path }); }
        catch (Exception)
        {
            Call("input.key", new { key = "escape" });
            Call("input.move", new { x = 0, y = 0 });
            Call("input.key", new { key = "escape" });
            var levels = path.Split('/');
            Call("ui.click", new { path = levels[0] });
            for (int level = 1; level < levels.Length; level++)
            {
                WaitForWidget(levels[level], null);
                if (level < levels.Length - 1)
                    Call("ui.hover", new { label = levels[level], kind = "menu", exact = true, frames = 12 });
                else Call("ui.click", new { label = levels[level], exact = true });
            }
        }
        Call("input.wait", new { frames = 3 });
    }

    private static void Scroll(string window, int amount)
    {
        var frame = Call("ui.windows").EnumerateArray().First(w => w.GetProperty("name").GetString().StartsWith(window, StringComparison.Ordinal));
        Call("input.scroll", new { x = frame.GetProperty("x").GetInt32() + frame.GetProperty("width").GetInt32() / 2,
            y = frame.GetProperty("y").GetInt32() + frame.GetProperty("height").GetInt32() / 2, notches = amount });
        Thread.Sleep(100);
    }

    private static void ScrollUntilVisible(string label, string window)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (Call("ui.find", new { label, window, exact = true }).GetArrayLength() > 0) return;
            Scroll(window, -3);
        }
        Assert.Fail("Not reachable by scrolling: " + window + "/" + label);
    }

    private static void AssertControlsFit(string window)
    {
        var tree = Call("ui.tree", new { window, limit = 5000 });
        var items = tree.GetProperty("windows").EnumerateArray().SelectMany(w => w.GetProperty("items").EnumerateArray()).ToArray();
        var clipped = items.Where(i => i.TryGetProperty("clippedHorizontally", out var value) && value.ValueKind == JsonValueKind.True &&
            i.GetProperty("label").GetString() != "tlcontent" &&
            i.GetProperty("kind").GetString() is "input" or "drag" or "slider" or "combo" or "color" or "button" or "checkbox" or "menu").ToArray();
        if (clipped.Length > 0)
        {
            var capture = Call("screenshot", new { path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "layout-failure.png") });
            TestContext.Progress.WriteLine("Layout screenshot: " + capture.GetProperty("path").GetString());
        }
        Assert.That(clipped, Is.Empty, window + ": " + string.Join(", ", clipped.Select(i => i.ToString())));
    }
}
