using System;
using System.IO;
using System.Threading;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class DeferredCompilationTests
{
    [Test]
    public void Play_and_pause_queue_the_latest_scripts_until_stop_then_edit_saves_reload()
    {
        Call("scene.load", new { name = "TestScene", force = true });
        var path = Path.Combine(Path.GetDirectoryName(ProjectFile), "Scripts", "DeferredCompileProbe.cs");
        File.WriteAllText(path, """
            using Voltage;
            using Voltage.Data;
            using Voltage.Serialization;
            public partial class DeferredSettings : DataAsset { public int Threshold = 5; }
            public partial class DeferredCompileProbe : Component, IUpdatable
            {
                public AssetReference Settings;
                public bool Run;
                public string Result = "Waiting";
                public int SourceVersion;
                public override void OnStart() { SourceVersion = 1; }
                public void Update()
                {
                    if (!Run || Core.IsEditMode) return;
                    Run = false;
                    var settings = DataAssets.Load<DeferredSettings>(Settings);
                    Result = settings?.Threshold == 5 ? "Passed" : "Null or invalid settings";
                }
            }
            """);
        try
        {
            Assert.That(Call("scripts.compile").GetProperty("success").GetBoolean(), Is.True);
            Call("data.create", new { type = "DeferredSettings", path = "Data/DeferredSettings.vasset" });
            Call("entity.create", new { name = "Deferred compile probe" });
            Call("component.add", new { entity = "Deferred compile probe", type = "DeferredCompileProbe" });
            Set("Settings", "Data/DeferredSettings.vasset");
            Call("scene.save");
            Call("play.start");
            ChangeVersion(path, 1, 2);
            var queued = Call("scripts.compile", new { reloadScene = true });
            Assert.That(queued.GetProperty("deferred").GetBoolean(), Is.True);
            Assert.That(queued.GetProperty("success").GetBoolean(), Is.False);
            Thread.Sleep(900); // Longer than the file watcher's debounce.
            AssertVersion(1);
            Call("play.pause", new { paused = true });
            ChangeVersion(path, 2, 3);
            Assert.That(Call("scripts.compile").GetProperty("deferred").GetBoolean(), Is.True);
            Thread.Sleep(900);
            AssertVersion(1);
            Call("play.pause", new { paused = false });
            RunProbe(); // The old, still-running component lazily loads its tuning after both saves.
            Call("play.stop");
            WaitForVersion(3);
            Call("play.start");
            RunProbe();
            Call("play.stop");
            ChangeVersion(path, 3, 4);
            WaitForVersion(4); // Edit mode retains automatic compile and scene reload.
            Call("play.start");
            RunProbe();
            ChangeVersion(path, 4, 5); // File saves alone must also queue, without a manual compile request.
            Thread.Sleep(900);
            AssertVersion(4);
            Call("play.stop");
            WaitForVersion(5);
            Call("play.start");
            RunProbe();
        }
        finally
        {
            Call("play.stop");
            Call("scene.load", new { name = "TestScene", force = true });
            Call("entity.delete", new { entity = "Deferred compile probe" });
            Call("scene.save");
            File.Delete(path);
            Thread.Sleep(900); // Let the watcher's compile and reload for the deletion finish before the next fixture.
            Call("scripts.compile");
        }
    }

    private static void ChangeVersion(string path, int before, int after) =>
        File.WriteAllText(path, File.ReadAllText(path).Replace($"SourceVersion = {before}", $"SourceVersion = {after}"));

    private static void Set(string member, object value) =>
        Call("component.set", new { entity = "Deferred compile probe", type = "DeferredCompileProbe", member, value });

    private static int Version() => Call("component.get", new { entity = "Deferred compile probe", type = "DeferredCompileProbe" })
        .GetProperty("values").GetProperty("SourceVersion").GetInt32();

    private static void AssertVersion(int expected) => Assert.That(Version(), Is.EqualTo(expected));

    private static void WaitForVersion(int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (Version() != expected && DateTime.UtcNow < deadline) Thread.Sleep(30);
        AssertVersion(expected);
    }

    private static void RunProbe()
    {
        Set("Result", "Waiting");
        Set("Run", true);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        string result;
        do
        {
            result = Call("component.get", new { entity = "Deferred compile probe", type = "DeferredCompileProbe" })
                .GetProperty("values").GetProperty("Result").GetString();
            if (result != "Waiting") break;
            Thread.Sleep(20);
        } while (DateTime.UtcNow < deadline);
        Assert.That(result, Is.EqualTo("Passed"));
    }
}
