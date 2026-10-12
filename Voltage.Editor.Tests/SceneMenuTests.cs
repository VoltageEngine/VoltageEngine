using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class SceneMenuTests
{
	private static readonly string[] Keys = { "SceneMenuTests/Act 1/Lower/Shared", "SceneMenuTests/Act 1/Shared", "SceneMenuTests/Act 2/Shared" };
	private string ProjectRoot => Path.GetDirectoryName(ProjectFile);

	[SetUp]
	public void CreateNestedScenes()
	{
		var source = Path.Combine(ProjectRoot, "Data", "Scenes", "TestScene.vscene");
		foreach (var key in Keys)
		{
			var target = Path.Combine(ProjectRoot, "Data", "Scenes", key + ".vscene");
			Directory.CreateDirectory(Path.GetDirectoryName(target));
			File.Copy(source, target, true);
		}
	}

	[TearDown]
	public void Restore()
	{
		Call("input.release");
		if (Call("ui.find", new { label = "Select All", window = "Build Game", exact = true }).GetArrayLength() > 0)
		{
			Click("Select All"); Click("Include selected"); CloseBuild();
		}
		Call("scene.load", new { name = "TestScene", force = true });
		Directory.Delete(Path.Combine(ProjectRoot, "Data", "Scenes", "SceneMenuTests"), true);
	}

	[Test]
	public void Load_scene_menu_walks_folders_and_loads_the_correct_duplicate_name()
	{
		foreach (var key in new[] { Keys[0], Keys[2] })
		{
			Call("ui.click", new { path = "Project/Load Scene.../" + key });
			var deadline = DateTime.UtcNow.AddSeconds(10);
			while (!Call("status").GetProperty("scene").GetProperty("path").GetString().Replace('\\', '/').EndsWith(key + ".vscene", StringComparison.Ordinal))
			{
				if (DateTime.UtcNow > deadline) Assert.Fail("Menu loaded the wrong scene path");
				Thread.Sleep(100);
			}
		}
	}

	[Test]
	public void Control_click_toggles_rows_and_bulk_debug_only_persists_after_reopen()
	{
		OpenBuild(); Click("Clear Selection"); Click(Keys[0]); ModifiedClick(Keys[2], "LeftControl");
		Click("Debug only");
		AssertRules(new[] { Keys[0], Keys[2] }, true, false);
		CloseBuild(); OpenBuild();
		AssertRules(new[] { Keys[0], Keys[2] }, true, false);
		Click("Select All"); Click("Include selected");
		Click("Clear Selection"); Click(Keys[0]); ModifiedClick(Keys[1], "LeftControl"); ModifiedClick(Keys[0], "LeftControl");
		Click("Release only");
		AssertRules(new[] { Keys[1] }, false, true);
	}

	[Test]
	public void Shift_click_selects_a_reverse_range_and_select_all_restores_defaults()
	{
		OpenBuild(); Click("Clear Selection"); Click(Keys[2]); ModifiedClick(Keys[0], "LeftShift"); Click("Exclude selected");
		AssertRules(Keys, false, false);
		Click("Select All"); Click("Include selected");
		Assert.That(ReadRules().GetArrayLength(), Is.Zero);
	}

	[Test]
	public void Per_scene_checkboxes_save_independent_configuration_flags()
	{
		OpenBuild();
		Click("Release:" + Keys[1]);
		AssertRules(new[] { Keys[1] }, true, false);
		Click("Debug:" + Keys[1]);
		AssertRules(new[] { Keys[1] }, false, false);
		Click("Debug:" + Keys[1]); Click("Release:" + Keys[1]);
		Assert.That(ReadRules().GetArrayLength(), Is.Zero);
	}

	[Test]
	public void Debug_and_release_exports_contain_only_their_selected_scenes()
	{
		OpenBuild(); Click("Clear Selection"); Click(Keys[0]); Click("Debug only");
		Click(Keys[2]); Click("Exclude selected"); CloseBuild();
		foreach (var debug in new[] { true, false })
		{
			var result = Call("build.game", new { platform = "win-x64", debug, aot = false, compileAssets = false });
			Assert.That(result.GetProperty("success").GetBoolean(), Is.True, result.ToString());
			var scenes = Path.Combine(ProjectRoot, "bin", debug ? "Debug" : "Release", "win-x64", "Data", "Scenes");
			Assert.That(File.Exists(Path.Combine(scenes, Keys[0] + ".vscene")), Is.EqualTo(debug));
			Assert.That(File.Exists(Path.Combine(scenes, Keys[1] + ".vscene")), Is.True);
			Assert.That(File.Exists(Path.Combine(scenes, Keys[2] + ".vscene")), Is.False);
			Assert.That(File.Exists(Path.Combine(scenes, "MainScene.vscene")), Is.True);
		}
		OpenBuild();
	}

	private static void ModifiedClick(string key, string modifier)
	{
		Call("input.key", new { key = modifier, action = "down" });
		try { Click(key); } finally { Call("input.release"); }
	}

	private static void Click(string label)
	{
		WaitForWidget(label, "Build Game");
		Call("ui.click", new { label, window = "Build Game", exact = true });
	}

	private static void OpenBuild()
	{
		Call("ui.click", new { path = "Build/Scenes in Build" });
		WaitForWidget("Select All", "Build Game");
	}

	private static void CloseBuild()
	{
		var row = WaitForWidget("Select All", "Build Game");
		Call("input.scroll", new { x = row.GetProperty("x").GetInt32() + 350, y = row.GetProperty("y").GetInt32(), notches = -30 });
		Click("Cancel");
	}

	private JsonElement ReadRules()
	{
		using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectRoot, "ProjectSettings.json")));
		return doc.RootElement.GetProperty("BuildScenes").Clone();
	}

	private void AssertRules(string[] keys, bool debug, bool release)
	{
		var rules = ReadRules().EnumerateArray().ToArray();
		Assert.That(rules.Select(r => r.GetProperty("Scene").GetString()), Is.EquivalentTo(keys));
		Assert.That(rules.All(r => r.GetProperty("Debug").GetBoolean() == debug && r.GetProperty("Release").GetBoolean() == release), Is.True);
	}
}
