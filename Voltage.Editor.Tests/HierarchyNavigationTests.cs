using System.Linq;
using System.Threading;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class HierarchyNavigationTests
{
    [Test]
    public void Arrow_keys_walk_collapsed_nested_entities_in_both_directions_and_repeat()
    {
        Call("entity.create", new { name = "NavRoot" });
        Call("entity.create", new { name = "NavBranch", parent = "NavRoot" });
        Call("entity.create", new { name = "NavLeaf", parent = "NavBranch" });
        Call("entity.create", new { name = "NavSibling", parent = "NavRoot" });
        Call("entity.create", new { name = "NavOther" });
        try
        {
            Call("ui.click", new { label = "EntitySearch", window = "Scene Graph" });
            Call("input.type", new { text = "Nav" });
            WaitForWidget("NavRoot (2)", "Scene Graph");
            Call("ui.click", new { label = "x", window = "Scene Graph", exact = true });
            WaitForWidget("NavRoot (2)", "Scene Graph");
            Call("ui.click", new { label = "NavRoot (2)", window = "Scene Graph", exact = true });
            foreach (var name in new[] { "NavBranch", "NavLeaf", "NavSibling", "NavOther" })
            {
                Call("input.key", new { key = "Down" });
                Thread.Sleep(150);
                Assert.That(Selected(), Is.EqualTo(name));
            }
            foreach (var name in new[] { "NavSibling", "NavLeaf", "NavBranch", "NavRoot" })
            {
                Call("input.key", new { key = "Up" });
                Thread.Sleep(150);
                Assert.That(Selected(), Is.EqualTo(name));
            }
            Call("input.key", new { key = "Down", action = "down" });
            Thread.Sleep(650);
            Call("input.release");
            Assert.That(Selected(), Is.EqualTo("NavOther"));

            Call("ui.click", new { label = "EntitySearch", window = "Scene Graph" });
            Call("input.type", new { text = "Nav" });
            Call("input.key", new { key = "Up" });
            Assert.That(Selected(), Is.EqualTo("NavOther"), "Editing text moved the entity selection");
        }
        finally
        {
            Call("input.release");
            if (Call("ui.find", new { label = "x", window = "Scene Graph", exact = true }).GetArrayLength() > 0)
                Call("ui.click", new { label = "x", window = "Scene Graph", exact = true });
            Call("entity.delete", new { entity = "NavRoot" });
            Call("entity.delete", new { entity = "NavOther" });
        }
    }

    private static string Selected() => Call("entity.selected").EnumerateArray().Single().GetProperty("name").GetString();

    [Test]
    public void Arrow_navigation_uses_selection_with_locked_inspector_and_scrolls_clipped_descendants()
    {
        Call("entity.create", new { name = "LargeNavRoot" });
        for (int i = 0; i < 120; i++) Call("entity.create", new { name = $"LargeNav{i:D3}", parent = "LargeNavRoot" });
        try
        {
            Call("ui.click", new { label = "EntitySearch", window = "Scene Graph" });
            Call("input.type", new { text = "LargeNavRoot" });
            WaitForWidget("LargeNavRoot (120)", "Scene Graph");
            Call("ui.click", new { label = "x", window = "Scene Graph", exact = true });
            Call("ui.click", new { label = "LargeNavRoot (120)", window = "Scene Graph", exact = true });
            WaitForWidget("Lock On", "Main Entity Inspector");
            Call("ui.click", new { label = "Lock On", window = "Main Entity Inspector", exact = true });
            Call("ui.click", new { label = "LargeNavRoot (120)", window = "Scene Graph", exact = true });
            Call("entity.select", new { entity = "LargeNav119" });
            Call("input.key", new { key = "Up" });
            Thread.Sleep(200);
            Assert.That(Selected(), Is.EqualTo("LargeNav118"));
            WaitForWidget("LargeNav118 (0)", "Scene Graph");
            var visible = Call("ui.find", new { label = "LargeNav118 (0)", window = "Scene Graph", exact = true, visibleOnly = true });
            Assert.That(visible.GetArrayLength(), Is.GreaterThan(0), "Selection stayed outside the clipped viewport");
            Call("input.key", new { key = "Down" });
            Thread.Sleep(150);
            Assert.That(Selected(), Is.EqualTo("LargeNav119"));
        }
        finally
        {
            Call("input.release");
            if (Call("ui.find", new { label = "Lock Off", window = "Main Entity Inspector", exact = true }).GetArrayLength() > 0)
                Call("ui.click", new { label = "Lock Off", window = "Main Entity Inspector", exact = true });
            Call("entity.delete", new { entity = "LargeNavRoot" });
        }
    }

    [Test]
    public void Scene_graph_clicks_and_gateway_focus_center_nested_content_after_selection_changes()
    {
        Call("entity.create", new { name = "FrameRoot", x = -500, y = -600 });
        Call("entity.create", new { name = "FrameGroup", parent = "FrameRoot" });
        Call("entity.create", new { name = "FrameLeaf", parent = "FrameGroup" });
        Call("entity.set", new { entity = "FrameLeaf", x = 400, y = 200 });
        Call("component.add", new { entity = "FrameLeaf", type = "BoxCollider" });
        Call("component.set", new { entity = "FrameLeaf", type = "BoxCollider", member = "LocalOffset", value = new { x = 40, y = 30 } });
        Call("entity.create", new { name = "FrameOther", x = 900, y = 700 });
        try
        {
            var focus = Call("view.focus", new { entity = "FrameLeaf", zoom = 2 });
            var focusX = focus.GetProperty("x").GetSingle();
            var focusY = focus.GetProperty("y").GetSingle();
            foreach (var entity in new[] { "FrameRoot", "FrameGroup", "FrameLeaf" })
            {
                var result = Call("view.focus", new { entity, zoom = 2 });
                Assert.That(result.GetProperty("x").GetSingle(), Is.EqualTo(focusX).Within(.1));
                Assert.That(result.GetProperty("y").GetSingle(), Is.EqualTo(focusY).Within(.1));
            }
            Call("entity.select", new { entity = "FrameOther" });
            foreach (var (label, name, x, y) in new[]
                     { ("FrameRoot (1)", "FrameRoot", 440, 230), ("FrameOther (0)", "FrameOther", 900, 700), ("FrameLeaf (0)", "FrameLeaf", 440, 230) })
            {
                Call("ui.click", new { label = "EntitySearch", window = "Scene Graph" });
                Call("input.type", new { text = name });
                WaitForWidget(label, "Scene Graph");
                Call("ui.click", new { label, window = "Scene Graph", exact = true });
                Thread.Sleep(1500);
                Assert.That(Call("entity.selected").EnumerateArray().Single().GetProperty("name").GetString(), Is.EqualTo(name));
                var view = Call("view.get");
                Assert.That(view.GetProperty("x").GetSingle(), Is.EqualTo(focusX + x - 440).Within(1));
                Assert.That(view.GetProperty("y").GetSingle(), Is.EqualTo(focusY + y - 230).Within(1));
                Call("ui.click", new { label = "x", window = "Scene Graph", exact = true });
            }
        }
        finally
        {
            Call("input.release");
            if (Call("ui.find", new { label = "x", window = "Scene Graph", exact = true }).GetArrayLength() > 0)
                Call("ui.click", new { label = "x", window = "Scene Graph", exact = true });
            Call("entity.delete", new { entity = "FrameRoot" });
            Call("entity.delete", new { entity = "FrameOther" });
        }
    }
}
