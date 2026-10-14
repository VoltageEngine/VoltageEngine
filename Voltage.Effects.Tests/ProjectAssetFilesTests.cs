using NUnit.Framework;
using Voltage.Editor.Builders;
using Voltage.Project;

namespace Voltage.Effects.Tests;

[TestFixture]
public class ProjectAssetFilesTests
{
    private string _root;
    [SetUp] public void Setup() => _root = Path.Combine(Path.GetTempPath(), "VoltageAssetLocations-" + Guid.NewGuid().ToString("N"));
    [TearDown] public void Cleanup() => Directory.Delete(_root, true);

    private string Write(string relative)
    {
        var file = Path.GetFullPath(Path.Combine(_root, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllText(file, relative);
        return file;
    }

    [Test]
    public void Discovery_finds_native_files_anywhere_and_ignores_build_copies()
    {
        var rootScene = Write("Root.VSCENE");
        var scene = Write("World/Act 1/Room.vscene");
        var prefab = Write("Actors/Enemies/Robber.vprefab");
        foreach (var folder in new[] { "bin", "obj", "Build", ".git", "EngineLibs", "PluginLibs" }) Write(folder + "/Copy.vscene");
        Write("win-x64/Data/Scenes/Root.vscene");
        Write("win-x64/Game.runtimeconfig.json");
        Assert.That(ProjectAssetFiles.Find(_root, ".vscene"), Is.EquivalentTo(new[] { rootScene, scene }));
        Assert.That(ProjectAssetFiles.Find(_root, ".vprefab"), Is.EqualTo(new[] { prefab }));
        Assert.That(ProjectAssetFiles.PathComparer.Equals(ProjectAssetFiles.Resolve(_root, "Root", ".vscene"), rootScene), Is.True);
    }

    [Test]
    public void Exact_paths_keep_duplicate_names_distinct_and_ambiguous_short_names_fail()
    {
        var first = Write("World/Room.vscene");
        Write("Bonus/Room.vscene");
        Assert.That(ProjectAssetFiles.Resolve(_root, "World\\Room", ".vscene"), Is.EqualTo(first));
        Assert.Throws<InvalidOperationException>(() => ProjectAssetFiles.Resolve(_root, "Room", ".vscene"));
        Assert.Throws<ArgumentException>(() => ProjectAssetFiles.Resolve(_root, "../Room", ".vscene"));
    }

    [Test]
    public void Existing_folder_relative_keys_and_short_names_still_resolve()
    {
        var scene = Write("Data/Scenes/Act 1/Room.vscene");
        var prefab = Write("Custom/Actors/Enemy.vprefab");
        Assert.That(ProjectAssetFiles.Resolve(_root, "Act 1/Room", ".vscene", Path.Combine(_root, "Data/Scenes")), Is.EqualTo(scene));
        Assert.That(ProjectAssetFiles.Resolve(_root, "Enemy", ".vprefab"), Is.EqualTo(prefab));
        Assert.That(ProjectAssetFiles.Key(_root, prefab), Is.EqualTo("Custom/Actors/Enemy"));
    }

    [Test]
    public void Build_rules_accept_existing_keys_and_package_custom_locations_and_prefabs()
    {
        var main = Write("Data/Scenes/Main.vscene");
        Write("Data/Scenes/Excluded.vscene");
        var custom = Write("World/Room.vscene");
        var prefab = Write("Actors/Npcs/Actor.vprefab");
        File.WriteAllText(prefab + ".meta", "stable-guid");
        var settings = new ProjectSettings { InitialScene = "Main" };
        settings.BuildScenes.Add(new() { Scene = "Excluded", Release = false });
        var legacy = Path.Combine(_root, "Data/Scenes");
        var plan = SceneBuildSelection.Plan(_root, settings, false, legacy);
        Assert.That(plan, Is.EquivalentTo(new[] { "Data/Scenes/Main", "World/Room" }));
        var output = Path.Combine(_root, "bin/game");
        Directory.CreateDirectory(output);
        SceneBuildSelection.Package(_root, legacy, output, plan);
        Assert.That(File.Exists(Path.Combine(output, "World/Room.vscene")), Is.True);
        Assert.That(File.Exists(Path.Combine(output, "Data/Scenes/Excluded.vscene")), Is.False);
        Assert.That(File.ReadAllText(Path.Combine(output, "Actors/Npcs/Actor.vprefab.meta")), Is.EqualTo("stable-guid"));
        Assert.That(File.Exists(main) && File.Exists(custom), Is.True);
        Assert.That(ProjectAssetFiles.Find(_root, ".vscene"), Has.Count.EqualTo(3));
    }

    [Test]
    public void Project_root_can_also_be_the_default_scene_creation_folder()
    {
        Write("Main.vscene");
        var output = Path.Combine(_root, "bin/game");
        Directory.CreateDirectory(output);
        var plan = SceneBuildSelection.Plan(_root, new ProjectSettings { InitialScene = "Main" }, true, _root);
        SceneBuildSelection.Package(_root, _root, output, plan);
        Assert.That(File.Exists(Path.Combine(output, "Main.vscene")), Is.True);
    }
}
