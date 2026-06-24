using ImGuiNET;
using Num = System.Numerics;

namespace Voltage.Editor.Utils
{
	/// <summary>
	/// Safe wrappers around ImGui text functions that forward to native printf-style varargs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="ImGui.Text"/>, <see cref="ImGui.TextColored"/>, <see cref="ImGui.TextWrapped"/>,
	/// <see cref="ImGui.TextDisabled"/>, <see cref="ImGui.BulletText"/>, <see cref="ImGui.LabelText"/>
	/// and <see cref="ImGui.SetTooltip"/> pass their string straight to a native printf-family function.
	/// Any literal <c>%</c> in a RUNTIME string (e.g. <c>$"Zoom: {x}%"</c>) is then interpreted as a
	/// format specifier and reads garbage off the varargs stack — corrupting output or crashing.
	/// </para>
	/// <para>
	/// These helpers route through <see cref="ImGui.TextUnformatted(string)"/> (and tooltip/wrap/color
	/// primitives), which treat the string as opaque text, so any string containing <c>%</c> is safe.
	/// Use these for ANY string that is not a compile-time literal known to be <c>%</c>-free.
	/// </para>
	/// </remarks>
	public static class ImGuiSafe
	{
		/// <summary>Safe replacement for <see cref="ImGui.Text(string)"/>.</summary>
		public static void TextSafe(string text)
		{
			ImGui.TextUnformatted(text ?? string.Empty);
		}

		/// <summary>Safe replacement for <see cref="ImGui.TextColored(System.Numerics.Vector4,string)"/>.</summary>
		public static void TextColoredSafe(Num.Vector4 color, string text)
		{
			ImGui.PushStyleColor(ImGuiCol.Text, color);
			ImGui.TextUnformatted(text ?? string.Empty);
			ImGui.PopStyleColor();
		}

		/// <summary>Safe replacement for <see cref="ImGui.TextDisabled(string)"/>.</summary>
		public static void TextDisabledSafe(string text)
		{
			var color = ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];
			ImGui.PushStyleColor(ImGuiCol.Text, color);
			ImGui.TextUnformatted(text ?? string.Empty);
			ImGui.PopStyleColor();
		}

		/// <summary>Safe replacement for <see cref="ImGui.TextWrapped(string)"/>.</summary>
		public static void TextWrappedSafe(string text)
		{
			// 0.0f wraps at the window content region edge, matching ImGui.TextWrapped's behavior.
			ImGui.PushTextWrapPos(0.0f);
			ImGui.TextUnformatted(text ?? string.Empty);
			ImGui.PopTextWrapPos();
		}

		/// <summary>Safe, colored wrapped text (no ImGui equivalent, but commonly needed).</summary>
		public static void TextWrappedColoredSafe(Num.Vector4 color, string text)
		{
			ImGui.PushStyleColor(ImGuiCol.Text, color);
			ImGui.PushTextWrapPos(0.0f);
			ImGui.TextUnformatted(text ?? string.Empty);
			ImGui.PopTextWrapPos();
			ImGui.PopStyleColor();
		}

		/// <summary>Safe replacement for <see cref="ImGui.BulletText(string)"/>.</summary>
		public static void BulletTextSafe(string text)
		{
			// Mirror BulletText layout: a bullet glyph followed by unformatted text on the same line.
			ImGui.Bullet();
			ImGui.SameLine();
			ImGui.TextUnformatted(text ?? string.Empty);
		}

		/// <summary>Safe replacement for <see cref="ImGui.LabelText(string,string)"/>.</summary>
		public static void LabelTextSafe(string label, string text)
		{
			// LabelText draws "<value>    <label>". Reproduce with a value + same-line label so neither
			// the value nor the label string is treated as a printf format.
			ImGui.TextUnformatted(text ?? string.Empty);
			ImGui.SameLine();
			ImGui.TextUnformatted(label ?? string.Empty);
		}

		/// <summary>Safe replacement for <see cref="ImGui.SetTooltip(string)"/>.</summary>
		public static void SetTooltipSafe(string text)
		{
			ImGui.BeginTooltip();
			ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
			ImGui.TextUnformatted(text ?? string.Empty);
			ImGui.PopTextWrapPos();
			ImGui.EndTooltip();
		}
	}
}
