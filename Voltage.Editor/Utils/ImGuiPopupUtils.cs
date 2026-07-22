using System;
using ImGuiNET;
using Num = System.Numerics;

namespace Voltage.Editor.Utils
{
	/// <summary>Shared sizing rules for context menus and other popups.</summary>
	public static class ImGuiPopupUtils
	{
		private const float MinHeight = 120f;

		/// <summary>
		/// Caps the height of the popup that <paramref name="popupId"/> is about to open, so a menu taller than the
		/// window scrolls instead of running off the bottom. Multi-viewport is off, so a popup can never leave the
		/// main window - ImGui shifts it up to fit, but a menu with nowhere left to go stays clipped.
		/// Call immediately before <c>BeginPopupContextItem</c> with the same id.
		/// </summary>
		public static void ConstrainHeight(string popupId, float margin = 24f)
		{
			// Only while the popup is actually open: an unconsumed constraint would leak onto the next window begun.
			if (!ImGui.IsPopupOpen(popupId))
				return;

			var available = ImGui.GetMainViewport().WorkSize.Y - margin;

			ImGui.SetNextWindowSizeConstraints(
				Num.Vector2.Zero,
				new Num.Vector2(float.MaxValue, Math.Max(MinHeight, available)));
		}
	}
}
