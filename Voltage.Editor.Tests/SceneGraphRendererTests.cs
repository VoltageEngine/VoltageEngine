using System.Linq;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class SceneGraphRendererTests
{
	[Test]
	public void Renderers_section_shows_scene_renderers_without_periodic_disappearing()
	{
		Call("ui.focus", new { window = "Scene Graph" });
		Call("ui.click", new { label = "Renderers", window = "Scene Graph", exact = true });
		try
		{
			for (var i = 0; i < 4; i++)
			{
				Call("input.wait", new { frames = i == 0 ? 2 : 70 });
				var items = Call("ui.tree", new { window = "Scene Graph", visibleOnly = false })
					.GetProperty("windows")[0].GetProperty("items").EnumerateArray();
				Assert.That(items.Any(item => item.GetProperty("kind").GetString() == "header" &&
					item.GetProperty("label").GetString().EndsWith("Renderer")), Is.True);
			}
		}
		finally
		{
			Call("ui.click", new { label = "Renderers", window = "Scene Graph", exact = true });
		}
	}
}
