using System;
using System.Numerics;
using ImGuiNET;
using NUnit.Framework;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.Styling;

namespace Voltage.Effects.Tests;

[TestFixture, NonParallelizable]
public class EditorLayoutTests
{
	private IntPtr _context;
	[SetUp]
	public unsafe void Setup()
	{
		_context = ImGui.CreateContext();
		ImGui.SetCurrentContext(_context);
		ImGui.GetIO().NativePtr->IniFilename = null;
		ImGui.GetIO().DisplaySize = new Vector2(1920, 1080);
		ImGui.GetIO().DeltaTime = 1f / 60;
	}
	[TearDown]
	public void Cleanup()
	{
		if (_context != IntPtr.Zero) ImGui.DestroyContext(_context);
		_context = IntPtr.Zero;
	}

	[TestCase(0.75f, true), TestCase(1f, true), TestCase(1.25f, true), TestCase(1.5f, true), TestCase(1.75f, true), TestCase(2f, true)]
	[TestCase(0.75f, false), TestCase(1f, false), TestCase(1.25f, false), TestCase(1.5f, false), TestCase(1.75f, false), TestCase(2f, false)]
	public unsafe void Fonts_are_rebaked_at_native_pixels_without_accumulating_old_fonts(float scale, bool classic)
	{
		for (var rebuild = 0; rebuild < 3; rebuild++)
		{
			var fonts = EditorFonts.Build(new ImGuiOptions(), scale, classic);
			Assert.That(fonts.Ui.FontSize, Is.EqualTo(MathF.Round((classic ? 13 : 14) * scale)));
			Assert.That(fonts.Code.FontSize, Is.EqualTo(MathF.Round(13 * scale)));
			Assert.That(fonts.Code.CalcTextSizeA(fonts.Code.FontSize, float.MaxValue, 0, "iiii").X,
				Is.EqualTo(fonts.Code.CalcTextSizeA(fonts.Code.FontSize, float.MaxValue, 0, "WWWW").X), "code must remain monospaced");
			Assert.That(ImGui.GetIO().FontGlobalScale, Is.EqualTo(1));
			Assert.That(ImGui.GetIO().Fonts.Fonts.Size, Is.EqualTo(4));
			foreach (var glyph in new[] { '\uf245', '\uf065', '\uf2f1', '\uf5cb', '\uf1fc', '\uf04b', '\uf04d', '\uf04c', '\uf2ea', '\uf06e', '\uf070', '\uf028', '\uf6a9' })
				Assert.That(fonts.Icons.FindGlyphNoFallback(glyph).NativePtr != null, Is.True, $"missing toolbar icon {glyph:x4}");
		}
	}

	[TestCase(180, 0.75f), TestCase(260, 1f), TestCase(360, 2f)]
	[TestCase(180, 1.5f), TestCase(260, 2f), TestCase(360, 0.75f)]
	[TestCase(180, 2f)]
	[TestCase(1280, 1f), TestCase(1600, 1.5f), TestCase(1920, 2f)]
	public unsafe void Docked_toolbar_controls_wrap_and_remain_within_panel_width(int width, float scale)
	{
		var fonts = EditorFonts.Build(new ImGuiOptions(), scale, true);
		ImGui.GetIO().Fonts.SetTexID(new IntPtr(1));
		StudioGraphite.Apply();
		ImGui.GetStyle().ScaleAllSizes(scale);
		for (var frame = 0; frame < 2; frame++)
		{
			ImGui.NewFrame();
			ImGui.SetNextWindowPos(Vector2.Zero);
			ImGui.SetNextWindowSize(new Vector2(width, 700));
			Gui.Begin("Tools", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize);
			var flow = new ToolbarFlow();
			var button = new Vector2(ImGui.GetFrameHeight() + 4 * scale);
			var previousMax = Vector2.Zero;
			for (var i = 0; i < 12; i++)
			{
				flow.Next(button.X);
				Gui.IconButton("Tool" + i, "\uf245", fonts.Icons, button);
				var min = ImGui.GetItemRectMin();
				var max = ImGui.GetItemRectMax();
				Assert.That(max.X, Is.LessThanOrEqualTo(width), "a control escaped the docked panel");
				Assert.That(min.X >= previousMax.X || min.Y >= previousMax.Y, Is.True, "controls overlapped");
				previousMax = max;
			}
			foreach (var label in new[] { "Snap to Grid", "Show Grid" })
			{
				var caption = label;
				if (ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2 > ImGui.GetContentRegionAvail().X)
					caption = label == "Snap to Grid" ? "Snap" : "Grid";
				flow.Next(ImGui.CalcTextSize(caption).X + ImGui.GetStyle().FramePadding.X * 2);
				Gui.CaptionButton(label, caption);
				Assert.That(ImGui.GetItemRectMax().X, Is.LessThanOrEqualTo(width));
			}
			Gui.End();
			ImGui.EndFrame();
		}
	}

	[TestCase("ProggyForever", 0.75f), TestCase("ProggyForever", 1f), TestCase("ProggyForever", 1.25f)]
	[TestCase("ProggyForever", 1.5f), TestCase("ProggyForever", 1.75f), TestCase("ProggyForever", 2f)]
	[TestCase("JetBrainsMono", 0.75f), TestCase("JetBrainsMono", 1f), TestCase("JetBrainsMono", 1.25f)]
	[TestCase("JetBrainsMono", 1.5f), TestCase("JetBrainsMono", 1.75f), TestCase("JetBrainsMono", 2f)]
	public void Alternative_fonts_are_loaded_at_native_size_and_keep_code_monospaced(string name, float scale)
	{
		var fonts = EditorFonts.Build(new ImGuiOptions(), scale, name, name);
		Assert.That(fonts.Ui.FontSize, Is.EqualTo(MathF.Round(14 * scale)));
		Assert.That(fonts.Code.FontSize, Is.EqualTo(MathF.Round(14 * scale)));
		Assert.That(fonts.Code.CalcTextSizeA(fonts.Code.FontSize, float.MaxValue, 0, "iiii").X,
			Is.EqualTo(fonts.Code.CalcTextSizeA(fonts.Code.FontSize, float.MaxValue, 0, "WWWW").X));
		Assert.That(ImGui.GetIO().FontGlobalScale, Is.EqualTo(1));
		Assert.That(ImGui.GetIO().Fonts.Fonts.Size, Is.EqualTo(4));
	}

	[Test]
	public void Legacy_docking_layout_keeps_panel_sizes_and_uses_the_new_native_host_id()
	{
		var layout = "[Window][###SceneGraphWindow]\nPos=4,5\nSize=260,700\nDockId=0x00000003,0\n[Docking][Data]\nDockSpace ID=0x9C7DA328 Window=0x41896AB8 Split=X\n DockNode ID=0x00000003 Parent=0x9C7DA328 Selected=0x043F4834\n";
		var upgraded = LayoutCompatibility.Upgrade(layout);
		Assert.That(upgraded, Does.Contain("ID=0xF8E1A4A1 Window=0x7CF38D7C"));
		Assert.That(upgraded, Does.Contain("Size=260,700"));
		Assert.That(upgraded, Does.Contain("DockId=0x00000003,0"));
		Assert.That(upgraded, Does.Not.Contain("0x043F4834"));
		Assert.That(LayoutCompatibility.Upgrade(upgraded), Is.EqualTo(upgraded));
		EditorFonts.Build(new ImGuiOptions(), 1, true);
		ImGui.GetIO().Fonts.SetTexID(new IntPtr(1));
		ImGui.NewFrame();
		ImGui.Begin("DockSpaceWindow");
		Assert.That(ImGui.GetID("MainDockSpace"), Is.EqualTo(0xF8E1A4A1));
		ImGui.End();
		ImGui.EndFrame();
	}
}
