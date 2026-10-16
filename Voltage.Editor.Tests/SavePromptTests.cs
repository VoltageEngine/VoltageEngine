using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class SavePromptTests
{
    [TestCase(1600, 900, 100)]
    [TestCase(1920, 1080, 200)]
    [TestCase(900, 750, 125)]
    public void Save_prompt_fits_scaled_text_and_buttons_and_can_be_resized(int width, int height, int scale)
    {
        try
        {
            Call("scene.load", new { name = "TestScene", force = true });
            Call("ui.resize", new { width, height });
            Call("ui.click", new { path = $"View/Interface scale/{scale}%" });
            Call("entity.create", new { name = "An unsaved entity with a deliberately long description so the changes list needs to wrap across multiple lines" });
            Call("ui.click", new { path = "Project/Load Scene" });
            SceneMenuTests.ScrollPickerTo("Scenes/");
            Call("ui.click", new { label = "Scenes/", window = "load-scene-popup", exact = true });
            SceneMenuTests.ScrollPickerTo("TestScene.vscene");
            Call("ui.click", new { label = "TestScene.vscene", window = "load-scene-popup", exact = true });
            WaitForWidget("Open", "load-scene-popup");
            Call("ui.click", new { label = "Open", window = "load-scene-popup", exact = true });
            WaitForWidget("Cancel", "Save Changes?");
            var popup = Popup();
            Call("screenshot", new { path = Path.Combine(Path.GetTempPath(), "Voltage-HeadlessHurt", $"save-dialog-{scale}.png"), window = true });
            Assert.That(popup.GetProperty("height").GetInt32(), Is.GreaterThan(200));
            var buttons = new[] { "Save", "Don't Save", "Cancel" }.Select(label =>
                Call("ui.find", new { label, window = "Save Changes?", exact = true, visibleOnly = false }).EnumerateArray().Single()).ToArray();
            Assert.That(buttons.Length, Is.EqualTo(3));
            foreach (var button in buttons)
            {
                Assert.That(button.GetProperty("x").GetInt32() + button.GetProperty("width").GetInt32(),
                    Is.LessThanOrEqualTo(popup.GetProperty("x").GetInt32() + popup.GetProperty("width").GetInt32()));
                Assert.That(button.GetProperty("y").GetInt32() + button.GetProperty("height").GetInt32(),
                    Is.LessThanOrEqualTo(popup.GetProperty("y").GetInt32() + popup.GetProperty("height").GetInt32()));
            }
            Call("ui.resize", new { width = width + 400, height = height + 300 });
            Thread.Sleep(100);
            popup = Popup();
            int x = popup.GetProperty("x").GetInt32() + popup.GetProperty("width").GetInt32() - 3;
            int y = popup.GetProperty("y").GetInt32() + popup.GetProperty("height").GetInt32() - 3;
            Call("input.drag", new { x1 = x, y1 = y, x2 = x + 60, y2 = y + 50, frames = 15 });
            Thread.Sleep(100);
            var resized = Popup();
            Assert.That(resized.GetProperty("width").GetInt32() > popup.GetProperty("width").GetInt32() ||
                resized.GetProperty("height").GetInt32() > popup.GetProperty("height").GetInt32(), Is.True, "Resize handle did not change the dialog size");
            Call("ui.click", new { label = "Cancel", window = "Save Changes?", exact = true });
            Assert.That(Call("status").GetProperty("dirty").GetBoolean(), Is.True, "Cancel discarded unsaved edits");
        }
        finally
        {
            foreach (var window in new[] { "Save Changes?", "load-scene-popup" })
                if (Call("ui.find", new { label = "Cancel", window, exact = true }).GetArrayLength() > 0)
                    Call("ui.click", new { label = "Cancel", window, exact = true });
            Call("input.release");
            Call("scene.load", new { name = "TestScene", force = true });
            Call("ui.click", new { path = "View/Interface scale/100%" });
            Call("ui.resize", new { width = 1600, height = 900 });
        }
    }

    private static JsonElement Popup() => Call("ui.popups").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Save Changes?");
}
