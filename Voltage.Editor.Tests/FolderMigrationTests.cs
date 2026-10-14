using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class FolderMigrationTests
{
    [Test, Explicit("Requires an isolated copy of Jolt in VOLTAGE_MIGRATION_PROJECT.")]
    public void Copied_Jolt_loads_and_plays_with_root_native_folders()
    {
        var descriptor = Environment.GetEnvironmentVariable("VOLTAGE_MIGRATION_PROJECT");
        Assert.That(descriptor, Is.Not.Null);
        var root = Path.GetDirectoryName(Path.GetFullPath(descriptor));
        Assert.That(root.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase), Is.True, "Only migration snapshots may be tested here.");
        File.Delete(Path.Combine(root, "Scenes/MigrationProbe.vscene"));
        File.Delete(Path.Combine(root, "Scenes/MigrationProbe.vscene.meta"));
        var metas = Directory.GetFiles(Path.Combine(root, "Scenes"), "*.meta", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(root, "Prefabs"), "*.meta", SearchOption.AllDirectories))
            .ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        try
        {
            Call("project.load", new { path = descriptor, force = true });
            Call("input.wait", new { frames = 5 });
            var compiled = Call("scripts.compile", new { reloadScene = false });
            Assert.That(compiled.GetProperty("success").GetBoolean(), Is.True, compiled.ToString());
            Call("asset.refresh");
            var scenes = Call("scene.list");
            Assert.That(scenes.GetArrayLength(), Is.EqualTo(Directory.GetFiles(Path.Combine(root, "Scenes"), "*.vscene", SearchOption.AllDirectories).Length));
            foreach (var scene in scenes.EnumerateArray())
            {
                var path = scene.GetProperty("path").GetString();
                Call("scene.load", new { name = path, force = true });
                Assert.That(Call("status").GetProperty("scene").GetProperty("path").GetString(), Is.EqualTo(path));
            }
            Call("data.set", new { asset = "Data/Player/PlayerData.vasset", member = "DebugMenuValues.EnemyPrefabFolder", value = "Prefabs" });
            Call("scene.load", new { name = "Testing Scene", force = true });
            Call("scenecomponent.set", new { type = "TestingDebugMenu", member = "EnemyPrefabFolder", value = "Prefabs" });
            Call("scene.save");
            foreach (var name in new[] { "MainMenu", "A1_S1", "Testing Scene" })
            {
                Call("scene.load", new { name, force = true });
                Call("play.start");
                Call("input.wait", new { frames = 45 });
                Assert.That(Call("status").GetProperty("editMode").GetBoolean(), Is.False);
                Call("play.stop");
            }
            Call("scene.create", new { name = "MigrationProbe", force = true });
            var prefabs = Call("prefab.list");
            foreach (var prefab in prefabs.EnumerateArray())
            {
                var guid = prefab.GetProperty("guid").GetString();
                var placed = Call("prefab.instantiate", new { asset = guid });
                Assert.That(Call("prefab.info", new { entity = placed.GetProperty("guid").GetString() }).GetProperty("guid").GetString(), Is.EqualTo(guid));
            }
            foreach (var meta in metas) Assert.That(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(meta.Key))), Is.EqualTo(meta.Value), meta.Key);
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Data/assets.manifest")));
            foreach (var entry in manifest.RootElement.EnumerateObject())
                if (entry.Value.GetString().EndsWith(".vscene") || entry.Value.GetString().EndsWith(".vprefab"))
                    Assert.That(File.Exists(Path.Combine(root, entry.Value.GetString())), Is.True, entry.Value.GetString());
            var exported = Call("build.game", new { platform = "win-x64", debug = true, aot = false, compileAssets = false });
            Assert.That(exported.GetProperty("success").GetBoolean(), Is.True, exported.ToString());
            Assert.That(File.Exists(Path.Combine(root, "bin/Debug/win-x64/Scenes/A1_S1.vscene")), Is.True);
            Assert.That(Directory.GetFiles(Path.Combine(root, "bin/Debug/win-x64/Prefabs"), "*.vprefab", SearchOption.AllDirectories).Length, Is.EqualTo(prefabs.GetArrayLength()));
            TestContext.Progress.WriteLine($"Migration snapshot: {scenes.GetArrayLength()} scenes, {prefabs.GetArrayLength()} prefabs, three Play checks and a Debug export passed.");
        }
        finally
        {
            Call("play.stop");
            Call("project.load", new { path = ProjectFile, force = true });
        }
    }
}
