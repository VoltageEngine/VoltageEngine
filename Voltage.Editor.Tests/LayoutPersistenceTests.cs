using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class LayoutPersistenceTests
{
	[Test]
	public void Restart_restores_working_docks_order_active_tabs_visibility_and_floating_geometry()
	{
		Call("window.show", new { name = "TimelineWindow" });
		Call("window.show", new { name = "CoreWindow" });
		Call("window.show", new { name = "TilePaletteWindow", visible = false });
		var layoutPath = Path.Combine(DataDirectory, "Data", "Layouts", "imgui_layout.ini");
		string expected = null;
		string coreSelected = null, timelineSelected = null;
		Thread.Sleep(250);
		Call("ui.focus", new { window = "TimelineWindow" });
		Thread.Sleep(150);
		Call("ui.focus", new { window = "CoreWindow" });
		Thread.Sleep(150);
		Restart(() =>
		{
			Assert.That(File.Exists(layoutPath), Is.True, "orderly exit must save immediately");
			expected = File.ReadAllText(layoutPath);
			expected = SetDock(expected, "TimelineWindow", "0x00000008,0");
			expected = SetDock(expected, "DebugWindow", "0x00000008,1");
			expected = SetDock(expected, "AssetBrowserWindow", "0x00000008,2");
			expected = SetDock(expected, "CoreWindow", "0x00000005,0");
			expected = SetDock(expected, "MainEntityInspector", "0x00000005,1");
			coreSelected = Regex.Match(expected, @"ID=0x00000005[^\r\n]*Selected=(0x[0-9A-F]+)").Groups[1].Value;
			timelineSelected = Regex.Match(expected, @"ID=0x00000008[^\r\n]*Selected=(0x[0-9A-F]+)").Groups[1].Value;
			expected += "\n[Window][###UnavailablePluginWindow]\nPos=40,50\nSize=300,200\nCollapsed=0\n";
			expected += "\n[Window][###TilePaletteWindow]\nPos=83,127\nSize=423,517\nCollapsed=0\n";
			File.WriteAllText(layoutPath, expected);
			File.WriteAllText(Path.Combine(DataDirectory, "Data", "Layouts", "Earlier.ini"), "[Window][###TimelineWindow]\nPos=10,10\nSize=200,200\nCollapsed=0\n");
			var settingsPath = Path.Combine(DataDirectory, "Data", "Settings.json");
			var settings = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, JsonElement>>(File.ReadAllText(settingsPath));
			settings["ImGui_LastSelectedLayout"] = JsonSerializer.SerializeToElement("Earlier");
			File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings));
		});
		Thread.Sleep(600);
		Assert.That(Window("TimelineWindow").GetProperty("docked").GetBoolean(), Is.True);
		Assert.That(Window("CoreWindow").GetProperty("docked").GetBoolean(), Is.True);
		Assert.That(Call("window.list").EnumerateArray().Single(w => w.GetProperty("name").GetString() == "TilePaletteWindow").GetProperty("visible").GetBoolean(), Is.False);
		Restart(() =>
		{
			var restored = File.ReadAllText(layoutPath);
			Assert.That(Section(restored, "CoreWindow"), Does.Contain("DockId=0x00000005,0"));
			Assert.That(Section(restored, "MainEntityInspector"), Does.Contain("DockId=0x00000005,1"));
			Assert.That(restored, Does.Match(@"ID=0x00000005[^\r\n]*Selected=" + coreSelected));
			Assert.That(restored, Does.Match(@"ID=0x00000008[^\r\n]*Selected=" + timelineSelected));
		});
		Call("scene.create", new { name = "LayoutSwitch" });
		Assert.That(Window("TimelineWindow").GetProperty("docked").GetBoolean(), Is.True);
		Call("window.show", new { name = "TilePaletteWindow" });
		Thread.Sleep(250);
		var floating = Window("TilePaletteWindow");
		Assert.That(floating.GetProperty("docked").GetBoolean(), Is.False);
		Assert.That(floating.GetProperty("width").GetInt32(), Is.EqualTo(423));
		Assert.That(floating.GetProperty("height").GetInt32(), Is.EqualTo(517));
		Assert.That(floating.GetProperty("x").GetInt32(), Is.EqualTo(83));
		Assert.That(floating.GetProperty("y").GetInt32(), Is.EqualTo(127));
		Call("window.show", new { name = "CoreWindow", visible = false });
		Restart(() =>
		{
			var settingsPath = Path.Combine(DataDirectory, "Data", "Settings.json");
			var settings = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, JsonElement>>(File.ReadAllText(settingsPath));
			settings["ImGui_LastSelectedLayout"] = JsonSerializer.SerializeToElement("MissingNamedLayout");
			File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings));
		});
		Assert.That(Call("window.list").EnumerateArray().Single(w => w.GetProperty("name").GetString() == "CoreWindow").GetProperty("visible").GetBoolean(), Is.False);
		var saved = File.ReadAllText(layoutPath);
		Assert.That(Section(saved, "TimelineWindow"), Does.Contain("DockId=0x00000008,0"));
		Assert.That(Section(saved, "DebugWindow"), Does.Contain("DockId=0x00000008,1"));
		Assert.That(File.ReadAllText(Path.Combine(DataDirectory, "Data", "Layouts", "Earlier.ini")), Does.Contain("Pos=10,10"));
		Assert.That(File.Exists(layoutPath + ".bak"), Is.True);
		Assert.That(saved, Does.Contain("###UnavailablePluginWindow"));
	}

	private static JsonElement Window(string id) => Call("ui.windows").EnumerateArray().Single(w => w.GetProperty("id").GetString() == id);
	private static string Section(string ini, string id) => Regex.Match(ini, @"\[Window\]\[[^\r\n]*###" + id + @"\][^\[]*").Value;
	private static string SetDock(string ini, string id, string dock)
	{
		var section = Section(ini, id);
		if (section.Length == 0) return ini + $"\n[Window][###{id}]\nPos=262,680\nSize=1016,220\nCollapsed=0\nDockId={dock}\n";
		var updated = Regex.Replace(section, @"DockId=[^\r\n]*", "DockId=" + dock);
		if (!updated.Contains("DockId=")) updated += "DockId=" + dock + "\n";
		return ini.Replace(section, updated);
	}
}
