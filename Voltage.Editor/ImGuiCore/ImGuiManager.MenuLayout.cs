using System;
using System.Linq;
using ImGuiNET;

namespace Voltage.Editor.ImGuiCore;

public partial class ImGuiManager
{
    private (string Label, Action Draw)[] _layoutMenus;

    private void DrawResponsiveMenus()
    {
        _layoutMenus ??= new (string, Action)[]
        {
            ("File", DrawFileMenu), ("Project", DrawProjectMenu), ("View", DrawViewMenu),
            ("Profiler", DrawProfilerMenu), ("Scripting", DrawScriptingMenu), ("Effects", DrawEffectsMenu),
            ("Plugins (!)", DrawPluginsMenu), ("Build", DrawBuildMenu), ("Help", DrawHelpMenu)
        };
        float Width(string label) => ImGui.CalcTextSize(label).X + ImGui.GetStyle().ItemSpacing.X * 2;
        float remaining = _layoutMenus.Sum(menu => Width(menu.Label));
        for (int index = 0; index < _layoutMenus.Length; index++)
        {
            var menu = _layoutMenus[index];
            var width = Width(menu.Label);
            if (remaining > ImGui.GetContentRegionAvail().X && width + Width("More") > ImGui.GetContentRegionAvail().X)
            {
                if (Gui.BeginMenu("More"))
                {
                    for (; index < _layoutMenus.Length; index++) _layoutMenus[index].Draw();
                    Gui.EndMenu();
                }
                break;
            }
            menu.Draw();
            remaining -= width;
        }
        DrawSaveLayoutPopup();
        DrawRenameLayoutPopup();
        DrawDeleteLayoutPopup();
    }
}
