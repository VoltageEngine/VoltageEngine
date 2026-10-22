using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class TileSelectionUiTests
{
    [Test]
    public void Palette_scene_select_activates_tile_cursor_and_drag_moves_a_clicked_cell()
    {
        var content = Path.Combine(Path.GetDirectoryName(ProjectFile), "Content");
        Directory.CreateDirectory(content);
        File.WriteAllBytes(Path.Combine(content, "SelectionTile.png"), Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAG0lEQVR4nGMI2v31PyWYYdSAUQNGDfg/TAwAAEcHAS40R0jqAAAAAElFTkSuQmCC"));
        Thread.Sleep(1000);
        var tileset = Call("tileset.create", new { texture = "SelectionTile.png", name = "SelectionTiles" });
        Assert.That(tileset.GetProperty("tileCount").GetInt32(), Is.EqualTo(1));
        Call("scene.create", new { name = "TileSelectionExample", template = "empty" });
        Call("tilemap.create", new { tileset = "SelectionTiles.vtileset", name = "Example props" });
        Call("tilemap.paint", new { entity = "Example props", cells = new[] { new { x = 0, y = 0, tile = 0 }, new { x = 2, y = 0, tile = 0 } } });
        Call("window.show", new { name = "TilePaletteWindow", visible = true });
        Call("ui.resize", new { width = 1600, height = 1200 });
        Call("ui.click", new { path = "View/Interface scale/75%" });
        Call("input.wait", new { frames = 10 });
        Call("view.set", new { x = 0, y = 0, zoom = 4 });
        WaitForWidget("Scene Select", "Tile Palette");
        Call("ui.click", new { label = "Scene Select", window = "Tile Palette", exact = true });
        Call("input.wait", new { frames = 10 });
        var palette = Call("ui.windows").EnumerateArray().Single(w => w.GetProperty("name").GetString() == "Tile Palette");
        var right = palette.GetProperty("x").GetInt32() + palette.GetProperty("width").GetInt32() - 2;
        var bottom = palette.GetProperty("y").GetInt32() + palette.GetProperty("height").GetInt32() - 2;
        Call("input.drag", new { x1 = right, y1 = bottom, x2 = right, y2 = 1100, frames = 15 });
        WaitForWidget("Pick layer in scene", "Tile Palette");
        Call("view.focus", new { entity = "Example props", zoom = 4 });
        Call("ui.focus", new { window = "Game" });
        Call("input.move", new { x = 700, y = 600 });
        Call("input.wait", new { frames = 5 });
        var facts = Call("ui.info");
        var game = facts.GetProperty("gameView");
        var ratio = game.GetProperty("width").GetSingle() / game.GetProperty("renderTargetWidth").GetSingle();
        var mouse = facts.GetProperty("scaledMouse");
        var targetX = game.GetProperty("renderTargetWidth").GetSingle() / 2 - 64;
        var targetY = game.GetProperty("renderTargetHeight").GetSingle() / 2;
        var x = (int)Math.Round(700 + (targetX - mouse.GetProperty("x").GetSingle()) * ratio);
        var y = (int)Math.Round(600 + (targetY - mouse.GetProperty("y").GetSingle()) * ratio);
        var cellPixels = 64 * ratio;
        TestContext.Progress.WriteLine($"Tile drag at {x},{y}, cell pixels {cellPixels}");
        Call("input.drag", new { x1 = x, y1 = y, x2 = x, y2 = (int)(y + cellPixels * 2), frames = 15 });
        Assert.That(Call("tilemap.get", new { entity = "Example props", x = 0, y = 2 }).GetProperty("tile").GetInt32(), Is.EqualTo(0));
        Assert.That(Call("tilemap.get", new { entity = "Example props", x = 0, y = 0 }).GetProperty("tile").GetInt32(), Is.EqualTo(-1));
        Call("undo");
        Call("input.drag", new { x1 = (int)(x - cellPixels * 1.4f), y1 = (int)(y - cellPixels * 1.4f), x2 = (int)(x + cellPixels * 3.4f), y2 = (int)(y + cellPixels * 1.4f), frames = 15 });
        Call("input.drag", new { x1 = x, y1 = y, x2 = x, y2 = (int)(y + cellPixels * 2), frames = 15 });
        Assert.That(Call("tilemap.get", new { entity = "Example props", x = 2, y = 2 }).GetProperty("tile").GetInt32(), Is.EqualTo(0));
        Call("input.script", new { steps = new object[] {
            new { action = "move", x, y = (int)(y + cellPixels * 2) },
            new { action = "down", button = "left" },
            new { action = "move", x, y = (int)(y + cellPixels * 4) },
            new { action = "key", key = "Escape" },
            new { action = "release" } } });
        Assert.That(Call("tilemap.get", new { entity = "Example props", x = 0, y = 2 }).GetProperty("tile").GetInt32(), Is.EqualTo(0));
        Assert.That(Call("tilemap.get", new { entity = "Example props", x = 0, y = 4 }).GetProperty("tile").GetInt32(), Is.EqualTo(-1));
        Call("input.script", new { steps = new object[] {
            new { action = "move", x, y = (int)(y + cellPixels * 2) },
            new { action = "down", button = "left" },
            new { action = "move", x = 200, y = 800 },
            new { action = "move", x, y = (int)(y + cellPixels * 4) },
            new { action = "up", button = "left" } } });
        Assert.That(Call("tilemap.get", new { entity = "Example props", x = 0, y = 2 }).GetProperty("tile").GetInt32(), Is.EqualTo(0));
        Assert.That(Call("tilemap.get", new { entity = "Example props", x = 0, y = 4 }).GetProperty("tile").GetInt32(), Is.EqualTo(-1));
        Call("input.drag", new { x1 = (int)(x - cellPixels * 1.4f), y1 = (int)(y + cellPixels * .6f), x2 = (int)(x + cellPixels * 3.4f), y2 = (int)(y + cellPixels * 3.4f), frames = 15 });
        Call("entity.deselect");
        Call("hotkey.press", new { id = "Viewport.CursorNormal" });
        Call("view.focus", new { entity = "Example props", zoom = 4 });
        var viewBeforePick = Call("view.get");
        Call("input.click", new { x, y, count = 2 });
        Call("input.wait", new { frames = 10 });
        Assert.That(Call("entity.selected")[0].GetProperty("name").GetString(), Is.EqualTo("Example props"));
        Assert.That(Call("view.get").GetProperty("x").GetSingle(), Is.EqualTo(viewBeforePick.GetProperty("x").GetSingle()).Within(.01));
        Call("entity.deselect");
        Call("ui.click", new { label = "Scene Select", window = "Tile Palette", exact = true });
        Call("ui.click", new { path = "View/UI font/Inter" });
        Call("ui.click", new { path = "View/Interface scale/100%" });
        palette = Call("ui.windows").EnumerateArray().Single(w => w.GetProperty("name").GetString() == "Tile Palette");
        Resize(palette, 600, 1130);
        Call("view.focus", new { entity = "Example props", zoom = 4 });
        Call("ui.focus", new { window = "Game" });
        Call("input.move", new { x = 700, y = 600 });
        Call("input.wait", new { frames = 5 });
        facts = Call("ui.info");
        game = facts.GetProperty("gameView");
        ratio = game.GetProperty("width").GetSingle() / game.GetProperty("renderTargetWidth").GetSingle();
        mouse = facts.GetProperty("scaledMouse");
        x = (int)Math.Round(700 + (game.GetProperty("renderTargetWidth").GetSingle() / 2 - 64 - mouse.GetProperty("x").GetSingle()) * ratio);
        y = (int)Math.Round(600 + (game.GetProperty("renderTargetHeight").GetSingle() / 2 - mouse.GetProperty("y").GetSingle()) * ratio);
        cellPixels = 64 * ratio;
        Call("input.drag", new { x1 = (int)(x - cellPixels * .7f), y1 = (int)(y - cellPixels * .7f), x2 = (int)(x + cellPixels * 2.7f), y2 = (int)(y + cellPixels * .7f), frames = 15 });
        Call("input.move", new { x = 1250, y = 950 });
        Call("input.wait", new { frames = 5 });
        Capture("tile-scene-selection.png", "Real isolated editor: sparse selected cells and Scene Select");
        palette = Call("ui.windows").EnumerateArray().Single(w => w.GetProperty("name").GetString() == "Tile Palette");
        Capture("tile-palette.png", "Tile Palette source and scene selection controls", palette);
        Call("ui.focus", new { window = "Tile Palette" });
        Call("input.scroll", new { x = palette.GetProperty("x").GetInt32() + 200, y = 900, notches = -12 });
        Call("input.wait", new { frames = 5 });
        var zoom = WaitForWidget("Zoom", "Tile Palette");
        var sourcePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "tile-palette-source.png");
        var sourceTop = zoom.GetProperty("y").GetInt32() - 75;
        Call("screenshot", new { path = sourcePath, x = palette.GetProperty("x").GetInt32(), y = sourceTop, width = palette.GetProperty("width").GetInt32(), height = palette.GetProperty("y").GetInt32() + palette.GetProperty("height").GetInt32() - sourceTop });
        TestContext.AddTestAttachment(sourcePath, "Palette source atlas creates a brush stamp");
        Call("input.scroll", new { x = palette.GetProperty("x").GetInt32() + 200, y = 900, notches = 20 });
        Call("ui.click", new { label = "Edit Tileset", window = "Tile Palette", exact = true });
        Call("input.wait", new { frames = 5 });
        var editor = Call("ui.windows").EnumerateArray().Single(w => w.GetProperty("name").GetString() == "Tileset Editor");
        Resize(editor, 1080, 1120);
        editor = Call("ui.windows").EnumerateArray().Single(w => w.GetProperty("name").GetString() == "Tileset Editor");
        foreach (var browse in Call("ui.find", new { label = "Browse...", window = "Tileset Editor", exact = true }).EnumerateArray())
            Assert.That(browse.GetProperty("x").GetInt32() + browse.GetProperty("width").GetInt32(), Is.LessThanOrEqualTo(editor.GetProperty("x").GetInt32() + editor.GetProperty("width").GetInt32()));
        Capture("tileset-editor.png", "Tileset Editor grid and tile metadata", editor);
        foreach (var name in new[] { "Arena", "CollisionLab", "LightingLab" })
            Call("scene.create", new { name, load = false });
        Call("ui.click", new { path = "Build/Build Game" });
        WaitForWidget("Scenes/Arena", "Build Game");
        Call("ui.focus", new { window = "Build Game" });
        Call("input.key", new { key = "A", modifiers = new[] { "LeftControl" } });
        Call("input.wait", new { frames = 5 });
        var build = Call("ui.windows").EnumerateArray().Single(w => w.GetProperty("name").GetString() == "Build Game");
        Capture("build-scenes-modern.png", "Build Game with six scene rows and selected-set controls", build);
        Call("input.scroll", new { x = build.GetProperty("x").GetInt32() + build.GetProperty("width").GetInt32() - 6,
            y = build.GetProperty("y").GetInt32() + build.GetProperty("height").GetInt32() / 2, notches = -30 });
        WaitForWidget("Cancel", "Build Game");
        Call("ui.click", new { label = "Cancel", window = "Build Game", exact = true });
    }

    private static void Resize(JsonElement window, int width, int height)
    {
        var x = window.GetProperty("x").GetInt32();
        var y = window.GetProperty("y").GetInt32();
        Call("input.drag", new { x1 = x + window.GetProperty("width").GetInt32() - 2, y1 = y + window.GetProperty("height").GetInt32() - 2, x2 = x + width - 2, y2 = y + height - 2, frames = 15 });
    }

    private static void Capture(string name, string caption, JsonElement? window = null)
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, name);
        if (window is { } crop)
            Call("screenshot", new { path, x = crop.GetProperty("x").GetInt32(), y = crop.GetProperty("y").GetInt32(), width = crop.GetProperty("width").GetInt32(), height = crop.GetProperty("height").GetInt32() });
        else Call("screenshot", new { path });
        TestContext.AddTestAttachment(path, caption);
    }
}
