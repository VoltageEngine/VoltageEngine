using System;
using ImGuiNET;

namespace Voltage.Editor.Hotkeys
{
	/// <summary>A key plus modifiers. Ctrl stands for Command on macOS.</summary>
	public readonly struct HotkeyBinding : IEquatable<HotkeyBinding>
	{
		public readonly ImGuiKey Key;
		public readonly bool Ctrl;
		public readonly bool Shift;
		public readonly bool Alt;

		public static readonly HotkeyBinding None = new(ImGuiKey.None);

		public HotkeyBinding(ImGuiKey key, bool ctrl = false, bool shift = false, bool alt = false)
		{
			Key = key;
			Ctrl = ctrl;
			Shift = shift;
			Alt = alt;
		}

		public bool IsBound => Key != ImGuiKey.None;

		/// <summary>Modifiers must match exactly, so Ctrl+S never fires while Shift is also held.</summary>
		public bool ModifiersMatch()
		{
			var io = ImGui.GetIO();
			return (io.KeyCtrl || io.KeySuper) == Ctrl && io.KeyShift == Shift && io.KeyAlt == Alt;
		}

		public bool Pressed(bool repeat = false) => IsBound && ModifiersMatch() && ImGui.IsKeyPressed(Key, repeat);

		public bool Down() => IsBound && ModifiersMatch() && ImGui.IsKeyDown(Key);

		public bool Equals(HotkeyBinding other) =>
			Key == other.Key && Ctrl == other.Ctrl && Shift == other.Shift && Alt == other.Alt;

		public override bool Equals(object obj) => obj is HotkeyBinding other && Equals(other);

		public override int GetHashCode() => HashCode.Combine((int)Key, Ctrl, Shift, Alt);

		public static bool operator ==(HotkeyBinding a, HotkeyBinding b) => a.Equals(b);
		public static bool operator !=(HotkeyBinding a, HotkeyBinding b) => !a.Equals(b);

		/// <summary>Round-trippable form written to settings, e.g. "Ctrl+Shift+Z".</summary>
		public override string ToString()
		{
			if (!IsBound)
				return string.Empty;

			var prefix = (Ctrl ? "Ctrl+" : "") + (Shift ? "Shift+" : "") + (Alt ? "Alt+" : "");
			return prefix + Key;
		}

		/// <summary>Human-facing form for menus and the settings list.</summary>
		public string ToDisplayString()
		{
			if (!IsBound)
				return "unbound";

#if OS_MAC
			var ctrlName = "Cmd+";
#else
			var ctrlName = "Ctrl+";
#endif

			var prefix = (Ctrl ? ctrlName : "") + (Shift ? "Shift+" : "") + (Alt ? "Alt+" : "");
			return prefix + KeyName(Key);
		}

		/// <summary>ImGuiKey names the digit row "_1"; everything else reads fine as-is.</summary>
		public static string KeyName(ImGuiKey key)
		{
			var name = key.ToString();
			return name.StartsWith("_", StringComparison.Ordinal) ? name.Substring(1) : name;
		}

		public static bool TryParse(string text, out HotkeyBinding binding)
		{
			binding = None;

			if (string.IsNullOrWhiteSpace(text))
				return true;

			bool ctrl = false, shift = false, alt = false;
			var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries);

			for (var i = 0; i < parts.Length; i++)
			{
				var part = parts[i].Trim();

				if (i < parts.Length - 1)
				{
					if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
					    part.Equals("Cmd", StringComparison.OrdinalIgnoreCase))
						ctrl = true;
					else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
						shift = true;
					else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
						alt = true;
					else
						return false;

					continue;
				}

				if (!Enum.TryParse<ImGuiKey>(part, out var key) &&
				    !Enum.TryParse("_" + part, out key))
					return false;

				binding = new HotkeyBinding(key, ctrl, shift, alt);
			}

			return binding.IsBound;
		}
	}
}
