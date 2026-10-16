using System;
using System.Numerics;
using System.Text;
using System.Globalization;
using ImGuiNET;
using Voltage.Editor.Utils;

namespace Voltage.Editor.ImGuiCore;

public static partial class Gui
{
    private static string Caption(string label)
    {
        var end = label.IndexOf("##", StringComparison.Ordinal);
        return end < 0 ? label : label[..end];
    }

    private static void StartRowIfNeeded(float width)
    {
        if (width > ImGui.GetContentRegionAvail().X && ImGui.GetCursorScreenPos().Y < ImGui.GetItemRectMax().Y)
            ImGui.NewLine();
    }

    private static string FieldLabel(string label, float minimum)
    {
        var caption = Caption(label);
        var width = Math.Max(minimum, ImGui.CalcItemWidth());
        var labelWidth = caption.Length == 0 ? 0 : ImGui.CalcTextSize(caption).X + ImGui.GetStyle().ItemInnerSpacing.X;
        StartRowIfNeeded(width + labelWidth);
        var available = Math.Max(1, ImGui.GetContentRegionAvail().X);
        if (labelWidth > 0 && width + labelWidth > available)
        {
            ImGuiSafe.TextWrappedSafe(caption);
            label = "###" + label;
            labelWidth = 0;
        }
        ImGui.SetNextItemWidth(Math.Max(1, Math.Min(width, available - labelWidth)));
        return label;
    }

    private static string WrappedLabel(string label, float reserved)
    {
        var caption = Caption(label);
        StartRowIfNeeded(ImGui.CalcTextSize(caption).X + reserved);
        var available = Math.Max(1, ImGui.GetContentRegionAvail().X - reserved);
        if (ImGui.CalcTextSize(caption).X <= available) return label;
        var wrapped = new StringBuilder();
        float used = 0;
        foreach (var word in caption.Split(' '))
        {
            var width = ImGui.CalcTextSize(word).X;
            if (used > 0)
            {
                wrapped.Append(used + ImGui.CalcTextSize(" ").X + width > available ? '\n' : ' ');
                if (used + ImGui.CalcTextSize(" ").X + width > available) used = 0;
                else used += ImGui.CalcTextSize(" ").X;
            }
            var elements = StringInfo.GetTextElementEnumerator(word);
            while (elements.MoveNext())
            {
                var element = elements.GetTextElement();
                var elementWidth = ImGui.CalcTextSize(element).X;
                if (used > 0 && used + elementWidth > available) { wrapped.Append('\n'); used = 0; }
                wrapped.Append(element);
                used += elementWidth;
            }
        }
        return wrapped + "###" + label;
    }

    private static bool DrawButton(string label, Vector2 size)
    {
        var width = Math.Max(size.X, EditorLayout.ButtonWidth(Caption(label)));
        StartRowIfNeeded(width);
        if (size.X > 0) size.X = Math.Min(width, Math.Max(1, ImGui.GetContentRegionAvail().X));
        var wrapped = WrappedLabel(label, ImGui.GetStyle().FramePadding.X * 2);
        if (size.Y > 0) size.Y = Math.Max(size.Y, ImGui.CalcTextSize(Caption(wrapped)).Y + ImGui.GetStyle().FramePadding.Y * 2);
        return ImGui.Button(wrapped, size);
    }

    private static bool DrawHeader(string label, ImGuiTreeNodeFlags flags)
    {
        var visible = true;
        return DrawHeader(label, ref visible, flags, false);
    }

    private static bool DrawHeader(string label, ref bool visible, ImGuiTreeNodeFlags flags, bool closable = true)
    {
        var caption = Caption(label);
        var available = Math.Max(1, ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeight() * (closable ? 2 : 1));
        var clipped = ImGui.CalcTextSize(caption).X > available;
        var rendered = label;
        if (clipped)
        {
            var elements = StringInfo.ParseCombiningCharacters(caption);
            var count = elements.Length;
            while (count > 0 && ImGui.CalcTextSize(caption[..(count == elements.Length ? caption.Length : elements[count])] + "…").X > available) count--;
            rendered = caption[..(count == elements.Length ? caption.Length : elements[count])] + "…###" + label;
        }
        var expanded = Item(closable ? ImGui.CollapsingHeader(rendered, ref visible, flags) : ImGui.CollapsingHeader(rendered, flags), "header", label);
        if (clipped && ImGui.IsItemHovered()) ImGuiSafe.SetTooltipSafe(caption);
        return expanded;
    }
}
