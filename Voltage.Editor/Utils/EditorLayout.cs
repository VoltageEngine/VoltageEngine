using System;
using System.Numerics;
using ImGuiNET;

namespace Voltage.Editor.Utils;

internal static class EditorLayout
{
    internal static float Scale => ImGui.GetFontSize() / 14f;

    internal static void WindowSize(Vector2 preferred, ImGuiCond condition = ImGuiCond.FirstUseEver)
    {
        var maximum = Vector2.Max(new Vector2(1), ImGui.GetMainViewport().WorkSize - ImGui.GetStyle().WindowPadding * 2);
        var minimum = Vector2.Min(new Vector2(320, 180) * Scale, maximum);
        ImGui.SetNextWindowSizeConstraints(minimum, maximum);
        ImGui.SetNextWindowSize(Vector2.Clamp(preferred * Scale, minimum, maximum), condition);
    }

    internal static void SameLine(float nextWidth)
    {
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + nextWidth <= right)
            ImGui.SameLine();
    }

    internal static float ButtonWidth(string caption) => ImGui.CalcTextSize(caption).X + ImGui.GetStyle().FramePadding.X * 2;

    internal static float NumberWidth(bool steps = false, int components = 1) =>
        components * (ImGui.CalcTextSize("000000").X + ImGui.GetStyle().FramePadding.X * 2) +
        (steps ? (ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X) * 2 : 0);
}
