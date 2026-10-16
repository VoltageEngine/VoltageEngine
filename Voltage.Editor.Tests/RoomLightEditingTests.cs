using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class RoomLightEditingTests
{
    private const string Inspector = "Main Entity Inspector";
    private static JsonElement Vertices() => Call("component.get", new { entity = "RoomEditTest", type = "RoomLight" }).GetProperty("values").GetProperty("Vertices");
    private static JsonElement Visible(string label)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            var matches = Call("ui.find", new { label, window = Inspector, exact = true });
            if (matches.GetArrayLength() > 0) return matches[0];
            var hidden = Call("ui.find", new { label, window = Inspector, exact = true, visibleOnly = false });
            var window = Call("ui.windows").EnumerateArray().First(w => w.GetProperty("name").GetString() == Inspector);
            int x = window.GetProperty("x").GetInt32() + 30;
            int y = window.GetProperty("y").GetInt32() + window.GetProperty("height").GetInt32() / 2;
            int direction = hidden.GetArrayLength() > 0 && hidden[0].GetProperty("y").GetInt32() < window.GetProperty("y").GetInt32() ? 3 : -3;
            Call("input.scroll", new { x, y, notches = direction });
        }
        throw new TimeoutException($"Cannot scroll to {label}");
    }
    private static void Click(string label) { Visible(label); Call("ui.click", new { label, window = Inspector, exact = true }); }
    private static void Shortcut(string key, params string[] modifiers) { Call("input.key", new { key, modifiers }); Thread.Sleep(150); }

    [SetUp]
    public void CreateLight()
    {
        Call("scene.create", new { name = "RoomEdit" + Guid.NewGuid().ToString("N"), force = true });
        Call("entity.create", new { name = "RoomEditTest" });
        Call("component.add", new { entity = "RoomEditTest", type = "RoomLight" });
        Call("entity.select", new { entity = "RoomEditTest" });
        Thread.Sleep(400);
        if (Call("ui.find", new { label = "Vertices [4]", window = Inspector, exact = true, visibleOnly = false }).GetArrayLength() == 0)
            Click("RoomLight");
        Visible("Vertices [4]");
        Click("Vertices [4]");
        Visible("Add Element");
    }

    [TearDown]
    public void Cleanup() { Call("input.release"); Call("scene.load", new { name = "TestScene", force = true }); }

    [Test]
    public void Vertex_drag_ctrl_z_redo_and_later_edits_keep_independent_history()
    {
        var original = Vertices().GetRawText();
        void DragFirstVertex(int distance)
        {
            var widget = Visible("0");
            int x = widget.GetProperty("x").GetInt32() + 25;
            int y = widget.GetProperty("y").GetInt32() + widget.GetProperty("height").GetInt32() / 2;
            Call("input.drag", new { x1 = x, y1 = y, x2 = x + distance, y2 = y, frames = 8 });
            Thread.Sleep(150);
        }
        DragFirstVertex(35);
        var edited = Vertices().GetRawText();
        Assert.That(edited, Is.Not.EqualTo(original));
        Shortcut("z", "ctrl");
        Assert.That(Vertices().GetRawText(), Is.EqualTo(original));
        Shortcut("y", "ctrl");
        Assert.That(Vertices().GetRawText(), Is.EqualTo(edited));
        DragFirstVertex(20);
        Assert.That(Vertices().GetRawText(), Is.Not.EqualTo(edited));
        Shortcut("z", "ctrl");
        Assert.That(Vertices().GetRawText(), Is.EqualTo(edited));
        Shortcut("z", "ctrl");
        Assert.That(Vertices().GetRawText(), Is.EqualTo(original));
    }

    [Test]
    public void Adding_vertices_is_undoable_and_saved_list_round_trips()
    {
        var original = Vertices().GetRawText();
        Click("Add Element");
        Assert.That(Vertices().GetArrayLength(), Is.EqualTo(5));
        Shortcut("z", "ctrl");
        Assert.That(Vertices().GetRawText(), Is.EqualTo(original));
        Shortcut("y", "ctrl");
        Assert.That(Vertices().GetArrayLength(), Is.EqualTo(5));
        Call("scene.save");
        Call("scene.reload");
        Assert.That(Vertices().GetArrayLength(), Is.EqualTo(5));
    }
}
