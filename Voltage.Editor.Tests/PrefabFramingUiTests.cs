using System;
using System.IO;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class PrefabFramingUiTests
{
    [TestCase(16, 16, 0, 0)]
    [TestCase(5000, 3000, -800, -1200)]
    [TestCase(30, 9000, 6000, -4000)]
    public void Opening_fits_and_centers_content_and_return_restores_unsaved_camera_view(int width, int height, int x, int y)
    {
        var previous = Call("scene.info").GetProperty("name").GetString();
        Call("scene.save");
        try
        {
            Call("script.write", new {
                path = "PrefabViewProbe.cs",
                content = "using Voltage; using Microsoft.Xna.Framework; using System.Linq; public partial class PrefabViewProbe : Component { public float CenterX, CenterY, VisibleWidth, VisibleHeight, ContentX, ContentY; public override void OnStart() { var s = Core.Scene; var midpoint = s.Camera.ScreenToWorldPoint(s.SceneRenderTargetSize.ToVector2() / 2); CenterX=midpoint.X; CenterY=midpoint.Y; var size=s.Camera.ScreenToWorldPoint(s.SceneRenderTargetSize.ToVector2())-s.Camera.ScreenToWorldPoint(Vector2.Zero); VisibleWidth=size.X; VisibleHeight=size.Y; var bounds=s.Entities.First(e=>e.GetComponent<BoxCollider>() != null).GetComponent<BoxCollider>().Bounds; ContentX=bounds.Center.X; ContentY=bounds.Center.Y; } }"
            });
            Call("scene.create", new { name = $"PrefabFraming{width}_{height}", template = "empty", force = true });
            Call("entity.create", new { name = "Framed prefab", x, y });
            Call("component.add", new { entity = "Framed prefab", type = "BoxCollider" });
            Call("component.set", new { entity = "Framed prefab", type = "BoxCollider", member = "Width", value = width });
            Call("component.set", new { entity = "Framed prefab", type = "BoxCollider", member = "Height", value = height });
            var prefab = Call("prefab.create", new { entity = "Framed prefab", name = $"Frame{width}_{height}", keepOriginal = false });
            Call("scene.save");
            Call("view.set", new { x = 731, y = -219, zoom = .57 });
            var before = Call("view.get");
            for (int repeat = 0; repeat < 2; repeat++)
            {
                Call("prefab.open", new { asset = prefab.GetProperty("guid").GetString() });
                Call("input.wait", new { frames = 15 });
                if (repeat == 1)
                {
                    Call("view.set", new { x = -1000, y = 2000, zoom = .2 });
                    Call("hotkey.press", new { id = "Playback.ResetScene" });
                    Call("input.wait", new { frames = 15 });
                }
                Call("entity.create", new { name = "View probe" });
                Call("component.add", new { entity = "View probe", type = "PrefabViewProbe" });
                Call("input.wait", new { frames = 5 });
                var values = Call("component.get", new { entity = "View probe", type = "PrefabViewProbe" }).GetProperty("values");
                Assert.That(values.GetProperty("CenterX").GetSingle(), Is.EqualTo(values.GetProperty("ContentX").GetSingle()).Within(.1));
                Assert.That(values.GetProperty("CenterY").GetSingle(), Is.EqualTo(values.GetProperty("ContentY").GetSingle()).Within(.1));
                Assert.That(values.GetProperty("VisibleWidth").GetSingle(), Is.GreaterThan(width));
                Assert.That(values.GetProperty("VisibleHeight").GetSingle(), Is.GreaterThan(height));
                Call("prefab.close", new { force = true });
                Call("input.wait", new { frames = 15 });
                var after = Call("view.get");
                foreach (var member in new[] { "x", "y", "zoom" })
                    Assert.That(after.GetProperty(member).GetSingle(), Is.EqualTo(before.GetProperty(member).GetSingle()).Within(.001), member);
            }
        }
        finally
        {
            try { Call("prefab.close", new { force = true }); } catch { }
            Call("scene.load", new { name = previous, force = true });
        }
    }
}
