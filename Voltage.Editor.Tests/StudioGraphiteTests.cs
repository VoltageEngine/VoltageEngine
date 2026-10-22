using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class StudioGraphiteTests
{
	private static JsonElement Settings() => JsonDocument.Parse(File.ReadAllText(Path.Combine(DataDirectory, "Data", "Settings.json"))).RootElement.Clone();

	[TestCase("ProggyForever", "ProggyForever")]
	[TestCase("JetBrains Mono", "JetBrainsMono")]
	[TestCase("Classic (ProggyClean)", "Classic")]
	public void Font_choices_persist_separately_for_interface_and_code(string label, string name)
	{
		try
		{
			Call("ui.click", new { path = "View/UI font/Inter" });
			Call("ui.click", new { path = "View/Code font/JetBrains Mono" });
			Call("ui.click", new { path = "View/UI font/" + label });
			Call("ui.click", new { path = "View/Code font/" + label });
			Assert.That(Settings().GetProperty("Editor_UIFont").GetString(), Is.EqualTo(name));
			Assert.That(Settings().GetProperty("Editor_CodeFont").GetString(), Is.EqualTo(name));
			Assert.That(Call("ui.windows").EnumerateArray().Single(w => w.GetProperty("id").GetString() == "GameWindow")
				.GetProperty("docked").GetBoolean(), Is.True, "font changes must preserve docking");
		}
		finally
		{
			Call("ui.click", new { path = "View/UI font/Classic (ProggyClean)" });
			Call("ui.click", new { path = "View/Code font/Classic (ProggyClean)" });
		}
	}

	[TestCase("Scene Components", "Scene Components", "SceneGraph_ShowSceneComponents")]
	[TestCase("Post Processors", "Post Processors", "SceneGraph_ShowPostProcessors")]
	[TestCase("Renderers", "Renderers", "SceneGraph_ShowRenderers")]
	[TestCase("Entities", "Entities", "SceneGraph_ShowEntities")]
	public void Closing_a_section_updates_the_visibility_menu_and_the_menu_restores_it(string header, string menu, string setting)
	{
		Call("ui.focus", new { window = "Scene Graph" });
		Call("input.wait", new { frames = 5 });
		var item = WaitForWidget(header, "Scene Graph");
		var closeSize = WaitForWidget("Scene Graph Sections", "Scene Graph").GetProperty("height").GetInt32();
		Call("input.click", new
		{
			x = item.GetProperty("x").GetInt32() + item.GetProperty("width").GetInt32() - closeSize / 2,
			y = item.GetProperty("y").GetInt32() + closeSize / 2
		});
		try
		{
			var deadline = DateTime.UtcNow.AddSeconds(3);
			while ((!Settings().TryGetProperty(setting, out var persisted) || persisted.GetBoolean()) && DateTime.UtcNow < deadline)
				Thread.Sleep(100);
			Assert.That(Settings().GetProperty(setting).GetBoolean(), Is.False);
			Assert.That(Call("ui.find", new { label = header, window = "Scene Graph", exact = true }).GetArrayLength(), Is.Zero);
			Call("ui.click", new { label = "Scene Graph Sections", window = "Scene Graph", exact = true });
			Call("ui.click", new { label = menu, kind = "menuItem", exact = true });
			WaitForWidget(header, "Scene Graph");
			Assert.That(Settings().GetProperty(setting).GetBoolean(), Is.True);
		}
		finally
		{
			Call("input.release");
			if (Settings().TryGetProperty(setting, out var shown) && !shown.GetBoolean())
			{
				Call("ui.click", new { label = "Scene Graph Sections", window = "Scene Graph", exact = true });
				Call("ui.click", new { label = menu, kind = "menuItem", exact = true });
			}
		}
	}

	[TestCase(1280, 800, 75), TestCase(1600, 900, 100), TestCase(1920, 1080, 150)]
	public void Toolbar_controls_stay_inside_the_dock_at_different_resolutions_and_scales(int width, int height, int scale)
	{
		try
		{
			Call("input.release");
			Call("ui.resize", new { width, height });
			Thread.Sleep(150);
			Call("ui.click", new { path = $"View/Interface scale/{scale}%" });
			Thread.Sleep(150);
			var panel = Call("ui.windows").EnumerateArray().Single(w => w.GetProperty("name").GetString() == "Editor Tools");
			var controls = Call("ui.tree", new { window = "Editor Tools", visibleOnly = false }).GetProperty("windows")[0].GetProperty("items").EnumerateArray().ToList();
			Assert.That(controls.Count(c => c.GetProperty("kind").GetString() == "button"), Is.EqualTo(12));
			foreach (var control in controls)
				Assert.That(control.GetProperty("x").GetInt32() + control.GetProperty("w").GetInt32(),
					Is.LessThanOrEqualTo(panel.GetProperty("x").GetInt32() + panel.GetProperty("width").GetInt32()), control.GetProperty("label").GetString());
			Assert.That(panel.GetProperty("docked").GetBoolean(), Is.True);
		}
		finally
		{
			Call("ui.click", new { path = "View/Interface scale/100%" });
			Call("ui.resize", new { width = 1600, height = 900 });
			Call("input.release");
		}
	}
}
