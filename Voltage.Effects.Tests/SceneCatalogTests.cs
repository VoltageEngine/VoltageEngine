using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Voltage.Editor.Builders;
using Voltage.Project;
using Voltage.Serialization;

namespace Voltage.Effects.Tests;

[TestFixture]
public class SceneCatalogTests
{
	private string _root, _scenes, _output;

	[SetUp]
	public void Setup()
	{
		_root = Path.Combine(Path.GetTempPath(), "VoltageScenes-" + Guid.NewGuid().ToString("N"));
		_scenes = Path.Combine(_root, "Data", "Scenes");
		_output = Path.Combine(_root, "bin", "game");
		Directory.CreateDirectory(_output);
		foreach (var key in new[] { "Main", "Act 1/Room", "Act 1/Lower/Room", "Act 2/Room" })
		{
			var path = SceneCatalog.Resolve(_scenes, key);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, key);
			File.WriteAllText(path + ".meta", key + "-meta");
		}
	}

	[TearDown]
	public void Cleanup() => Directory.Delete(_root, true);

	[Test]
	public void Folders_keep_nested_paths_and_duplicate_leaf_names()
	{
		var catalog = SceneCatalog.Read(_scenes);
		Assert.That(catalog.Folders.Select(f => f.Name), Is.EqualTo(new[] { "Act 1", "Act 2" }));
		Assert.That(catalog.Scenes, Is.EqualTo(new[] { "Main" }));
		Assert.That(catalog.Folders[0].Scenes, Is.EqualTo(new[] { "Act 1/Room" }));
		Assert.That(catalog.Folders[0].Folders[0].Scenes, Is.EqualTo(new[] { "Act 1/Lower/Room" }));
		Assert.That(catalog.AllScenes(), Is.EqualTo(new[] { "Act 1/Lower/Room", "Act 1/Room", "Act 2/Room", "Main" }));
	}

	[Test]
	public void Keys_accept_either_separator_and_optional_extension()
	{
		Assert.That(SceneCatalog.Resolve(_scenes, "Act 1\\Lower\\Room.vscene"), Is.EqualTo(SceneCatalog.Resolve(_scenes, "Act 1/Lower/Room")));
		Assert.That(SceneCatalog.Read(Path.Combine(_root, "missing")).AllScenes(), Is.Empty);
	}

	[TestCase("")]
	[TestCase("../Main")]
	[TestCase("Act 1/../Main")]
	[TestCase("/Main")]
	[TestCase("C:/Main")]
	[TestCase("Act 1//Main")]
	public void Scene_keys_cannot_escape_the_scene_directory(string key) =>
		Assert.Throws<ArgumentException>(() => SceneCatalog.Resolve(_scenes, key));

	[Test]
	public void Old_settings_and_new_scenes_default_to_included()
	{
		var settings = AotDeserializers.DeserializeProjectSettings("{\"InitialScene\":\"Main\"}");
		Assert.That(SceneBuildSelection.Plan(_scenes, settings, true).Length, Is.EqualTo(4));
		Assert.That(SceneBuildSelection.Plan(_scenes, settings, false).Length, Is.EqualTo(4));
		Assert.That(SceneCatalog.Included("New/Scene", null, false), Is.True);
	}

	[Test]
	public void Build_flags_round_trip_and_apply_per_configuration()
	{
		var settings = AotDeserializers.DeserializeProjectSettings("{\"InitialScene\":\"Main\",\"BuildScenes\":[{\"Scene\":\"Act 1/Room\",\"Release\":false},{\"Scene\":\"Act 2/Room\",\"Debug\":false,\"Release\":false}]}");
		settings = AotDeserializers.DeserializeProjectSettings(Voltage.Persistence.Json.ToJson(settings));
		Assert.That(SceneBuildSelection.Plan(_scenes, settings, true), Does.Contain("Act 1/Room").And.Not.Contain("Act 2/Room"));
		Assert.That(SceneBuildSelection.Plan(_scenes, settings, false), Does.Not.Contain("Act 1/Room").And.Not.Contain("Act 2/Room"));
	}

	[TestCase(true)]
	[TestCase(false)]
	public void Excluded_or_missing_startup_scene_blocks_the_build(bool debug)
	{
		var settings = new ProjectSettings { InitialScene = "missing" };
		Assert.Throws<InvalidOperationException>(() => SceneBuildSelection.Plan(_scenes, settings, debug));
		settings.InitialScene = "Main";
		settings.BuildScenes.Add(new() { Scene = "Main", Debug = false, Release = false });
		Assert.Throws<InvalidOperationException>(() => SceneBuildSelection.Plan(_scenes, settings, debug));
	}

	[Test]
	public void Package_filters_publish_copies_and_manifest_without_removing_other_assets()
	{
		var data = Path.Combine(_output, "Data");
		Directory.CreateDirectory(data);
		File.WriteAllText(Path.Combine(data, "old.vscene"), "stale publish output");
		File.WriteAllText(Path.Combine(data, "old.vscene.meta"), "old-meta");
		File.WriteAllText(Path.Combine(data, "keep.vprefab"), "prefab");
		File.WriteAllText(Path.Combine(data, "assets.manifest"), "{\"keep\":\"Data/Scenes/Act 1/Room.vscene\",\"drop\":\"Data/Scenes/Act 2/Room.vscene\",\"prefab\":\"Data/keep.vprefab\"}");
		SceneBuildSelection.Package(_root, _scenes, _output, new[] { "Main", "Act 1/Room" });
		Assert.That(SceneCatalog.Read(Path.Combine(data, "Scenes")).AllScenes(), Is.EqualTo(new[] { "Act 1/Room", "Main" }));
		Assert.That(File.ReadAllText(SceneCatalog.Resolve(Path.Combine(data, "Scenes"), "Act 1/Room")), Is.EqualTo("Act 1/Room"));
		Assert.That(File.Exists(Path.Combine(data, "old.vscene.meta")), Is.False);
		Assert.That(File.Exists(Path.Combine(data, "keep.vprefab")), Is.True);
		var manifest = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(data, "assets.manifest")));
		Assert.That(manifest.Keys, Is.EquivalentTo(new[] { "keep", "prefab" }));
		Assert.That(File.Exists(SceneCatalog.Resolve(_scenes, "Act 2/Room")), Is.True, "Packaging changed the source project");
	}

	[Test]
	public void Packaging_rejects_source_output_overlap_and_invalid_keys_before_deleting()
	{
		Assert.Throws<InvalidOperationException>(() => SceneBuildSelection.Package(_root, _scenes, _scenes, new[] { "Main" }));
		var stale = Path.Combine(_output, "stale.vscene");
		File.WriteAllText(stale, "keep until a valid plan exists");
		Assert.Throws<ArgumentException>(() => SceneBuildSelection.Package(_root, _scenes, _output, new[] { "../Main" }));
		Assert.That(File.Exists(stale), Is.True);
	}
}
