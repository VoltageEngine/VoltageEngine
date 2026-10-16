using System;
using System.IO;
using System.Text.Json;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture]
public class GatewayInputAxisTests
{
	[Test]
	public void Synthetic_keys_update_joysticks_axes_overlap_and_press_edges()
	{
		var previous = Call("scene.info").GetProperty("name").GetString();
		Call("scene.save");
		var path = Path.Combine(Path.GetDirectoryName(ProjectFile), "Scripts", "GatewayAxisProbe.cs");
		File.WriteAllText(path, """
			using Microsoft.Xna.Framework;
			using Microsoft.Xna.Framework.Input;
			using Voltage;
			public partial class GatewayAxisProbe : Component, IUpdatable
			{
			    public Vector2 Joystick;
			    public float Axis;
			    public bool RawD;
			    public int PressEdges;
			    private VirtualJoystick _joystick;
			    private VirtualAxis _axis;
			    private VirtualButton _button;
			    public override void OnStart()
			    {
			        SetSerialized(false);
			        _joystick = new VirtualJoystick(false).AddKeyboardKeys(VirtualInput.OverlapBehavior.TakeNewer, Keys.A, Keys.D, Keys.S, Keys.W);
			        _axis = new VirtualAxis(new VirtualAxis.KeyboardKeys(VirtualInput.OverlapBehavior.TakeNewer, Keys.A, Keys.D));
			        _button = new VirtualButton().AddKeyboardKey(Keys.Enter);
			    }
			    public void Update()
			    {
			        if (_joystick == null) return;
			        Joystick = _joystick.Value;
			        Axis = _axis.Value;
			        RawD = Input.IsKeyDown(Keys.D);
			        if (_button.IsPressed) PressEdges++;
			    }
			    public override void OnRemovedFromEntity()
			    {
			        _joystick?.Deregister(); _axis?.Deregister(); _button?.Deregister();
			    }
			}
			""");
		try
		{
			Assert.That(Call("scripts.compile", new { reloadScene = true }).GetProperty("success").GetBoolean(), Is.True);
			Call("scene.create", new { name = "GatewayAxes", template = "empty" });
			Call("entity.create", new { name = "Axis probe" });
			Call("component.add", new { entity = "Axis probe", type = "GatewayAxisProbe" });
			Call("play.start");
			Call("input.move", new { x = 800, y = 400 });
			Key("D", "down");
			AssertAxis(1);
			Call("input.wait", new { frames = 8 });
			AssertAxis(1);
			Key("A", "down");
			AssertAxis(-1);
			Call("input.wait", new { frames = 8 });
			AssertAxis(-1);
			Key("A", "up");
			AssertAxis(1);
			Key("D", "up");
			AssertAxis(0);
			Key("Enter", "press");
			Call("input.wait", new { frames = 5 });
			Assert.That(Values().GetProperty("PressEdges").GetInt32(), Is.EqualTo(1));
			Key("D", "down");
			Call("input.release");
			Call("input.wait", new { frames = 3 });
			Assert.That(Values().GetProperty("RawD").GetBoolean(), Is.False);
			AssertAxis(0);
		}
		finally
		{
			Call("input.release");
			Call("play.stop");
			Call("scene.load", new { name = previous, force = true });
		}
	}

	private static void Key(string key, string action) => Call("input.key", new { key, action });
	private static JsonElement Values() => Call("component.get", new { entity = "Axis probe", type = "GatewayAxisProbe" }).GetProperty("values");
	private static void AssertAxis(float expected)
	{
		Call("input.wait", new { frames = 2 });
		var values = Values();
		Assert.That(values.GetProperty("Joystick").GetProperty("x").GetSingle(), Is.EqualTo(expected));
		Assert.That(values.GetProperty("Axis").GetSingle(), Is.EqualTo(expected));
	}
}
