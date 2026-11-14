using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class ScriptAndPlayModeFixTests
{
    private const string Probe = "Fix probe";
    private const string Asset = "Data/FixSettings.vasset";
    private static string ScriptPath => Path.Combine(Path.GetDirectoryName(ProjectFile), "Scripts", "FixProbe.cs");

    private const string ProbeSource = """
        using Voltage;
        using Voltage.Console;
        using Voltage.Data;
        using Voltage.Serialization;
        public partial class FixSettings : DataAsset { public int Value = 5; public int Saved = 1; }
        public partial class FixProbe : Component, IUpdatable
        {
            public AssetReference Settings;
            public bool Run;
            public int OldName;
            public void Update()
            {
                if (!Run || Core.IsEditMode) return;
                Run = false;
                DataAssets.Load<FixSettings>(Settings).Value = 99;
            }
        }
        public static class FixCommands { [Command("fix-probe", "test command")] public static void Probe() { } }
        """;

    [SetUp]
    public void CreateProbe()
    {
        Call("scene.load", new { name = "TestScene", force = true });
        File.WriteAllText(ScriptPath, ProbeSource);
        Assert.That(Call("scripts.compile").GetProperty("success").GetBoolean(), Is.True);
        if (!File.Exists(Path.Combine(Path.GetDirectoryName(ProjectFile), Asset)))
            Call("data.create", new { type = "FixSettings", path = Asset });
        Call("entity.create", new { name = Probe });
        Call("component.add", new { entity = Probe, type = "FixProbe" });
        Set("Settings", Asset);
        Call("scene.save");
    }

    [TearDown]
    public void RemoveProbe()
    {
        Call("play.stop");
        Call("scene.load", new { name = "TestScene", force = true });
        Call("entity.delete", new { entity = Probe });
        Call("scene.save");
        File.Delete(ScriptPath);
        Thread.Sleep(900); // Let the watcher's compile and reload for the deletion finish before the next fixture.
        Call("scripts.compile");
    }

    [Test]
    public void Play_mode_changes_to_a_data_asset_revert_on_stop_but_a_save_during_play_sticks()
    {
        Call("play.start");
        Set("Run", true);
        WaitFor(() => Data("Value") == 99, "the probe never changed the asset");
        Call("play.stop");
        WaitFor(() => Data("Value") == 5, "the Play-mode value survived Stop");

        Call("play.start");
        Call("data.set", new { asset = Asset, member = "Saved", value = 2 });
        Call("play.stop");
        Thread.Sleep(300);
        Assert.That(Data("Saved"), Is.EqualTo(2));
        Call("data.set", new { asset = Asset, member = "Saved", value = 1 });
    }

    [Test]
    public void Console_commands_in_scripts_follow_each_compile()
    {
        Assert.That(Commands(), Does.Contain("fix-probe"));

        File.WriteAllText(ScriptPath, ProbeSource.Replace("\"fix-probe\"", "\"fix-probe-renamed\""));
        Assert.That(Call("scripts.compile").GetProperty("success").GetBoolean(), Is.True);
        Assert.That(Commands(), Does.Contain("fix-probe-renamed").And.Not.Contain("fix-probe"));
    }

    [Test]
    public void A_renamed_field_with_DecodeAlias_keeps_its_saved_value()
    {
        Set("OldName", 7);
        Call("scene.save");

        File.WriteAllText(ScriptPath, File.ReadAllText(ScriptPath)
            .Replace("public int OldName;", "[Voltage.Persistence.DecodeAlias(\"OldName\")] public int NewName;"));
        Assert.That(Call("scripts.compile", new { reloadScene = true }).GetProperty("success").GetBoolean(), Is.True);
        WaitFor(() => Values().TryGetProperty("NewName", out var v) && v.GetInt32() == 7, "NewName did not read the value saved as OldName");
    }

    [Test]
    public void Compile_and_reload_reloads_with_hot_reload_off()
    {
        Call("ui.click", new { path = "Scripting/Enable Hot Reload" });
        try
        {
            Set("OldName", 3); // Unsaved, so only a reload from the file puts it back to 0.
            Call("ui.click", new { path = "Scripting/Compile & Reload Scene" });
            WaitFor(() => Values().GetProperty("OldName").GetInt32() == 0, "the scene did not reload");
        }
        finally
        {
            Call("ui.click", new { path = "Scripting/Enable Hot Reload" });
        }
    }

    private static void Set(string member, object value) =>
        Call("component.set", new { entity = Probe, type = "FixProbe", member, value });

    private static JsonElement Values() =>
        Call("component.get", new { entity = Probe, type = "FixProbe" }).GetProperty("values");

    private static int Data(string member) =>
        Call("data.get", new { asset = Asset }).GetProperty("values").GetProperty(member).GetInt32();

    private static string[] Commands() =>
        Call("console.commands").EnumerateArray().Select(e => e.GetString()).ToArray();

    private static void WaitFor(Func<bool> condition, string failure)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            try { if (condition()) return; }
            catch (Exception) { /* The entity is briefly missing while the scene reloads. */ }
            Thread.Sleep(50);
        }
        Assert.Fail(failure);
    }
}
