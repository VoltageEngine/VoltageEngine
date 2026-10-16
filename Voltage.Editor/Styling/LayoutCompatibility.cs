using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Voltage.Editor.Styling;

internal static class LayoutCompatibility
{
	// ImGui 1.91.6 changed its hash polynomial. Window names survive, but their stored docking IDs do not.
	internal static string Upgrade(string content)
	{
		if (!content.Contains("0x9C7DA328", StringComparison.OrdinalIgnoreCase)) return content;
		var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (Match window in Regex.Matches(content, @"^\[Window\]\[(.*?)\]\r?$", RegexOptions.Multiline))
		{
			var name = window.Groups[1].Value;
			ids[$"0x{Hash(name, 0, 0xedb88320):X8}"] = $"0x{Hash(name, 0, 0x82f63b78):X8}";
		}
		ids["0x41896AB8"] = "0x7CF38D7C";
		content = Regex.Replace(content, @"(Window|Selected)=(0x[0-9a-fA-F]+)", match =>
			ids.TryGetValue(match.Groups[2].Value, out var replacement) ? match.Groups[1].Value + "=" + replacement : match.Value);
		return Regex.Replace(content, "0x9C7DA328", "0xF8E1A4A1", RegexOptions.IgnoreCase);
	}

	private static uint Hash(string name, uint seed, uint polynomial)
	{
		var crc = ~seed;
		var bytes = Encoding.UTF8.GetBytes(name);
		for (var i = 0; i < bytes.Length; i++)
		{
			if (bytes[i] == '#' && i + 2 < bytes.Length && bytes[i + 1] == '#' && bytes[i + 2] == '#') crc = ~seed;
			crc ^= bytes[i];
			for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? polynomial : 0);
		}
		return ~crc;
	}
}
