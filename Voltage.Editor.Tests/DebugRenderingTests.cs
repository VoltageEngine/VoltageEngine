using System.IO;
using System.Text.Json;
using System.Threading;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class DebugRenderingTests
{
    private static int Categories()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(DataDirectory, "Data", "Settings.json")));
        return int.Parse(document.RootElement.GetProperty("Editor_DebugRenderCategories").ToString());
    }
    private static void Open() { Call("ui.click", new { label = "DebugRenderingFilters", window = "Editor Tools", exact = true }); Thread.Sleep(150); }
    private static void Click(string label) { Call("ui.click", new { label, kind = "checkbox", exact = true }); Thread.Sleep(150); }
    private static void Eye() { Call("ui.click", new { label = "DebugRenderingToggle", window = "Editor Tools", exact = true }); Thread.Sleep(150); }
    private static void Close()
    {
        Call("input.key", new { key = "Escape", action = "down" });
        Thread.Sleep(150);
        Call("input.release");
        Thread.Sleep(150);
    }
    private static void ExpectCategories(int expected)
    {
        for (int i = 0; i < 20 && Categories() != expected; i++) Thread.Sleep(50);
        if (Categories() != expected) TestContext.Progress.WriteLine(Call("log.tail", new { count = 8 }));
        Assert.That(Categories(), Is.EqualTo(expected));
    }

    [Test]
    public void Dropdown_filters_independent_categories_all_and_master_without_dirtying_scene()
    {
        Call("scene.save");
        try
        {
            Open();
            foreach (var category in new[] { "All", "Colliders", "Sprite renderers", "Tilemaps", "Lights", "UI", "Other components", "Transform gizmos" })
                WaitForWidget(category, "DebugRenderingCategories");
            Click("Lights");
            Assert.That(Categories(), Is.EqualTo(119));
            Click("Sprite renderers");
            Assert.That(Categories(), Is.EqualTo(117));
            Click("All");
            Assert.That(Categories(), Is.EqualTo(127));
            Click("All");
            Assert.That(Categories(), Is.Zero);
            Click("Colliders");
            Assert.That(Categories(), Is.EqualTo(1));
            Close();
            Eye();
            Assert.That(Categories(), Is.EqualTo(1), "Master switch lost selected categories");
            Eye();
            Assert.That(Categories(), Is.EqualTo(1));
            Assert.That(Call("status").GetProperty("dirty").GetBoolean(), Is.False);
            Call("scene.load", new { name = "TestScene", force = true });
            Assert.That(Categories(), Is.EqualTo(1), "Scene reload lost editor preferences");
            Open();
            Click("Colliders");
            Close();
            Eye();
            ExpectCategories(127);
        }
        finally
        {
            Close();
            Call("input.release");
            if (Categories() != 127) { Open(); Click("All"); Close(); }
        }
    }
}
