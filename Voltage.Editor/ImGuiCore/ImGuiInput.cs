using System;
using System.Linq;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Voltage.Editor.ImGuiCore;

public class ImGuiInput
{
	private static readonly (ImGuiKey Key, Keys[] Sources)[] KeyBindings = Enum.GetValues<Keys>()
		.Select(key => (Key: key, ImGuiKey: MapKey(key)))
		.Where(binding => binding.ImGuiKey != ImGuiKey.None)
		.GroupBy(binding => binding.ImGuiKey)
		.Select(group => (Key: group.Key, Sources: group.Select(binding => binding.Key).ToArray())).ToArray();
	private GameWindow _window;
	private int _scrollWheelValue;
	private int _horizontalScrollWheelValue;
	private bool _hasScrollBaseline;

	public void SetupInput()
	{
		if (_window != null)
			_window.TextInput -= OnTextInput;
		_window = Core.Instance.Window;
		_window.TextInput += OnTextInput;
	}

	private static void OnTextInput(object sender, TextInputEventArgs args)
	{
		if (args.Character != '\t' && (Core.Instance.IsActive || Core.KeepRunningWhenUnfocused) && ImGui.GetCurrentContext() != IntPtr.Zero)
			ImGui.GetIO().AddInputCharacter(args.Character);
	}

	public void UpdateInput()
	{
		var io = ImGui.GetIO();
		io.DisplaySize = new System.Numerics.Vector2(Core.GraphicsDevice.PresentationParameters.BackBufferWidth,
			Core.GraphicsDevice.PresentationParameters.BackBufferHeight);
		io.DisplayFramebufferScale = System.Numerics.Vector2.One;
		UpdateInput(Input.CurrentKeyboardState, Input.CurrentMouseState, Core.Instance.IsActive || Core.KeepRunningWhenUnfocused);
		var client = Core.Instance.Window.ClientBounds;
		if (client.Width > 0 && client.Height > 0 && (Core.Instance.IsActive || Core.KeepRunningWhenUnfocused))
			io.AddMousePosEvent(Input.CurrentMouseState.X * io.DisplaySize.X / client.Width,
				Input.CurrentMouseState.Y * io.DisplaySize.Y / client.Height);
	}

	internal void UpdateInput(KeyboardState keyboard, MouseState mouse, bool focused)
	{
		var io = ImGui.GetIO();
		io.AddFocusEvent(focused);
		foreach (var binding in KeyBindings)
		{
			var down = false;
			foreach (var key in binding.Sources)
				down |= focused && keyboard.IsKeyDown(key);
			io.AddKeyEvent(binding.Key, down);
		}
		io.AddKeyEvent(ImGuiKey.ModShift, focused && (keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift)));
		io.AddKeyEvent(ImGuiKey.ModCtrl, focused && (keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl)));
		io.AddKeyEvent(ImGuiKey.ModAlt, focused && (keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt)));
		io.AddKeyEvent(ImGuiKey.ModSuper, focused && (keyboard.IsKeyDown(Keys.LeftWindows) || keyboard.IsKeyDown(Keys.RightWindows)));
		io.AddMousePosEvent(focused ? mouse.X : -float.MaxValue, focused ? mouse.Y : -float.MaxValue);
		io.AddMouseButtonEvent(0, focused && mouse.LeftButton == ButtonState.Pressed);
		io.AddMouseButtonEvent(1, focused && mouse.RightButton == ButtonState.Pressed);
		io.AddMouseButtonEvent(2, focused && mouse.MiddleButton == ButtonState.Pressed);
		io.AddMouseButtonEvent(3, focused && mouse.XButton1 == ButtonState.Pressed);
		io.AddMouseButtonEvent(4, focused && mouse.XButton2 == ButtonState.Pressed);
		if (_hasScrollBaseline && focused)
			io.AddMouseWheelEvent((mouse.HorizontalScrollWheelValue - _horizontalScrollWheelValue) / 120f,
				(mouse.ScrollWheelValue - _scrollWheelValue) / 120f);
		_scrollWheelValue = mouse.ScrollWheelValue;
		_horizontalScrollWheelValue = mouse.HorizontalScrollWheelValue;
		_hasScrollBaseline = true;
	}

	internal static ImGuiKey MapKey(Keys key)
	{
		if (key >= Keys.D0 && key <= Keys.D9)
			return ImGuiKey._0 + (key - Keys.D0);
		if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
			return ImGuiKey.Keypad0 + (key - Keys.NumPad0);
		return key switch
		{
			Keys.Left => ImGuiKey.LeftArrow,
			Keys.Right => ImGuiKey.RightArrow,
			Keys.Up => ImGuiKey.UpArrow,
			Keys.Down => ImGuiKey.DownArrow,
			Keys.Back => ImGuiKey.Backspace,
			Keys.LeftControl => ImGuiKey.LeftCtrl,
			Keys.RightControl => ImGuiKey.RightCtrl,
			Keys.LeftWindows => ImGuiKey.LeftSuper,
			Keys.RightWindows => ImGuiKey.RightSuper,
			Keys.Apps => ImGuiKey.Menu,
			Keys.OemSemicolon => ImGuiKey.Semicolon,
			Keys.OemPlus => ImGuiKey.Equal,
			Keys.OemComma => ImGuiKey.Comma,
			Keys.OemMinus => ImGuiKey.Minus,
			Keys.OemPeriod => ImGuiKey.Period,
			Keys.OemQuestion => ImGuiKey.Slash,
			Keys.OemTilde => ImGuiKey.GraveAccent,
			Keys.OemOpenBrackets => ImGuiKey.LeftBracket,
			Keys.OemPipe or Keys.OemBackslash => ImGuiKey.Backslash,
			Keys.OemCloseBrackets => ImGuiKey.RightBracket,
			Keys.OemQuotes => ImGuiKey.Apostrophe,
			Keys.Scroll => ImGuiKey.ScrollLock,
			Keys.Decimal => ImGuiKey.KeypadDecimal,
			Keys.Divide => ImGuiKey.KeypadDivide,
			Keys.Multiply => ImGuiKey.KeypadMultiply,
			Keys.Subtract => ImGuiKey.KeypadSubtract,
			Keys.Add => ImGuiKey.KeypadAdd,
			_ => Enum.TryParse<ImGuiKey>(key.ToString(), out var mapped) ? mapped : ImGuiKey.None
		};
	}
}
