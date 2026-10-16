using ImGuiNET;
using Microsoft.Xna.Framework.Input;
using Voltage.Editor.Persistence;
using Voltage.Editor.Utils;

namespace Voltage.Editor.ImGuiCore;

public partial class ImGuiManager
{
	private readonly PersistentInt _debugRenderCategories = new("Editor_DebugRenderCategories", (int)DebugRenderCategory.All);

	private void SetDebugRenderCategories(DebugRenderCategory categories)
	{
		DebugRenderFilter.EnabledCategories = categories;
		_debugRenderCategories.Value = (int)categories;
		Core.DebugRenderEnabled = categories != DebugRenderCategory.None;
	}

	private void ToggleDebugRendering()
	{
		if (DebugRenderFilter.EnabledCategories == DebugRenderCategory.None) SetDebugRenderCategories(DebugRenderCategory.All);
		else Core.DebugRenderEnabled = !Core.DebugRenderEnabled;
	}

	private void DrawDebugRenderingButton(System.Numerics.Vector2 size)
	{
		var visible = Core.DebugRenderEnabled && DebugRenderFilter.EnabledCategories != DebugRenderCategory.None;
		var origin = ImGui.GetCursorScreenPos();
		ImGui.BeginGroup();
		ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0);
		ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0);
		if (Gui.IconButton("DebugRenderingToggle", visible ? "\uf06e" : "\uf070", _renderer.IconFontPtr, size, visible))
			ToggleDebugRendering();
		if (ImGui.IsItemHovered()) ImGuiSafe.SetTooltipSafe("Show or hide the selected debug rendering categories");
		ImGui.SameLine(0, 0);
		if (Gui.IconButton("DebugRenderingFilters", "\uf0d7", _renderer.IconFontPtr, new System.Numerics.Vector2(size.X * 0.5f, size.Y), visible))
			ImGui.OpenPopup("DebugRenderingCategories");
		if (ImGui.IsItemHovered()) ImGuiSafe.SetTooltipSafe("Choose debug rendering categories");
		ImGui.PopStyleVar(2);
		ImGui.EndGroup();
		var border = ImGui.GetColorU32(ImGuiCol.Border);
		var draw = ImGui.GetWindowDrawList();
		draw.AddRect(origin, origin + new System.Numerics.Vector2(size.X * 1.5f, size.Y), border);
		draw.AddLine(origin + new System.Numerics.Vector2(size.X, 5 * FontSizeMultiplier),
			origin + new System.Numerics.Vector2(size.X, size.Y - 5 * FontSizeMultiplier), border);
		DrawDebugRenderingFilters();
	}
	private void DrawDebugRenderingFilters()
	{
		if (!Gui.BeginPopup("DebugRenderingCategories")) return;
		if (Input.IsKeyPressed(Keys.Escape))
		{
			ImGui.CloseCurrentPopup();
			Gui.EndPopup();
			return;
		}
		var all = DebugRenderFilter.EnabledCategories == DebugRenderCategory.All;
		if (Gui.Checkbox("All", ref all)) SetDebugRenderCategories(all ? DebugRenderCategory.All : DebugRenderCategory.None);
		ImGui.Separator();
		Category("Colliders", DebugRenderCategory.Colliders);
		Category("Sprite renderers", DebugRenderCategory.Sprites);
		Category("Tilemaps", DebugRenderCategory.Tilemaps);
		Category("Lights", DebugRenderCategory.Lights);
		Category("UI", DebugRenderCategory.UI);
		Category("Other components", DebugRenderCategory.Other);
		Category("Transform gizmos", DebugRenderCategory.Gizmos);
		ImGui.TextDisabled("Checked categories are visible.");
		Gui.EndPopup();

		void Category(string label, DebugRenderCategory category)
		{
			var enabled = DebugRenderFilter.IsEnabled(category);
			if (Gui.Checkbox(label, ref enabled))
				SetDebugRenderCategories(enabled ? DebugRenderFilter.EnabledCategories | category : DebugRenderFilter.EnabledCategories & ~category);
		}
	}
}
