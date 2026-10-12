using System.Numerics;
using ImGuiNET;

namespace Voltage.Editor.Styling;

internal static class StudioGraphite
{
	internal static readonly Vector4 Background = Rgb(0x151719);
	internal static readonly Vector4 Panel = Rgb(0x202326);
	internal static readonly Vector4 Field = Rgb(0x191c1f);
	internal static readonly Vector4 Border = Rgb(0x353a40);
	internal static readonly Vector4 Text = Rgb(0xe3e6e8);
	internal static readonly Vector4 Muted = Rgb(0x929da8);
	internal static readonly Vector4 Accent = Rgb(0xe4b15b);
	internal static readonly Vector4 Selection = Rgb(0x373128);
	internal static readonly Vector4 Success = Rgb(0x8ac9a2);
	internal static readonly Vector4 Error = Rgb(0xf28b82);
	private static Vector4 Rgb(int value) => new((value >> 16 & 255) / 255f, (value >> 8 & 255) / 255f, (value & 255) / 255f, 1);

	internal static void Apply()
	{
		// Assign logical metrics directly; applying a theme never multiplies existing sizes.
		ImGui.StyleColorsDark();
		var style = ImGui.GetStyle();
		style.WindowPadding = new Vector2(10, 10);
		style.FramePadding = new Vector2(6, 5);
		style.ItemSpacing = new Vector2(8, 6);
		style.ItemInnerSpacing = new Vector2(6, 4);
		style.IndentSpacing = 18;
		style.WindowRounding = 4;
		style.ChildRounding = 3;
		style.FrameRounding = 3;
		style.PopupRounding = 4;
		style.ScrollbarRounding = 3;
		style.GrabRounding = 2;
		style.TabRounding = 3;
		style.FrameBorderSize = 1;
		style.ScrollbarSize = 12;
		style.DisabledAlpha = 0.5f;
		var colors = style.Colors;
		colors[(int)ImGuiCol.Text] = Text;
		colors[(int)ImGuiCol.TextDisabled] = Muted;
		colors[(int)ImGuiCol.WindowBg] = Panel;
		colors[(int)ImGuiCol.ChildBg] = Field;
		colors[(int)ImGuiCol.PopupBg] = Panel;
		colors[(int)ImGuiCol.Border] = Border;
		colors[(int)ImGuiCol.BorderShadow] = Vector4.Zero;
		colors[(int)ImGuiCol.FrameBg] = Field;
		colors[(int)ImGuiCol.FrameBgHovered] = Rgb(0x2b3035);
		colors[(int)ImGuiCol.FrameBgActive] = Selection;
		colors[(int)ImGuiCol.TitleBg] = Background;
		colors[(int)ImGuiCol.TitleBgActive] = Panel;
		colors[(int)ImGuiCol.TitleBgCollapsed] = Background;
		colors[(int)ImGuiCol.MenuBarBg] = Panel;
		colors[(int)ImGuiCol.ScrollbarBg] = Field;
		colors[(int)ImGuiCol.ScrollbarGrab] = Rgb(0x4a5159);
		colors[(int)ImGuiCol.ScrollbarGrabHovered] = Muted;
		colors[(int)ImGuiCol.ScrollbarGrabActive] = Accent;
		colors[(int)ImGuiCol.CheckMark] = Accent;
		colors[(int)ImGuiCol.SliderGrab] = Accent;
		colors[(int)ImGuiCol.SliderGrabActive] = Rgb(0xf1c77d);
		colors[(int)ImGuiCol.Button] = Rgb(0x292e33);
		colors[(int)ImGuiCol.ButtonHovered] = Rgb(0x41403a);
		colors[(int)ImGuiCol.ButtonActive] = Selection;
		colors[(int)ImGuiCol.Header] = Selection;
		colors[(int)ImGuiCol.HeaderHovered] = Rgb(0x454036);
		colors[(int)ImGuiCol.HeaderActive] = Rgb(0x514635);
		colors[(int)ImGuiCol.Separator] = Border;
		colors[(int)ImGuiCol.SeparatorHovered] = Accent;
		colors[(int)ImGuiCol.SeparatorActive] = Accent;
		colors[(int)ImGuiCol.ResizeGrip] = Border;
		colors[(int)ImGuiCol.ResizeGripHovered] = Muted;
		colors[(int)ImGuiCol.ResizeGripActive] = Accent;
		colors[(int)ImGuiCol.Tab] = Background;
		colors[(int)ImGuiCol.TabHovered] = Rgb(0x41403a);
		colors[(int)ImGuiCol.TabSelected] = Panel;
		colors[(int)ImGuiCol.TabSelectedOverline] = Accent;
		colors[(int)ImGuiCol.TabDimmed] = Background;
		colors[(int)ImGuiCol.TabDimmedSelected] = Panel;
		colors[(int)ImGuiCol.TabDimmedSelectedOverline] = Border;
		colors[(int)ImGuiCol.DockingPreview] = new Vector4(Accent.X, Accent.Y, Accent.Z, 0.3f);
		colors[(int)ImGuiCol.DockingEmptyBg] = Background;
		colors[(int)ImGuiCol.PlotLines] = Accent;
		colors[(int)ImGuiCol.PlotLinesHovered] = Success;
		colors[(int)ImGuiCol.PlotHistogram] = Accent;
		colors[(int)ImGuiCol.PlotHistogramHovered] = Success;
		colors[(int)ImGuiCol.TextSelectedBg] = Rgb(0x514635);
		colors[(int)ImGuiCol.DragDropTarget] = Accent;
		colors[(int)ImGuiCol.NavCursor] = Accent;
		colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0, 0, 0, 0.65f);
	}
}
