using System;
using System.IO;
using System.Runtime.InteropServices;
using ImGuiNET;
using Voltage.Editor.ImGuiCore;

namespace Voltage.Editor.Styling;

internal static class EditorFonts
{
	// ImGui retains this pointer for future atlas rebuilds. Keep Latin, Greek and UI punctuation pinned.
	private static readonly ushort[] Ranges = { 0x0020, 0x024f, 0x0370, 0x03ff, 0x1f00, 0x1fff, 0x2000, 0x206f, 0x20a0, 0x20cf, 0 };
	private static readonly GCHandle RangesPin = GCHandle.Alloc(Ranges, GCHandleType.Pinned);
	private static readonly ushort[] IconRanges = { 0xf000, 0xf8ff, 0 };
	private static readonly GCHandle IconRangesPin = GCHandle.Alloc(IconRanges, GCHandleType.Pinned);
	internal static IntPtr GlyphRanges => RangesPin.AddrOfPinnedObject();

	internal static (ImFontPtr Ui, ImFontPtr Code, ImFontPtr Icons, ImFontPtr Heading) Build(ImGuiOptions options, float scale, bool classic) =>
		Build(options, scale, classic ? "Classic" : "Inter", "Classic");

	internal static unsafe (ImFontPtr Ui, ImFontPtr Code, ImFontPtr Icons, ImFontPtr Heading) Build(ImGuiOptions options, float scale, string uiFont, string codeFont, float textScale = 1)
	{
		var io = ImGui.GetIO();
		io.NativePtr->FontDefault = null;
		io.Fonts.Clear();
		// Bake the requested pixel size. Never stretch a smaller atlas with FontGlobalScale.
		io.FontGlobalScale = 1;
		var directory = Path.Combine(AppContext.BaseDirectory, "DefaultContent", "Fonts");
		string FontPath(string name) => name switch
		{
			"Inter" => Path.Combine(directory, "Inter.ttf"),
			"ProggyForever" => Path.Combine(directory, "ProggyForever-Regular.ttf"),
			"JetBrainsMono" => Path.Combine(directory, "JetBrainsMono-Regular.ttf"),
			_ => null
		};
		ImFontPtr Add(string path, float size, bool pixelFont, IntPtr ranges)
		{
			var config = new ImFontConfigPtr(ImGuiNative.ImFontConfig_ImFontConfig());
			try
			{
				config.SizePixels = MathF.Round(size * scale);
				config.OversampleH = pixelFont ? 1 : 2;
				config.OversampleV = 1;
				config.PixelSnapH = true;
				return path != null && File.Exists(path)
					? io.Fonts.AddFontFromFileTTF(path, config.SizePixels, config, ranges)
					: io.Fonts.AddFontDefault(config);
			}
			finally { ImGuiNative.ImFontConfig_destroy(config.NativePtr); }
		}
		var ui = Add(FontPath(uiFont), (uiFont == "Classic" ? 13 : 14) * textScale, uiFont == "Classic", GlyphRanges);
		// Every severity and compiler log uses the same selected face and size.
		var code = Add(FontPath(codeFont), codeFont == "Classic" ? 13 : 14, codeFont == "Classic", GlyphRanges);
		var icons = Add(Path.Combine(directory, "FontAwesome-Solid.ttf"), 16, false, IconRangesPin.AddrOfPinnedObject());
		var heading = Add(FontPath(uiFont), 18 * textScale, uiFont == "Classic", GlyphRanges);
		foreach (var font in options._fonts)
			Add(font.Item1, font.Item2, false, GlyphRanges);
		io.NativePtr->FontDefault = ui.NativePtr;
		io.Fonts.Flags = ImFontAtlasFlags.NoPowerOfTwoHeight;
		io.Fonts.Build();
		return (ui, code, icons, heading);
	}
}
