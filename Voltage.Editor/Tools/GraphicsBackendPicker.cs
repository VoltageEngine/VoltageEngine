using ImGuiNET;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.Utils;

namespace Voltage.Editor.Tools;

/// <summary>A combo of every graphics backend; ones this platform or build cannot use are greyed, with the reason on hover.</summary>
internal static class GraphicsBackendPicker
{
	public static bool Draw(string label, ref GraphicsApi choice, bool gameBuild)
	{
		bool changed = false;
		if (Gui.BeginCombo(label, GraphicsBackendSelector.Label(choice)))
		{
			foreach (var option in GraphicsBackendSelector.Choices)
			{
				var reason = GraphicsBackendSelector.Unavailable(option, gameBuild);
				if (Gui.Selectable(GraphicsBackendSelector.Label(option), option == choice, reason == null ? ImGuiSelectableFlags.None : ImGuiSelectableFlags.Disabled))
				{
					changed = option != choice;
					choice = option;
				}
				var note = reason ?? Describe(option, gameBuild);
				if (note != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
					ImGuiSafe.SetTooltipSafe(note);
			}
			ImGui.EndCombo();
		}
		return changed;
	}

	static string Describe(GraphicsApi option, bool gameBuild)
	{
		if (option == GraphicsApi.Auto)
			return gameBuild ? "The best backend the player's machine supports." : "DirectX 12, then Vulkan, then OpenGL 4, skipping any this machine cannot use.";
		if (!gameBuild && GraphicsBackendSelector.FailedBefore(option))
			return "It failed to start on this machine before, so Automatic skips it. Choosing it here tries once more.";
		return option switch
		{
			GraphicsApi.OpenGL => null,
			GraphicsApi.OpenGL4 => "Preview: runs Voltage's effects, but opening a project can still hang on it.",
			_ => "Preview: Voltage's own effects and lighting do not run on it yet."
		};
	}
}
