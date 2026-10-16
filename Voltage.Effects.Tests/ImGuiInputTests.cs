using System;
using ImGuiNET;
using Microsoft.Xna.Framework.Input;
using NUnit.Framework;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.Utils;

namespace Voltage.Effects.Tests;

[TestFixture, NonParallelizable]
public class ImGuiInputTests
{
	private IntPtr _context;
	private ImGuiInput _input;
	private bool _frameOpen;

	[SetUp]
	public unsafe void CreateContext()
	{
		Assert.That(ImGui.GetVersion(), Does.StartWith("1.91.6"));
		_context = ImGui.CreateContext();
		ImGui.SetCurrentContext(_context);
		var io = ImGui.GetIO();
		io.NativePtr->IniFilename = null;
		io.DisplaySize = new System.Numerics.Vector2(640, 360);
		io.DeltaTime = 1f / 60;
		io.ConfigInputTrickleEventQueue = false;
		io.Fonts.AddFontDefault();
		io.Fonts.Build();
		io.Fonts.SetTexID(new IntPtr(1));
		_input = new ImGuiInput();
	}

	[TearDown]
	public void DestroyContext()
	{
		if (_frameOpen) ImGui.EndFrame();
		_frameOpen = false;
		if (_context != IntPtr.Zero) ImGui.DestroyContext(_context);
		_context = IntPtr.Zero;
	}

	private void Frame(KeyboardState keyboard = default, MouseState mouse = default, bool focused = true)
	{
		if (_frameOpen) ImGui.EndFrame();
		_input.UpdateInput(keyboard, mouse, focused);
		ImGui.NewFrame();
		_frameOpen = true;
	}

	[TestCase(Keys.A, ImGuiKey.A)]
	[TestCase(Keys.D0, ImGuiKey._0)]
	[TestCase(Keys.D9, ImGuiKey._9)]
	[TestCase(Keys.NumPad3, ImGuiKey.Keypad3)]
	[TestCase(Keys.Left, ImGuiKey.LeftArrow)]
	[TestCase(Keys.Back, ImGuiKey.Backspace)]
	[TestCase(Keys.F12, ImGuiKey.F12)]
	[TestCase(Keys.OemPlus, ImGuiKey.Equal)]
	[TestCase(Keys.OemQuestion, ImGuiKey.Slash)]
	[TestCase(Keys.OemPipe, ImGuiKey.Backslash)]
	[TestCase(Keys.OemBackslash, ImGuiKey.Backslash)]
	public void Key_press_and_release_reach_native_ImGui(Keys key, ImGuiKey expected)
	{
		Frame(new KeyboardState(key));
		Assert.That(ImGui.IsKeyDown(expected), Is.True);
		Frame();
		Assert.That(ImGui.IsKeyDown(expected), Is.False);
	}

	[Test]
	public void Modifiers_remain_down_until_both_sides_are_released()
	{
		Frame(new KeyboardState(Keys.LeftControl, Keys.RightControl, Keys.LeftShift, Keys.RightAlt, Keys.LeftWindows));
		var io = ImGui.GetIO();
		Assert.That(io.KeyCtrl && io.KeyShift && io.KeyAlt && io.KeySuper, Is.True);
		Frame(new KeyboardState(Keys.RightControl));
		Assert.That(io.KeyCtrl, Is.True);
		Assert.That(io.KeyShift || io.KeyAlt || io.KeySuper, Is.False);
		Frame();
		Assert.That(io.KeyCtrl, Is.False);
	}

	[Test]
	public void Lost_focus_releases_held_keys_and_mouse_buttons()
	{
		var mouse = new MouseState(20, 30, 0, ButtonState.Pressed, ButtonState.Pressed, ButtonState.Pressed, ButtonState.Pressed, ButtonState.Pressed);
		Frame(new KeyboardState(Keys.A), mouse);
		for (var i = 0; i < 5; i++) Assert.That(ImGui.GetIO().MouseDown[i], Is.True);
		Frame(new KeyboardState(Keys.A), mouse, false);
		Assert.That(ImGui.IsKeyDown(ImGuiKey.A), Is.False);
		for (var i = 0; i < 5; i++) Assert.That(ImGui.GetIO().MouseDown[i], Is.False);
		Frame(new KeyboardState(Keys.A), mouse, true);
		Assert.That(ImGui.IsKeyDown(ImGuiKey.A), Is.True);
		Assert.That(ImGui.GetIO().MousePos, Is.EqualTo(new System.Numerics.Vector2(20, 30)));
	}

	[Test]
	public void Wheel_preserves_magnitude_and_ignores_unfocused_scrolling()
	{
		MouseState Mouse(int vertical, int horizontal) => new(0, 0, vertical, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, horizontal);
		Frame(mouse: Mouse(480, 120));
		Assert.That(ImGui.GetIO().MouseWheel, Is.Zero);
		Frame(mouse: Mouse(720, 0));
		Assert.That(ImGui.GetIO().MouseWheel, Is.EqualTo(2));
		Assert.That(ImGui.GetIO().MouseWheelH, Is.EqualTo(-1));
		Frame(mouse: Mouse(1200, 0), focused: false);
		Frame(mouse: Mouse(1200, 0));
		Assert.That(ImGui.GetIO().MouseWheel, Is.Zero);
	}

	[Test]
	public void Unicode_text_survives_the_event_queue()
	{
		ImGui.GetIO().AddInputCharactersUTF8("ΩЖá");
		Frame();
		var characters = ImGui.GetIO().InputQueueCharacters;
		Assert.That(characters.Size, Is.EqualTo(3));
		Assert.That(characters[0], Is.EqualTo('Ω'));
		Assert.That(characters[1], Is.EqualTo('Ж'));
		Assert.That(characters[2], Is.EqualTo('á'));
	}

	[Test]
	public void Graphite_theme_can_be_reapplied_without_compounding_spacing()
	{
		VoltageEditorThemes.StudioGraphite();
		ImGui.GetStyle().ScaleAllSizes(2);
		VoltageEditorThemes.StudioGraphite();
		Assert.That(ImGui.GetStyle().WindowPadding, Is.EqualTo(new System.Numerics.Vector2(10, 10)));
		Assert.That(ImGui.GetStyle().FramePadding, Is.EqualTo(new System.Numerics.Vector2(6, 5)));
		Assert.That(ImGui.GetVersion(), Does.StartWith("1.91.6"));
	}
}
