using System;
using System.Collections.Generic;
using ImGuiNET;
using Num = System.Numerics;


namespace Voltage.Editor.ImGuiCore;

public class ImGuiOptions
{
	internal bool _includeDefaultFont = true;
	internal List<Tuple<string, float>> _fonts = new();
	internal Num.Vector2 _gameWindowFirstPosition = new(345f, 25f);
	internal ImGuiWindowFlags _gameWindowFlags = ImGuiWindowFlags.NoCollapse;
	public float FontSizeMultiplier = 1.0f;

	public ImGuiOptions AddFont(string path, float size)
	{
		_fonts.Add(new Tuple<string, float>(path, size));
		return this;
	}

	public ImGuiOptions IncludeDefaultFont(bool include)
	{
		_includeDefaultFont = include;
		return this;
	}
}