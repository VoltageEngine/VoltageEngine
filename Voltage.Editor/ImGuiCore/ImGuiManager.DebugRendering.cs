using ImGuiNET;
using Microsoft.Xna.Framework.Input;
using Voltage.Editor.Persistence;

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
