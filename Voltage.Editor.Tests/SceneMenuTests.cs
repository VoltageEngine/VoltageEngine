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
	private static readonly string[] Keys = { "Scenes/SceneMenuTests/Act 1/Lower/Shared", "Scenes/SceneMenuTests/Act 1/Shared", "Scenes/SceneMenuTests/Act 2/Shared" };
	private string ProjectRoot => Path.GetDirectoryName(ProjectFile);

	[SetUp]
	public void CreateNestedScenes()
	{
		var source = Path.Combine(ProjectRoot, "Scenes", "TestScene.vscene");
		foreach (var key in Keys)
		{
			var target = Path.Combine(ProjectRoot, key + ".vscene");
			Directory.CreateDirectory(Path.GetDirectoryName(target));
			File.Copy(source, target, true);
		}
	}

	[TearDown]
	public void Restore()
	{
		Call("input.release");
		if (Call("ui.find", new { label = "Cancel", window = "load-scene-popup", exact = true }).GetArrayLength() > 0)
			Call("ui.click", new { label = "Cancel", window = "load-scene-popup", exact = true });
		if (Call("ui.find", new { label = Keys[0], window = "Build Game", exact = true }).GetArrayLength() > 0)
		{
			Click("Select All"); Click("Include selected"); CloseBuild();
		}
		Call("scene.load", new { name = "TestScene", force = true });
		Directory.Delete(Path.Combine(ProjectRoot, "Scenes", "SceneMenuTests"), true);
	}

	[Test]
	public void Load_scene_button_opens_a_filtered_browser_and_loads_the_correct_duplicate_name()
	{
		foreach (var key in new[] { Keys[0], Keys[2] })
		{
			OpenScenePickerAtRoot();
            foreach (var folder in key.Split('/').SkipLast(1))
            {
                ScrollPickerTo(folder + "/");
                Call("ui.click", new { label = folder + "/", window = "load-scene-popup", exact = true });
                Call("input.wait", new { frames = 3 });
            }
            WaitForWidget("Shared.vscene", "load-scene-popup");
            Assert.That(Call("ui.find", new { label = "ProjectSettings.json", window = "load-scene-popup", exact = true }).GetArrayLength(), Is.Zero);
            Call("ui.click", new { label = "Shared.vscene", window = "load-scene-popup", exact = true });
            WaitForWidget("Open", "load-scene-popup");
            Call("ui.click", new { label = "Open", window = "load-scene-popup", exact = true });
			var deadline = DateTime.UtcNow.AddSeconds(10);
			while (!Call("status").GetProperty("scene").GetProperty("path").GetString().Replace('\\', '/').EndsWith(key + ".vscene", StringComparison.Ordinal))
			{
				if (DateTime.UtcNow > deadline) Assert.Fail("Menu loaded the wrong scene path");
				Thread.Sleep(100);
			}
		}
	}

    [Test]
    public void New_scene_button_opens_the_creation_prompt_without_a_submenu()
    {
        Call("ui.click", new { path = "Project/New Scene" });
        WaitForWidget("SceneName", "create-scene-popup");
        Assert.That(Call("ui.find", new { label = "SceneFolder", window = "create-scene-popup" }).GetArrayLength(), Is.EqualTo(1));
        WaitForWidget("Cancel", "create-scene-popup");
        Call("ui.click", new { label = "Cancel", window = "create-scene-popup", exact = true });
    }

    internal static void OpenScenePickerAtRoot()
    {
        OpenScenePicker();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (Call("ui.find", new { label = "Scenes/", window = "load-scene-popup", exact = true, visibleOnly = false }).GetArrayLength() > 0) return;
            ScrollPickerTo("../");
            Call("ui.click", new { label = "../", window = "load-scene-popup", exact = true });
            Call("input.wait", new { frames = 3 });
        }
        Assert.Fail("File picker did not return to the test project root");
    }

    internal static void HoverLoadSceneMenu()
    {
        if (Call("ui.find", new { label = "Load Scene", exact = true }).GetArrayLength() == 0)
            Call("ui.click", new { path = "Project" });
        WaitForWidget("Load Scene", "");
        Call("ui.hover", new { label = "Load Scene", exact = true, frames = 30 });
        WaitForWidget("Select File", "");
    }

    internal static void OpenScenePicker()
    {
        HoverLoadSceneMenu();
        Call("ui.click", new { label = "Select File", exact = true });
        WaitForWidget("Cancel", "load-scene-popup");
    }

    internal static void ScrollPickerTo(string label)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            if (Call("ui.find", new { label, window = "load-scene-popup", exact = true }).GetArrayLength() > 0) return;
            var row = Call("ui.find", new { label = "/", kind = "selectable", window = "load-scene-popup" })[0];
            var target = Call("ui.find", new { label, window = "load-scene-popup", exact = true, visibleOnly = false });
            var direction = target.GetArrayLength() > 0 && target[0].GetProperty("y").GetInt32() < row.GetProperty("y").GetInt32() ? 3 : -3;
            Call("input.scroll", new { x = row.GetProperty("x").GetInt32() + 30, y = row.GetProperty("y").GetInt32() + 15, notches = direction });
            Call("input.wait", new { frames = 2 });
        }
        WaitForWidget(label, "load-scene-popup");
    }

    [Test]
    public void Native_assets_outside_default_folders_are_indexed_loaded_and_keep_prefab_identity_when_moved()
    {
        var scene = Call("scene.create", new { name = "Outside", path = "Worlds/Chapter/Outside.vscene", force = true });
        var entity = Call("entity.create", new { name = "LocationTestActor" });
        var prefab = Call("prefab.create", new { entity = "LocationTestActor", path = "Actors/Npcs/LocationTestActor.vprefab", keepOriginal = false });
        var guid = prefab.GetProperty("guid").GetString();
        var actorGuid = prefab.GetProperty("instance").GetProperty("guid").GetString();
        Assert.That(Call("scene.list").EnumerateArray().Any(x => x.GetProperty("path").GetString().Replace('\\', '/').EndsWith("Worlds/Chapter/Outside.vscene")), Is.True);
        Assert.That(Call("prefab.list").EnumerateArray().Any(x => x.GetProperty("guid").GetString() == guid), Is.True);
        var moved = Path.GetFullPath(Path.Combine(ProjectRoot, "Worlds/Chapter/Moved.vprefab"));
        var source = Path.Combine(ProjectRoot, "Actors/Npcs/LocationTestActor.vprefab");
        Call("scene.save");
        File.Move(source, moved);
        File.Move(source + ".meta", moved + ".meta");
        Call("asset.refresh");
        Call("scene.reload");
        Assert.That(Call("prefab.list").EnumerateArray().Any(x => x.GetProperty("guid").GetString() == guid && x.GetProperty("path").GetString() == moved), Is.True);
        Assert.That(Call("prefab.info", new { entity = actorGuid }).GetProperty("guid").GetString(), Is.EqualTo(guid));
        Call("scene.load", new { name = "TestScene", force = true });
        Directory.Delete(Path.Combine(ProjectRoot, "Worlds"), true);
        Directory.Delete(Path.Combine(ProjectRoot, "Actors"), true);
        Call("asset.refresh");
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
		Assert.That(ReadRules().GetArrayLength(), Is.Zero, "include-all shortcut must restore both configurations");
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

	[TestCase("LeftControl"), TestCase("LeftWindows")]
	public void Checkboxes_apply_to_sparse_selected_rows_for_control_and_command(string modifier)
	{
		OpenBuild(); Click(Keys[0]); ModifiedClick(Keys[2], modifier);
		Click("Debug:" + Keys[2]);
		AssertRules(new[] { Keys[0], Keys[2] }, false, true);
		Click("Release:" + Keys[0]);
		AssertRules(new[] { Keys[0], Keys[2] }, false, false);
		Call("input.key", new { key = "a", modifiers = new[] { modifier } });
		Call("input.wait", new { frames = 3 });
		Call("input.key", new { key = "d1", modifiers = new[] { modifier } });
		Call("input.wait", new { frames = 3 });
		Assert.That(ReadRules().GetArrayLength(), Is.Zero);
	}

	[Test]
	public void Scene_list_drag_resizes_and_keeps_a_six_row_minimum()
	{
		OpenBuild();
		var first = WaitForWidget(Keys[0], "Build Game");
		var grip = WaitForWidget("Resize build scenes", "Build Game");
		int start = grip.GetProperty("y").GetInt32();
		Assert.That(start - first.GetProperty("y").GetInt32(), Is.GreaterThanOrEqualTo(first.GetProperty("height").GetInt32() * 6));
		int x = grip.GetProperty("x").GetInt32() + grip.GetProperty("width").GetInt32() / 2;
		Call("input.drag", new { x1 = x, y1 = start + 3, x2 = x, y2 = start + 103, frames = 15 });
		grip = WaitForWidget("Resize build scenes", "Build Game");
		Assert.That(grip.GetProperty("y").GetInt32(), Is.GreaterThan(start + 70));
		Call("input.drag", new { x1 = x, y1 = grip.GetProperty("y").GetInt32() + 3, x2 = x, y2 = first.GetProperty("y").GetInt32(), frames = 15 });
		grip = WaitForWidget("Resize build scenes", "Build Game");
		Assert.That(grip.GetProperty("y").GetInt32(), Is.EqualTo(start).Within(2));
		Call("input.move", new { x = 0, y = 0 });
		Call("input.wait", new { frames = 3 });
		var panel = Call("ui.windows").EnumerateArray().First(w => w.GetProperty("name").GetString().StartsWith("Build Game"));
		Call("screenshot", new { path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "build-scenes-panel.png"),
			x = panel.GetProperty("x").GetInt32(), y = panel.GetProperty("y").GetInt32(), width = panel.GetProperty("width").GetInt32(), height = panel.GetProperty("height").GetInt32() });
	}

	[TestCase("Inter", 75), TestCase("ProggyForever", 100), TestCase("JetBrains Mono", 150), TestCase("Classic (ProggyClean)", 200)]
	public void Build_buttons_fit_scaled_text_and_remain_inside_the_panel(string font, int scale)
	{
		try
		{
			Call("ui.click", new { path = "View/UI font/" + font });
			Call("ui.click", new { path = $"View/Interface scale/{scale}%" });
			Call("ui.resize", new { width = 1920, height = 1080 });
			OpenBuild();
			var grip = WaitForWidget(Keys[0], "Build Game");
			Call("input.scroll", new { x = grip.GetProperty("x").GetInt32() + 5, y = grip.GetProperty("y").GetInt32(), notches = -40 });
			var panel = Call("ui.windows").EnumerateArray().First(w => w.GetProperty("name").GetString().StartsWith("Build Game"));
			foreach (var label in new[] { "Build", "Build and Run", "Cancel" })
			{
				var button = WaitForWidget(label, "Build Game");
				Assert.That(button.GetProperty("height").GetInt32(), Is.GreaterThanOrEqualTo((font.StartsWith("Classic") ? 13 : 14) * scale / 100 + 10 * scale / 100));
				Assert.That(button.GetProperty("x").GetInt32() + button.GetProperty("width").GetInt32(), Is.LessThanOrEqualTo(panel.GetProperty("x").GetInt32() + panel.GetProperty("width").GetInt32()));
			}
			Call("screenshot", new { path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"build-buttons-{scale}.png"),
				x = panel.GetProperty("x").GetInt32(), y = panel.GetProperty("y").GetInt32(), width = panel.GetProperty("width").GetInt32(), height = panel.GetProperty("height").GetInt32() });
			Click("Cancel");
		}
		catch (Exception error)
		{
			TestContext.Progress.WriteLine(error.ToString());
			Call("screenshot", new { path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "build-layout-failure.png") });
			throw;
		}
		finally
		{
			if (Call("ui.windows").EnumerateArray().Any(w => w.GetProperty("name").GetString().StartsWith("Build Game"))) CloseBuild();
			Call("ui.click", new { path = "View/UI font/Classic (ProggyClean)" });
			Call("ui.click", new { path = "View/Interface scale/100%" });
			Call("ui.resize", new { width = 1600, height = 900 });
		}
	}

	[Test]
	public void Debug_and_release_exports_contain_only_their_selected_scenes()
	{
		var duplicate = Path.Combine(ProjectRoot, "Worlds", "MainScene.vscene");
		Directory.CreateDirectory(Path.GetDirectoryName(duplicate));
		File.Copy(Path.Combine(ProjectRoot, "Scenes", "MainScene.vscene"), duplicate);
		OpenBuild(); Click("Clear Selection"); Click(Keys[0]); Click("Debug only");
		Click(Keys[2]); Click("Exclude selected"); CloseBuild();
		foreach (var debug in new[] { true, false })
		{
			var result = Call("build.game", new { platform = "win-x64", debug, aot = false, compileAssets = false });
			Assert.That(result.GetProperty("success").GetBoolean(), Is.True, result.ToString());
			var scenes = Path.Combine(ProjectRoot, "bin", debug ? "Debug" : "Release", "win-x64");
			Assert.That(File.Exists(Path.Combine(scenes, Keys[0] + ".vscene")), Is.EqualTo(debug));
			Assert.That(File.Exists(Path.Combine(scenes, Keys[1] + ".vscene")), Is.True);
			Assert.That(File.Exists(Path.Combine(scenes, Keys[2] + ".vscene")), Is.False);
			Assert.That(File.Exists(Path.Combine(scenes, "Scenes/MainScene.vscene")), Is.True);
			using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(scenes, "ProjectSettings.json")));
			Assert.That(settings.RootElement.GetProperty("InitialScene").GetString(), Is.EqualTo("Scenes/MainScene"));
		}
		Directory.Delete(Path.Combine(ProjectRoot, "Worlds"), true);
		OpenBuild();
	}

	private static void ModifiedClick(string key, string modifier)
	{
		Call("input.key", new { key = modifier, action = "down" });
		try { Click(key); } finally { Call("input.key", new { key = modifier, action = "up" }); }
	}

	private static void Click(string label)
	{
        string key = label switch { "Select All" => "a", "Clear Selection" => "escape", "Include selected" => "d1", "Exclude selected" => "d2", "Debug only" => "d3", "Release only" => "d4", _ => null };
        if (key != null)
        {
            Call("input.key", new { key, modifiers = key == "escape" ? Array.Empty<string>() : new[] { "ctrl" } });
            Call("input.wait", new { frames = 3 });
            return;
        }
		if (label.Contains("Scenes/")) ScrollBuildTo(label);
		WaitForWidget(label, "Build Game");
		Call("ui.click", new { label, window = "Build Game", exact = true });
	}

	private static void OpenBuild()
	{
		Call("ui.click", new { path = "Build/Scenes in Build" });
		ScrollBuildTo(Keys[0]);
		WaitForWidget(Keys[0], "Build Game");
	}

	private static void ScrollBuildTo(string label)
	{
		for (var attempt = 0; attempt < 20; attempt++)
		{
			if (Call("ui.find", new { label, window = "Build Game", exact = true }).GetArrayLength() > 0) return;
			var rows = Call("ui.find", new { label = "Scenes/", kind = "selectable", window = "Build Game" });
			if (rows.GetArrayLength() == 0) { Call("input.wait", new { frames = 3 }); continue; }
			var row = rows[0];
			var target = Call("ui.find", new { label, window = "Build Game", exact = true, visibleOnly = false });
			var direction = target.GetArrayLength() > 0 && target[0].GetProperty("y").GetInt32() < row.GetProperty("y").GetInt32() ? 1 : -1;
			Call("input.scroll", new { x = row.GetProperty("x").GetInt32() + 30, y = row.GetProperty("y").GetInt32() + 10, notches = direction });
			Call("input.wait", new { frames = 2 });
		}
	}

	private static void CloseBuild()
	{
		WaitForWidget(Keys[0], "Build Game");
		// The wheel over the scene table scrolls only the table, so scroll the popup at its own scrollbar.
		var panel = Call("ui.windows").EnumerateArray().First(w => w.GetProperty("name").GetString().StartsWith("Build Game"));
		Call("input.scroll", new { x = panel.GetProperty("x").GetInt32() + panel.GetProperty("width").GetInt32() - 6,
			y = panel.GetProperty("y").GetInt32() + panel.GetProperty("height").GetInt32() / 2, notches = -30 });
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
