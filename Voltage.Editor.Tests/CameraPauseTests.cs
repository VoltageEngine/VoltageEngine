using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class CameraPauseTests
{
    [Test]
    public void Game_pause_preserves_camera_with_movement_input_and_editor_pause_starts_from_gameplay_position()
    {
        var previous = Call("scene.info").GetProperty("name").GetString();
        Call("scene.save");
        try
        {
            Call("script.write", new
            {
                path = "CameraPauseProbe.cs",
                content = "using Microsoft.Xna.Framework; using Voltage; public partial class CameraPauseProbe : Component, IUpdatable { public bool Freeze; public void Update() { if (!Freeze) return; Freeze = false; Core.Scene.Camera.Position = new Vector2(860, 400); Core.IsPauseMode = true; } }"
            });
            Call("scene.create", new { name = "CameraPauseRegression", template = "empty" });
            Call("component.set", new { entity = "Camera (Main)", type = "Camera", member = "GameZoom", value = 0.61f });
            Call("entity.create", new { name = "Game pause" });
            Call("component.add", new { entity = "Game pause", type = "CameraPauseProbe" });
            Call("scene.save");
            Call("play.start");
            Call("input.wait", new { frames = 20 });
            Assert.That(Camera().GetProperty("RawZoom").GetSingle(), Is.EqualTo(0.61f).Within(0.00001f));
            Call("component.set", new { entity = "Game pause", type = "CameraPauseProbe", member = "Freeze", value = true });
            Call("input.wait", new { frames = 20 });
            Assert.That(Call("play.state").GetProperty("paused").GetBoolean(), Is.True);
            Call("ui.focus", new { window = "GameWindow" });
            Call("input.script", new { steps = new object[] { new { action = "keyDown", key = "W" }, new { action = "wait", frames = 30 }, new { action = "scroll", delta = 1 }, new { action = "wait", frames = 10 }, new { action = "release" } } });
            AssertCamera();
            Call("play.pause", new { paused = false });
            Call("input.wait", new { frames = 10 });
            Call("play.pause", new { paused = true });
            Call("input.wait", new { frames = 30 });
            AssertCamera();
            Call("input.script", new { steps = new object[] { new { action = "keyDown", key = "W" }, new { action = "wait", frames = 30 }, new { action = "release" } } });
            Assert.That(Camera().GetProperty("Position").GetProperty("y").GetSingle(), Is.LessThan(395), "Editor pause should still allow deliberate camera navigation");
        }
        finally
        {
            Call("input.release");
            Call("play.stop");
            Call("scene.load", new { name = previous, force = true });
        }
    }

    [Test]
    public void Editor_view_zoom_is_never_saved_and_play_starts_from_game_zoom()
    {
        var previous = Call("scene.info").GetProperty("name").GetString();
        Call("scene.save");
        try
        {
            Call("scene.create", new { name = "GameZoomRegression", template = "empty" });
            Call("component.set", new { entity = "Camera (Main)", type = "Camera", member = "GameZoom", value = 0.75f });
            Call("view.set", new { zoom = 1.6f });
            Call("scene.save");
            Call("play.start");
            Call("input.wait", new { frames = 5 });
            Assert.That(Camera().GetProperty("RawZoom").GetSingle(), Is.EqualTo(0.75f).Within(0.00001f), "Play must start from the game zoom");
            Call("play.stop");
            Call("input.wait", new { frames = 30 });
            Assert.That(Camera().GetProperty("GameZoom").GetSingle(), Is.EqualTo(0.75f).Within(0.00001f), "Saving must not write the view zoom");
            Assert.That(Camera().GetProperty("RawZoom").GetSingle(), Is.EqualTo(1.6f).Within(0.00001f), "Stop must give the editor view back");
        }
        finally
        {
            Call("play.stop");
            Call("scene.load", new { name = previous, force = true });
        }
    }

    private static System.Text.Json.JsonElement Camera() => Call("component.get", new { entity = "Camera (Main)", type = "Camera" }).GetProperty("values");
    private static void AssertCamera()
    {
        var camera = Camera();
        Assert.That(camera.GetProperty("RawZoom").GetSingle(), Is.EqualTo(0.61f).Within(0.00001f));
        Assert.That(camera.GetProperty("Position").GetProperty("x").GetSingle(), Is.EqualTo(860).Within(0.01f));
        Assert.That(camera.GetProperty("Position").GetProperty("y").GetSingle(), Is.EqualTo(400).Within(0.01f));
    }
}
