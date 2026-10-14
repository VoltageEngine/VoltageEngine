using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Voltage.Project;

/// <summary>Scene keys and folders relative to the discovery root.</summary>
public sealed class SceneCatalog
{
	public string Name { get; private set; } = "";
	public string Key { get; private set; } = "";
	public List<SceneCatalog> Folders { get; } = new();
	public List<string> Scenes { get; } = new();

	public static SceneCatalog Read(string directory)
	{
		var keys = Directory.Exists(directory)
			? ProjectAssetFiles.Find(directory, ".vscene")
				.Select(file => Path.GetRelativePath(directory, file))
			: Enumerable.Empty<string>();
		return FromKeys(keys);
	}

	public static SceneCatalog FromKeys(IEnumerable<string> keys)
	{
		var root = new SceneCatalog();
		foreach (var key in keys.Select(NormalizeKey).Distinct(StringComparer.Ordinal)
			.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ThenBy(k => k, StringComparer.Ordinal))
		{
			var node = root;
			var parts = key.Split('/');
			for (int i = 0; i < parts.Length - 1; i++)
			{
				var folder = node.Folders.Find(f => f.Name == parts[i]);
				if (folder == null)
				{
					folder = new SceneCatalog { Name = parts[i], Key = node.Key.Length == 0 ? parts[i] : node.Key + "/" + parts[i] };
					node.Folders.Add(folder);
				}
				node = folder;
			}
			node.Scenes.Add(key);
		}
		return root;
	}

	public IEnumerable<string> AllScenes()
	{
		foreach (var folder in Folders)
			foreach (var scene in folder.AllScenes()) yield return scene;
		foreach (var scene in Scenes) yield return scene;
	}

	public static string NormalizeKey(string key)
	{
		if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A scene key is required.", nameof(key));
		key = key.Replace('\\', '/');
		if (key.EndsWith(".vscene", StringComparison.OrdinalIgnoreCase)) key = key[..^7];
		if (key.Contains(':') || key.Split('/').Any(p => p.Length == 0 || p == "." || p == ".."))
			throw new ArgumentException("Scene keys must be relative paths inside the project.", nameof(key));
		return key;
	}

	public static string Resolve(string directory, string key) =>
		Path.Combine(directory, NormalizeKey(key).Replace('/', Path.DirectorySeparatorChar) + ".vscene");

	public static bool Included(string key, IReadOnlyList<ProjectSettings.SceneBuildRule> rules, bool debug)
	{
		key = NormalizeKey(key);
		if (rules != null)
			foreach (var rule in rules)
				if (rule != null && string.Equals(rule.Scene?.Replace('\\', '/'), key, StringComparison.Ordinal))
					return debug ? rule.Debug : rule.Release;
		return true;
	}
}
