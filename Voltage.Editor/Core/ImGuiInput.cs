using System;
using System.Collections.Generic;
using ImGuiNET;
using Microsoft.Xna.Framework.Input;
using Nez;

namespace Voltage.Editor.Core
{
	public class ImGuiInput
	{
		private List<int> _keys = new List<int>();
		private int _scrollWheelValue;

		public ImGuiInput()
		{
		}

		/// <summary>
		/// Maps ImGui keys to XNA keys. We use this later on to tell ImGui what keys were pressed
		/// </summary>
		public void SetupInput()
		{
			var io = ImGui.GetIO();

#if FNA
    // forward clipboard methods to SDL
    io.SetClipboardTextFn = Marshal.GetFunctionPointerForDelegate<SetClipboardTextDelegate>(SetClipboardText);
    io.GetClipboardTextFn =
 Marshal.GetFunctionPointerForDelegate<GetClipboardTextDelegate>(SDL2.SDL.SDL_GetClipboardText);
#endif

			_keys.Clear();

			// Map all XNA Keys to ImGui keys (legacy KeyMap for compatibility)
			foreach (ImGuiKey imguiKey in Enum.GetValues(typeof(ImGuiKey)))
			{
				// Try to find a matching XNA Key
				if (Enum.TryParse(typeof(Keys), imguiKey.ToString(), out var xnaKeyObj))
				{
					var xnaKey = (Keys)xnaKeyObj;
					int imguiKeyIndex = (int)imguiKey;
					int xnaKeyIndex = (int)xnaKey;

					if (imguiKeyIndex >= 0 && imguiKeyIndex < io.KeyMap.Count)
					{
						io.KeyMap[imguiKeyIndex] = xnaKeyIndex;
						if (!_keys.Contains(xnaKeyIndex))
							_keys.Add(xnaKeyIndex);
					}
				}
			}

			// Add ImGui keys that do not have a direct XNA mapping
			_keys.Add(io.KeyMap[(int)ImGuiKey.Tab] = (int)Keys.Tab);
			_keys.Add(io.KeyMap[(int)ImGuiKey.LeftArrow] = (int)Keys.Left);
			_keys.Add(io.KeyMap[(int)ImGuiKey.RightArrow] = (int)Keys.Right);
			_keys.Add(io.KeyMap[(int)ImGuiKey.UpArrow] = (int)Keys.Up);
			_keys.Add(io.KeyMap[(int)ImGuiKey.DownArrow] = (int)Keys.Down);
			_keys.Add(io.KeyMap[(int)ImGuiKey.PageUp] = (int)Keys.PageUp);
			_keys.Add(io.KeyMap[(int)ImGuiKey.PageDown] = (int)Keys.PageDown);
			_keys.Add(io.KeyMap[(int)ImGuiKey.Home] = (int)Keys.Home);
			_keys.Add(io.KeyMap[(int)ImGuiKey.End] = (int)Keys.End);
			_keys.Add(io.KeyMap[(int)ImGuiKey.Delete] = (int)Keys.Delete);
			_keys.Add(io.KeyMap[(int)ImGuiKey.Backspace] = (int)Keys.Back);
			_keys.Add(io.KeyMap[(int)ImGuiKey.Enter] = (int)Keys.Enter);
			_keys.Add(io.KeyMap[(int)ImGuiKey.Escape] = (int)Keys.Escape);
			_keys.Add(io.KeyMap[(int)ImGuiKey.LeftCtrl] = (int)Keys.LeftControl);

			// Add main keyboard number keys 0-9
			_keys.Add(io.KeyMap[(int)ImGuiKey._0] = (int)Keys.D0);
			_keys.Add(io.KeyMap[(int)ImGuiKey._1] = (int)Keys.D1);
			_keys.Add(io.KeyMap[(int)ImGuiKey._2] = (int)Keys.D2);
			_keys.Add(io.KeyMap[(int)ImGuiKey._3] = (int)Keys.D3);
			_keys.Add(io.KeyMap[(int)ImGuiKey._4] = (int)Keys.D4);
			_keys.Add(io.KeyMap[(int)ImGuiKey._5] = (int)Keys.D5);
			_keys.Add(io.KeyMap[(int)ImGuiKey._6] = (int)Keys.D6);
			_keys.Add(io.KeyMap[(int)ImGuiKey._7] = (int)Keys.D7);
			_keys.Add(io.KeyMap[(int)ImGuiKey._8] = (int)Keys.D8);
			_keys.Add(io.KeyMap[(int)ImGuiKey._9] = (int)Keys.D9);

			// Add all XNA keys to _keys for full coverage (for UpdateInput)
			foreach (Keys key in Enum.GetValues(typeof(Keys)))
			{
				int keyIndex = (int)key;
				if (!_keys.Contains(keyIndex))
				{
					_keys.Add(keyIndex);
				}
			}

#if !FNA
			Nez.Core.Instance.Window.TextInput += (s, a) =>
			{
				if (a.Character == '\t')
					return;

				io.AddInputCharacter(a.Character);
			};
#else
    TextInputEXT.TextInput += c =>
    {
        if (c == '\t')
            return;
        ImGui.GetIO().AddInputCharacter(c);
    };
#endif
		}

		/// <summary>
		/// Sends XNA input state to ImGui
		/// </summary>
		public void UpdateInput()
		{
			var io = ImGui.GetIO();

			var mouse = Input.CurrentMouseState;
			var keyboard = Input.CurrentKeyboardState;

			for (int i = 0; i < _keys.Count; i++)
			{
				io.KeysDown[_keys[i]] = keyboard.IsKeyDown((Keys)_keys[i]);
			}

			io.KeyShift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
			io.KeyCtrl = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
			io.KeyAlt = keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt);
			io.KeySuper = keyboard.IsKeyDown(Keys.LeftWindows) || keyboard.IsKeyDown(Keys.RightWindows);

			io.DisplaySize = new System.Numerics.Vector2(Nez.Core.GraphicsDevice.PresentationParameters.BackBufferWidth,
				Nez.Core.GraphicsDevice.PresentationParameters.BackBufferHeight);
			io.DisplayFramebufferScale = new System.Numerics.Vector2(1f, 1f);

			io.MousePos = new System.Numerics.Vector2(mouse.X, mouse.Y);

			io.MouseDown[0] = mouse.LeftButton == ButtonState.Pressed;
			io.MouseDown[1] = mouse.RightButton == ButtonState.Pressed;
			io.MouseDown[2] = mouse.MiddleButton == ButtonState.Pressed;

			var scrollDelta = mouse.ScrollWheelValue - _scrollWheelValue;
			io.MouseWheel = scrollDelta > 0 ? 1 : scrollDelta < 0 ? -1 : 0;
			_scrollWheelValue = mouse.ScrollWheelValue;
		}
	}
}
