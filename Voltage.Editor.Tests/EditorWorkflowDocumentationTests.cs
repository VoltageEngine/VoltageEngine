using System.IO;
using System.Linq;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class EditorWorkflowDocumentationTests
{
    [Test]
    public void Core_plugin_and_timeline_controls_remain_inside_their_windows()
    {
        try
        {
            Call("ui.resize", new { width = 1600, height = 1200 });
            Call("ui.click", new { path = "View/UI font/Inter" });
            Call("ui.click", new { path = "View/Interface scale/100%" });
            Call("window.show", new { name = "CoreWindow", visible = true });
            Call("input.wait", new { frames = 5 });
            Call("ui.focus", new { window = "Core Settings" });
            Call("input.drag", new { x1 = 1278, y1 = 400, x2 = 1050, y2 = 400, frames = 15 });
            WaitForWidget("ResetSceneAutomatically", "Core Settings");
            Capture("Core Settings", "core-settings.png");
            Call("ui.click", new { path = "Plugins/Plugin Manager" });
            WaitForWidget("Create New Plugin", "Plugin Manager");
            var pluginBounds = Call("ui.windows").EnumerateArray().Single(w => w.GetProperty("name").GetString() == "Plugin Manager");
            Call("input.drag", new { x1 = pluginBounds.GetProperty("x").GetInt32() + 718, y1 = pluginBounds.GetProperty("y").GetInt32() + 458, x2 = pluginBounds.GetProperty("x").GetInt32() + 718, y2 = pluginBounds.GetProperty("y").GetInt32() + 998, frames = 15 });
            Check("Plugin Manager", "Create New Plugin", "Reload Plugins");
            Call("ui.click", new { label = "Add Plugin", window = "Plugin Manager", kind = "header", exact = true });
            Check("Plugin Manager", "Browse");
            Capture("Plugin Manager", "plugin-manager.png");
            ClosePluginManager();
            Call("timeline.create", new { name = "ExampleSequence" });
            Call("entity.create", new { name = "Sequence director" });
            Call("component.add", new { entity = "Sequence director", type = "TimelineDirector" });
            Call("component.set", new { entity = "Sequence director", type = "TimelineDirector", member = "Timeline", value = "Data/Timelines/ExampleSequence.vtimeline" });
            Call("entity.select", new { entity = "Sequence director" });
            Call("window.show", new { name = "TimelineWindow", visible = true });
            WaitForWidget("Save", "Timeline");
            Call("input.drag", new { x1 = 700, y1 = 978, x2 = 700, y2 = 380, frames = 15 });
            Check("Timeline", "Play", "Stop", "Record", "Zoom out", "Zoom in", "Fit", "Save", "Load");
            Call("input.drag", new { x1 = 1048, y1 = 300, x2 = 1530, y2 = 300, frames = 15 });
            Capture("Timeline", "timeline-editor.png");
            Call("input.drag", new { x1 = 1528, y1 = 300, x2 = 1050, y2 = 300, frames = 15 });
            foreach (var scale in new[] { "150%", "200%" })
            {
                Call("ui.click", new { path = "View/Interface scale/" + scale });
                Call("input.wait", new { frames = 5 });
                Call("ui.focus", new { window = "Timeline" });
                Check("Timeline", "Play", "Stop", "Record", "Zoom out", "Zoom in", "Fit", "Save", "Load");
                Call("ui.click", new { path = "Plugins/Plugin Manager" });
                Call("input.wait", new { frames = 5 });
                Check("Plugin Manager", "Create New Plugin", "Reload Plugins");
                Check("Plugin Manager", "Browse");
                ClosePluginManager();
            }
        }
        finally
        {
            Call("ui.click", new { path = "View/UI font/Classic (ProggyClean)" });
            Call("ui.click", new { path = "View/Interface scale/100%" });
            Call("ui.resize", new { width = 1600, height = 900 });
        }
    }

    private static void ClosePluginManager()
    {
        var bounds = Call("ui.windows").EnumerateArray().Single(w => w.GetProperty("name").GetString() == "Plugin Manager");
        Call("input.click", new { x = bounds.GetProperty("x").GetInt32() + bounds.GetProperty("width").GetInt32() - 27, y = bounds.GetProperty("y").GetInt32() + 24 });
    }

    private static void Check(string window, params string[] labels)
    {
        var bounds = Call("ui.windows").EnumerateArray().Single(w => w.GetProperty("name").GetString() == window);
        var right = bounds.GetProperty("x").GetInt32() + bounds.GetProperty("width").GetInt32();
        foreach (var label in labels)
        {
            var item = WaitForWidget(label, window);
            Assert.That(item.GetProperty("x").GetInt32() + item.GetProperty("width").GetInt32(), Is.LessThanOrEqualTo(right), label);
        }
    }

    private static void Capture(string window, string filename)
    {
        Call("ui.focus", new { window });
        Call("input.move", new { x = 1450, y = 1100 });
        Call("input.wait", new { frames = 5 });
        var bounds = Call("ui.windows").EnumerateArray().Single(w => w.GetProperty("name").GetString() == window);
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, filename);
        Call("screenshot", new { path, x = bounds.GetProperty("x").GetInt32(), y = bounds.GetProperty("y").GetInt32(), width = bounds.GetProperty("width").GetInt32(), height = window == "Core Settings" ? 600 : window == "Plugin Manager" ? 650 : bounds.GetProperty("height").GetInt32() });
        TestContext.AddTestAttachment(path, window);
    }
}
