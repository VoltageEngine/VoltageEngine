using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Voltage.Editor.Utils;

namespace Voltage.Editor.Persistence;

/// <summary>Per-project scene history and file-picker folder, stored in the editor profile.</summary>
public static class SceneLoadHistory
{
	private const int Limit = 6;

	public static List<string> GetRecentScenes(string projectPath)
	{
		string[] paths;
		try
		{
			paths = JsonSerializer.Deserialize<string[]>(EditorSettingsLoader.LoadSetting(Key("RecentScenes", projectPath), "[]")) ?? Array.Empty<string>();
		}
		catch (JsonException)
		{
			paths = Array.Empty<string>();
		}

		return paths.Where(path => File.Exists(path) && CrossPlatformPath.IsPathUnder(projectPath, path) &&
			Path.GetExtension(path).Equals(".vscene", StringComparison.OrdinalIgnoreCase))
			.Select(CrossPlatformPath.GetFullPath)
			.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
			.Take(Limit).ToList();
	}

	public static void RecordOpenedScene(string projectPath, string scenePath)
	{
		var paths = GetRecentScenes(projectPath);
		paths.RemoveAll(path => CrossPlatformPath.AreEqual(path, scenePath));
		paths.Insert(0, CrossPlatformPath.GetFullPath(scenePath));
		EditorSettingsLoader.SaveSetting(Key("RecentScenes", projectPath), JsonSerializer.Serialize(paths.Take(Limit)));
	}

	public static string GetBrowseFolder(string projectPath)
	{
		var folder = EditorSettingsLoader.LoadSetting(Key("SceneBrowseFolder", projectPath), projectPath);
		return Directory.Exists(folder) && CrossPlatformPath.IsPathUnder(projectPath, folder) ? folder : projectPath;
	}

	public static void RememberSelectedFile(string projectPath, string scenePath) =>
		EditorSettingsLoader.SaveSetting(Key("SceneBrowseFolder", projectPath), Path.GetDirectoryName(CrossPlatformPath.GetFullPath(scenePath)));

	private static string Key(string name, string projectPath)
	{
		var root = Path.TrimEndingDirectorySeparator(CrossPlatformPath.GetFullPath(projectPath));
		return name + ":" + (OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root);
	}
}
