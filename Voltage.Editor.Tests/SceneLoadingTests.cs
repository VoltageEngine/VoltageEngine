using System.Linq;
using System.IO;
using NUnit.Framework;
using Voltage.Cli;

namespace Voltage.Editor.Tests;

[TestFixture]
public sealed class SceneLoadingTests
{
    [Test]
    public void Completed_work_never_reports_ready_before_update_and_presentation()
    {
        var progress = new SceneLoadProgress();
        for (int phase = 0; phase < SceneLoadProgress.PhaseCount; phase++)
        {
            progress.Items(phase, 100, 100);
            progress.CompletePhase(phase);
            Assert.That(progress.Percent, Is.LessThan(100));
            Assert.That(progress.Ready, Is.False);
        }
        progress.MarkPresented();
        Assert.That(progress.Ready, Is.False);
        progress.MarkUpdated();
        progress.MarkPresented();
        Assert.That(progress.Percent, Is.EqualTo(100));
        Assert.That(progress.Ready, Is.True);
    }

    [Test]
    public void Dynamically_added_work_does_not_move_progress_backwards()
    {
        var progress = new SceneLoadProgress();
        progress.Items(6, 20, 20);
        int previous = progress.Percent;
        progress.Items(6, 1, 80);
        Assert.That(progress.Percent, Is.EqualTo(previous));
        progress.Items(6, 0, 0);
        Assert.That(progress.Percent, Is.EqualTo(previous));
    }

    [Test]
    public void Failed_load_retains_error_and_cannot_complete()
    {
        var progress = new SceneLoadProgress();
        progress.Items(4, 5, 10);
        progress.Fail("Invalid scene data");
        progress.MarkUpdated();
        progress.MarkPresented();
        Assert.That(progress.Error, Is.EqualTo("Invalid scene data"));
        Assert.That(progress.Ready, Is.False);
        Assert.That(progress.Percent, Is.LessThan(100));
    }

    [Test]
    public void Loading_commands_are_available_through_the_existing_gateway()
    {
        var commands = EditorSession.Call("commands");
        var names = commands.EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToArray();
        Assert.That(names, Does.Contain("loading.status"));
        Assert.That(names, Does.Contain("loading.history"));
        Assert.That(EditorSession.Call("loading.status").GetProperty("progress").GetProperty("totalPhases").GetInt32(), Is.EqualTo(8));
    }

    [Test]
    public void Truncated_scene_json_is_rejected_without_replacing_the_active_scene()
    {
        var before = EditorSession.Call("status").GetProperty("scene").GetProperty("path").GetString();
        var path = Path.Combine(Path.GetDirectoryName(EditorSession.ProjectFile), "Scenes", "TruncatedLoad.vscene");
        File.WriteAllText(path, "{\"Name\":\"unterminated");
        try
        {
            Assert.Throws<CliException>(() => EditorSession.Call("scene.load", new { path, force = true }));
            Assert.That(EditorSession.Call("status").GetProperty("scene").GetProperty("path").GetString(), Is.EqualTo(before));
        }
        finally { File.Delete(path); }
    }
}
