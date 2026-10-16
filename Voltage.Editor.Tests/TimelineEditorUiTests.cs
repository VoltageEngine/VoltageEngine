using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class TimelineEditorUiTests
{
    [Test]
    public void Preview_transport_scrubbing_and_scene_save_restore_the_original_camera()
    {
        Call("scene.create", new { name = "TimelinePoseScene", force = true });
        var owner = Owner("Preview owner", "PosePreview");
        var camera = Call("entity.list").EnumerateArray().Single(e => e.GetProperty("name").GetString() == "Camera (Main)").GetProperty("guid").GetString();
        Call("timeline.bind", new { entity = owner, role = "Actor", target = camera });
        const string asset = "Data/Timelines/PosePreview.vtimeline";
        Call("timeline.track.add", new { asset, type = "transform", role = "Actor" });
        Call("timeline.key.add", new { asset, track = 0, channel = "Position", time = 0, value = new { x = 100, y = 200 } });
        Call("timeline.key.add", new { asset, track = 0, channel = "Position", time = 5, value = new { x = 300, y = 400 } });
        Call("view.set", new { x = 17, y = -23, zoom = 2 });
        Call("scene.save");
        Call("ui.resize", new { width = 1600, height = 1200 });
        Call("ui.click", new { path = "View/Interface scale/75%" });
        Call("entity.select", new { entity = owner });
        Call("window.show", new { name = "TimelineWindow", visible = true });
        Call("input.wait", new { frames = 5 });
        Call("ui.focus", new { window = "Timeline" });
        WaitForWidget("Play", "Timeline");
        var bounds = Call("ui.windows").EnumerateArray().Single(w => w.GetProperty("name").GetString() == "Timeline");
        if (bounds.GetProperty("height").GetInt32() < 500)
            Call("input.drag", new { x1 = 700, y1 = bounds.GetProperty("y").GetInt32() - 3, x2 = 700, y2 = 380, frames = 15 });
        Click("Play");
        Call("input.wait", new { frames = 30 });
        Click("Pause");
        var time = State().GetProperty("time").GetSingle();
        Assert.That(time, Is.GreaterThan(0));
        Assert.That(Call("view.get").GetProperty("x").GetSingle(), Is.EqualTo(100 + 40 * time).Within(1));
        Call("input.wait", new { frames = 15 });
        Assert.That(Call("view.get").GetProperty("x").GetSingle(), Is.EqualTo(100 + 40 * time).Within(1));
        foreach (var sample in new[] { 2f, .5f, 3f })
        {
            var canvas = WaitForWidget("tlcontent", "Timeline");
            var zero = canvas.GetProperty("x").GetInt32() + canvas.GetProperty("width").GetInt32() - 60 - 5 * 80;
            Call("input.click", new { x = (int)(zero + sample * 80), y = canvas.GetProperty("y").GetInt32() + 4 });
            Call("input.wait", new { frames = 5 });
            Assert.That(State().GetProperty("time").GetSingle(), Is.EqualTo(sample).Within(.03));
            Assert.That(Call("view.get").GetProperty("x").GetSingle(), Is.EqualTo(100 + 40 * sample).Within(2));
        }
        Capture("timeline-preview.png", "Timeline sampled pose and transport");
        Click("Stop");
        AssertCameraBaseline();
        Click("Play");
        Call("input.wait", new { frames = 10 });
        Call("scene.save");
        AssertCameraBaseline();
        Assert.That(State().GetProperty("playing").GetBoolean(), Is.False);
        Call("scene.reload");
        AssertCameraBaseline();
    }

    private static void Capture(string name, string description)
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, name);
        Call("screenshot", new { path });
        TestContext.AddTestAttachment(path, description);
    }

    private static void AssertCameraBaseline()
    {
        var view = Call("view.get");
        Assert.That(view.GetProperty("x").GetSingle(), Is.EqualTo(17).Within(.01));
        Assert.That(view.GetProperty("y").GetSingle(), Is.EqualTo(-23).Within(.01));
        Assert.That(view.GetProperty("zoom").GetSingle(), Is.EqualTo(2).Within(.01));
    }

    [Test]
    public void Scene_picker_lock_dirty_guard_and_asset_drop_preserve_context()
    {
        Call("scene.create", new { name = "TimelineUiScene", force = true });
        var a = Owner("Owner A", "PickerA");
        var b = Owner("Owner B", "PickerB");
        Call("scene.save");
        Call("entity.deselect");
        Call("ui.resize", new { width = 1600, height = 1200 });
        Call("ui.click", new { path = "View/Interface scale/75%" });
        Call("window.show", new { name = "TimelineWindow", visible = true });
        Call("input.wait", new { frames = 5 });
        Call("ui.focus", new { window = "Timeline" });
        WaitForWidget("Owner A / PickerA", "Timeline");
        Capture("timeline-picker.png", "Current scene cutscene picker");
        Click("Owner A / PickerA");
        Assert.That(State().GetProperty("director").GetString(), Is.EqualTo(a));
        Click("Lock timeline");
        Call("entity.select", new { entity = b });
        Call("input.wait", new { frames = 5 });
        Assert.That(State().GetProperty("director").GetString(), Is.EqualTo(a));
        Click("Lock timeline");
        Call("input.wait", new { frames = 5 });
        Assert.That(State().GetProperty("director").GetString(), Is.EqualTo(b));
        SetLength(9);
        Call("entity.select", new { entity = a });
        WaitForWidget("Cancel", "Unsaved timeline changes");
        Assert.That(Call("entity.selected")[0].GetProperty("guid").GetString(), Is.EqualTo(b));
        Prompt("Cancel");
        Assert.That(State().GetProperty("duration").GetSingle(), Is.EqualTo(9));
        Assert.That(State().GetProperty("dirty").GetBoolean(), Is.True);
        Call("editor.exit");
        Prompt("Cancel");
        Assert.That(State().GetProperty("dirty").GetBoolean(), Is.True);
        Assert.That(Call("status").GetProperty("scene").GetProperty("name").GetString(), Is.EqualTo("TimelineUiScene"));
        Call("entity.select", new { entity = a });
        Prompt("Save");
        Assert.That(State().GetProperty("director").GetString(), Is.EqualTo(a));
        Assert.That(ReadDuration("PickerB"), Is.EqualTo(9));
        SetLength(8);
        Call("entity.select", new { entity = b });
        Prompt("Discard");
        Assert.That(ReadDuration("PickerA"), Is.EqualTo(5));
        Assert.That(State().GetProperty("director").GetString(), Is.EqualTo(b));
        Call("input.drag", new { x1 = 700, y1 = 978, x2 = 700, y2 = 480, frames = 15 });
        UndockTimeline();
        Search("PickerA");
        if (Call("ui.find", new { label = "PickerA.vtimeline", window = "Asset Browser", exact = true }).GetArrayLength() == 0)
        {
            var data = WaitForWidget("Data", "Asset Browser");
            Call("input.click", new { x = data.GetProperty("x").GetInt32() + 8, y = data.GetProperty("y").GetInt32() + 10 });
            Call("input.wait", new { frames = 5 });
        }
        if (Call("ui.find", new { label = "PickerA.vtimeline", window = "Asset Browser", exact = true }).GetArrayLength() == 0)
        {
            var folder = WaitForWidget("Timelines", "Asset Browser");
            Call("input.click", new { x = folder.GetProperty("x").GetInt32() + 8, y = folder.GetProperty("y").GetInt32() + 10 });
            Call("input.wait", new { frames = 5 });
        }
        var source = WaitForWidget("PickerA.vtimeline", "Asset Browser");
        var target = WaitForWidget("Drop timeline asset here", "Timeline");
        Drag(source, target);
        Assert.That(State().GetProperty("standalone").GetBoolean(), Is.True);
        Assert.That(State().TryGetProperty("director", out var droppedDirector) && droppedDirector.ValueKind != JsonValueKind.Null, Is.False);
        Assert.That(Call("status").GetProperty("scene").GetProperty("name").GetString(), Is.EqualTo("TimelineUiScene"));
        Assert.That(State().GetProperty("path").GetString(), Does.EndWith("PickerA.vtimeline"));
        Click("Preview with Owner A");
        Assert.That(State().GetProperty("director").GetString(), Is.EqualTo(a));
        Call("entity.set", new { entity = a, name = "Duplicate" });
        Call("entity.set", new { entity = b, name = "Duplicate" });
        var bLabel = Call("entity.list").EnumerateArray().Single(e => e.GetProperty("guid").GetString() == b).GetProperty("name").GetString() + " / PickerB";
        Click("Scene cutscenes");
        WaitForWidget("Duplicate / PickerA", "Timeline");
        WaitForWidget(bLabel, "Timeline");
        Click("Duplicate / PickerA");
        Assert.That(State().GetProperty("director").GetString(), Is.EqualTo(a));
        Call("entity.delete", new { entity = a });
        Call("input.wait", new { frames = 5 });
        Assert.That(State().TryGetProperty("director", out var deletedDirector) && deletedDirector.ValueKind != JsonValueKind.Null, Is.False);
        Assert.That(Call("ui.find", new { label = "Duplicate / PickerA", window = "Timeline", exact = true }).GetArrayLength(), Is.Zero);
        WaitForWidget(bLabel, "Timeline");
        Call("scene.save");
        Call("scene.create", new { name = "OtherTimelineScene", force = true });
        Owner("Other owner", "OtherSequence");
        Call("scene.save");
        Call("entity.deselect");
        WaitForWidget("Other owner / OtherSequence", "Timeline");
        Assert.That(Call("ui.find", new { label = bLabel, window = "Timeline", exact = true }).GetArrayLength(), Is.Zero);
        Click("Other owner / OtherSequence");
        SetLength(7);
        try { Call("scene.load", new { name = "TimelineUiScene", force = true }); } catch (Voltage.Cli.CliException) { }
        Prompt("Cancel");
        Assert.That(Call("status").GetProperty("scene").GetProperty("name").GetString(), Is.EqualTo("OtherTimelineScene"));
        Assert.That(State().GetProperty("dirty").GetBoolean(), Is.True);
        try { Call("scene.load", new { name = "TimelineUiScene", force = true }); } catch (Voltage.Cli.CliException) { }
        Prompt("Discard");
        Call("input.wait", new { frames = 15 });
        Assert.That(Call("status").GetProperty("scene").GetProperty("name").GetString(), Is.EqualTo("TimelineUiScene"));
        Call("entity.deselect");
        WaitForWidget(bLabel, "Timeline");
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "timeline-scene-picker.png");
        Call("screenshot", new { path });
        TestContext.AddTestAttachment(path, "Scene-only cutscene picker in the real editor");
    }

    private static string Owner(string name, string asset)
    {
        Call("timeline.create", new { name = asset });
        var owner = Call("entity.create", new { name }).GetProperty("guid").GetString();
        Call("component.add", new { entity = owner, type = "TimelineDirector" });
        Call("component.set", new { entity = owner, type = "TimelineDirector", member = "Timeline", value = "Data/Timelines/" + asset + ".vtimeline" });
        return owner;
    }

    private static JsonElement State() => Call("timeline.editor.state");
    private static void Click(string label) { WaitForWidget(label, "Timeline"); Call("ui.click", new { label, window = "Timeline", exact = true }); Call("input.wait", new { frames = 5 }); }
    private static void Prompt(string label) { WaitForWidget(label, "Unsaved timeline changes"); Call("ui.click", new { label, window = "Unsaved timeline changes", exact = true }); Call("input.wait", new { frames = 5 }); }
    private static void SetLength(float value)
    {
        Click("Length");
        Call("input.key", new { key = "a", modifiers = new[] { "ctrl" } });
        Call("input.type", new { text = value.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        Call("input.key", new { key = "Enter" });
        Call("input.wait", new { frames = 5 });
        Assert.That(State().GetProperty("duration").GetSingle(), Is.EqualTo(value));
    }
    private static float ReadDuration(string name)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(ProjectFile), "Data/Timelines", name + ".vtimeline")));
        return json.RootElement.GetProperty("Duration").GetSingle();
    }
    private static void Search(string text)
    {
        Call("ui.focus", new { window = "Asset Browser" });
        Call("ui.click", new { label = "AssetSearch", window = "Asset Browser" });
        Call("input.key", new { key = "a", modifiers = new[] { "ctrl" } });
        Call("input.type", new { text });
        Call("input.wait", new { frames = 5 });
    }
    private static void UndockTimeline()
    {
        var bounds = Call("ui.windows").EnumerateArray().Single(w => w.GetProperty("name").GetString() == "Timeline");
        if (bounds.GetProperty("docked").GetBoolean())
            Call("input.drag", new { x1 = bounds.GetProperty("x").GetInt32() + 400, y1 = bounds.GetProperty("y").GetInt32() + 18, x2 = 750, y2 = 200, frames = 20 });
        Call("ui.focus", new { window = "Timeline" });
        bounds = Call("ui.windows").EnumerateArray().Single(w => w.GetProperty("name").GetString() == "Timeline");
        Call("input.drag", new { x1 = bounds.GetProperty("x").GetInt32() + 100, y1 = bounds.GetProperty("y").GetInt32() + 15, x2 = 600, y2 = 10, frames = 20 });
        bounds = Call("ui.windows").EnumerateArray().Single(w => w.GetProperty("name").GetString() == "Timeline");
        Call("input.drag", new { x1 = bounds.GetProperty("x").GetInt32() + bounds.GetProperty("width").GetInt32() - 2, y1 = bounds.GetProperty("y").GetInt32() + bounds.GetProperty("height").GetInt32() - 2,
            x2 = bounds.GetProperty("x").GetInt32() + 820, y2 = bounds.GetProperty("y").GetInt32() + 850, frames = 15 });
    }
    private static void Drag(JsonElement source, JsonElement target) => Call("input.drag", new {
        x1 = source.GetProperty("x").GetInt32() + 50, y1 = source.GetProperty("y").GetInt32() + source.GetProperty("height").GetInt32() / 2,
        x2 = target.GetProperty("x").GetInt32() + 50, y2 = target.GetProperty("y").GetInt32() + target.GetProperty("height").GetInt32() / 2, frames = 20 });
}
