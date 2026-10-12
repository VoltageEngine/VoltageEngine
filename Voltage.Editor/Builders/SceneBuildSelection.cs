using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Voltage.Project;

namespace Voltage.Editor.Builders;

public static class SceneBuildSelection
{
	public static string[] Plan(string scenesDirectory, ProjectSettings settings, bool debug)
	{
		var scenes = SceneCatalog.Read(scenesDirectory).AllScenes()
			.Where(key => SceneCatalog.Included(key, settings?.BuildScenes, debug)).ToArray();
		var initial = settings?.InitialScene;
		if (string.IsNullOrWhiteSpace(initial) || !scenes.Contains(SceneCatalog.NormalizeKey(initial), StringComparer.Ordinal))
			throw new InvalidOperationException($"The startup scene '{initial}' must exist and be included in the {(debug ? "Debug" : "Release")} build.");
		return scenes;
	}

	/// <summary>Removes scenes copied by publish and packages only the selected scenes in the runtime Scenes folder.</summary>
	public static void Package(string projectDirectory, string scenesDirectory, string outputDirectory, IReadOnlyList<string> scenes)
	{
		var sourceRoot = Path.GetFullPath(scenesDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		var outputRoot = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		if (sourceRoot.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase) || outputRoot.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("Scene sources and build output must be separate folders.");
		foreach (var key in scenes)
			if (!File.Exists(SceneCatalog.Resolve(scenesDirectory, key))) throw new FileNotFoundException($"Selected scene '{key}' no longer exists.");
		foreach (var file in Directory.GetFiles(outputDirectory, "*.vscene", SearchOption.AllDirectories))
		{
			File.Delete(file);
			if (File.Exists(file + ".meta")) File.Delete(file + ".meta");
		}
		var paths = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var key in scenes)
		{
			var source = SceneCatalog.Resolve(scenesDirectory, key);
			var relative = "Data/Scenes/" + SceneCatalog.NormalizeKey(key) + ".vscene";
			var destination = Path.Combine(outputDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(Path.GetDirectoryName(destination));
			File.Copy(source, destination, true);
			if (File.Exists(source + ".meta")) File.Copy(source + ".meta", destination + ".meta", true);
			paths[Path.GetRelativePath(projectDirectory, source).Replace('\\', '/')] = relative;
		}

		var manifestPath = Path.Combine(outputDirectory, "Data", "assets.manifest");
		if (!File.Exists(manifestPath)) return;
		var manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifestPath));
		foreach (var id in manifest.Keys.ToArray())
		{
			var path = manifest[id]?.Replace('\\', '/');
			if (path?.EndsWith(".vscene", StringComparison.OrdinalIgnoreCase) != true) continue;
			if (paths.TryGetValue(path, out var destination)) manifest[id] = destination;
			else manifest.Remove(id);
		}
		File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
	}
}
