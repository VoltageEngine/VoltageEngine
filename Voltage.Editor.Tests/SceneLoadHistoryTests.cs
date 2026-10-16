using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Voltage.Cli;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class SceneLoadHistoryTests
{
	private string ProjectRoot => Path.GetDirectoryName(ProjectFile);
	private string HistoryFolder => Path.Combine(ProjectRoot, "Scenes", "SceneLoadHistoryTests");
	private string Key(int number) => $"Scenes/SceneLoadHistoryTests/Scene{number}.vscene";

	[SetUp]
	public void CreateScenes()
	{
		Directory.CreateDirectory(Path.Combine(HistoryFolder, "Deep"));
		var source = Path.Combine(ProjectRoot, "Scenes", "TestScene.vscene");
		for (var i = 0; i < 7; i++) File.Copy(source, Path.Combine(ProjectRoot, Key(i)), true);
		File.Copy(source, Path.Combine(HistoryFolder, "Deep", "Remembered.vscene"), true);
	}

	[TearDown]
	public void Restore()
	{
		foreach (var window in new[] { "Save Changes?", "load-scene-popup" })
			if (Call("ui.find", new { label = "Cancel", window, exact = true }).GetArrayLength() > 0)
				Call("ui.click", new { label = "Cancel", window, exact = true });
		CloseMenus();
		Call("input.release");
		Call("scene.load", new { name = "TestScene", force = true });
		Directory.Delete(HistoryFolder, true);
	}

	[Test]
	public void Hover_menus_show_six_unique_latest_scenes_and_keep_history_after_restart()
	{
		for (var i = 0; i < 7; i++) Load(i);
		var alias = Path.Combine(HistoryFolder, "Deep", "..", "Scene3.vscene");
		if (OperatingSystem.IsWindows()) alias = alias.ToUpperInvariant();
		Call("scene.load", new { path = alias, force = true });
		Assert.That(() => Call("scene.load", new { path = Path.Combine(HistoryFolder, "Missing.vscene"), force = true }), Throws.InstanceOf<CliException>());
		var expected = new[] { 3, 6, 5, 4, 2, 1 }.Select(Key).ToArray();
		Assert.That(OpenRecents(), Is.EqualTo(expected).IgnoreCase);
		CloseMenus();
		Restart();
		Assert.That(OpenRecents(), Is.EqualTo(expected).IgnoreCase);
		CloseMenus();
		File.Delete(Path.Combine(ProjectRoot, Key(1)));
		Assert.That(OpenRecents(), Is.EqualTo(expected.Take(5)).IgnoreCase, "Missing scenes should not appear");
		Call("screenshot", new { path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "recent-scenes.png") });
	}

	[Test]
	public void Recent_scene_selection_preserves_the_unsaved_changes_prompt()
	{
		Load(0);
		Load(1);
		Call("entity.create", new { name = "UnsavedRecentSceneEdit" });
		OpenRecents();
		Call("ui.click", new { label = Key(0), exact = true });
		WaitForWidget("Cancel", "Save Changes?");
		Call("ui.click", new { label = "Cancel", window = "Save Changes?", exact = true });
		Assert.That(Call("status").GetProperty("dirty").GetBoolean(), Is.True);
		Assert.That(CurrentPath(), Does.EndWith(Key(1)));
		Assert.That(OpenRecents()[0], Is.EqualTo(Key(1)));
		Call("ui.click", new { label = Key(0), exact = true });
		WaitForWidget("Don't Save", "Save Changes?");
		Call("ui.click", new { label = "Don't Save", window = "Save Changes?", exact = true });
		Assert.That(CurrentPath(), Does.EndWith(Key(0)));
		Assert.That(OpenRecents()[0], Is.EqualTo(Key(0)));
	}

	[Test]
	public void Select_file_remembers_the_exact_folder_after_cancel_recents_and_restart_and_handles_deleted_folders()
	{
		SceneMenuTests.OpenScenePickerAtRoot();
		foreach (var folder in new[] { "Scenes/", "SceneLoadHistoryTests/", "Deep/" })
		{
			SceneMenuTests.ScrollPickerTo(folder);
			Call("ui.click", new { label = folder, window = "load-scene-popup", exact = true });
		}
		WaitForWidget("Remembered.vscene", "load-scene-popup");
		Call("ui.click", new { label = "Remembered.vscene", window = "load-scene-popup", exact = true });
		Call("ui.click", new { label = "Open", window = "load-scene-popup", exact = true });
		Assert.That(CurrentPath(), Does.EndWith("SceneLoadHistoryTests/Deep/Remembered.vscene"));
		OpenRememberedFolder();
		Call("ui.click", new { label = "../", window = "load-scene-popup", exact = true });
		CancelPicker();
		OpenRememberedFolder();
		CancelPicker();
		Load(0);
		OpenRecents();
		Call("ui.click", new { label = Key(0), exact = true });
		Restart();
		OpenRememberedFolder();
		CancelPicker();
		Directory.Delete(Path.Combine(HistoryFolder, "Deep"), true);
		SceneMenuTests.OpenScenePicker();
		SceneMenuTests.ScrollPickerTo("Scenes/");
		CancelPicker();
	}

	private void Load(int number) => Call("scene.load", new { path = Path.Combine(ProjectRoot, Key(number)), force = true });
	private static string CurrentPath() => Call("status").GetProperty("scene").GetProperty("path").GetString().Replace('\\', '/');
	private static void CancelPicker() => Call("ui.click", new { label = "Cancel", window = "load-scene-popup", exact = true });

	private static void OpenRememberedFolder()
	{
		SceneMenuTests.OpenScenePicker();
		WaitForWidget("Remembered.vscene", "load-scene-popup");
	}

	private static string[] OpenRecents()
	{
		SceneMenuTests.HoverLoadSceneMenu();
		Call("ui.hover", new { label = "Recents", exact = true, frames = 30 });
		return Call("ui.tree").GetProperty("windows").EnumerateArray()
			.SelectMany(window => window.GetProperty("items").EnumerateArray())
			.Where(item => item.GetProperty("kind").GetString() == "menuItem" &&
				item.GetProperty("path").GetString() == "Project/Load Scene/Recents")
			.Select(item => item.GetProperty("label").GetString()).ToArray();
	}

	private static void CloseMenus()
	{
		for (var i = 0; i < 3; i++) Call("input.key", new { key = "escape" });
	}
}
