using ImGuiNET;

namespace Voltage.Editor.ImGuiCore;

/// <summary>Flows controls into additional rows instead of overlapping or escaping a docked panel.</summary>
internal sealed class ToolbarFlow
{
	private bool _first = true;
	internal void Next(float width)
	{
		if (!_first)
		{
			var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
			if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + width <= right)
				ImGui.SameLine();
		}
		_first = false;
	}
}
