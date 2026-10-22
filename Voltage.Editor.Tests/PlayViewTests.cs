using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class PlayViewTests
{
	[TestCase("Maximized")]
	[TestCase("Floating")]
	public void Play_view_pops_the_game_out_and_stop_docks_it_again(string mode)
	{
		try
		{
			Choose(mode);
			Call("play.start");
			Call("input.wait", new { frames = 5 });
			var popped = Window("GameWindow" + mode);
			var docked = Window("GameWindow");
			Assert.That(popped.ValueKind, Is.EqualTo(JsonValueKind.Object), "no popped-out game view");
			Assert.That(popped.GetProperty("docked").GetBoolean(), Is.False);
			Assert.That(popped.GetProperty("focused").GetBoolean(), Is.True, "Play must focus the game view");
			Assert.That(docked.GetProperty("docked").GetBoolean(), Is.True, "the dock layout must stay put");
			if (mode == "Maximized")
				Assert.That(popped.GetProperty("width").GetInt32() * popped.GetProperty("height").GetInt32(),
					Is.GreaterThan(docked.GetProperty("width").GetInt32() * docked.GetProperty("height").GetInt32()));

			Call("play.stop");
			Call("input.wait", new { frames = 5 });
			Assert.That(Window("GameWindow" + mode).ValueKind, Is.EqualTo(JsonValueKind.Undefined), "Stop must close the popped-out view");
			Assert.That(Window("GameWindow").GetProperty("docked").GetBoolean(), Is.True);
		}
		finally
		{
			Call("play.stop");
			Choose("Custom");
			Call("input.release");
		}
	}

	private static void Choose(string mode)
	{
		Call("ui.click", new { label = "PlayView", window = "Editor Tools", exact = true });
		Call("input.wait", new { frames = 3 });
		Call("ui.click", new { label = mode, kind = "radio", exact = true });
		Call("input.wait", new { frames = 3 });
	}

	private static JsonElement Window(string id) =>
		Call("ui.windows").EnumerateArray().FirstOrDefault(w => w.GetProperty("id").GetString() == id && w.GetProperty("open").GetBoolean());
}
