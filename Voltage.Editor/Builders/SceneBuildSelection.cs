using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Voltage.Project;

namespace Voltage.Editor.Builders;

public static class SceneBuildSelection
{
    public static bool Included(string projectDirectory, string scenesDirectory, string key, IReadOnlyList<ProjectSettings.SceneBuildRule> rules, bool debug)
    {
        key = SceneCatalog.NormalizeKey(key);
        var legacy = string.IsNullOrWhiteSpace(scenesDirectory) ? key :
            Path.ChangeExtension(Path.GetRelativePath(scenesDirectory, SceneCatalog.Resolve(projectDirectory, key)), null).Replace('\\', '/');
        var rule = rules?.FirstOrDefault(r => r != null && r.Scene?.Replace('\\', '/') == key)
            ?? rules?.FirstOrDefault(r => r != null && r.Scene?.Replace('\\', '/') == legacy);
        return rule == null || (debug ? rule.Debug : rule.Release);
    }

    public static string[] Plan(string projectDirectory, ProjectSettings settings, bool debug, string scenesDirectory = null)
    {
        var scenes = SceneCatalog.Read(projectDirectory).AllScenes()
            .Where(key => Included(projectDirectory, scenesDirectory, key, settings?.BuildScenes, debug)).ToArray();
        var initial = settings?.InitialScene;
        var path = ProjectAssetFiles.Resolve(projectDirectory, initial, ".vscene", scenesDirectory);
        if (path == null || !scenes.Contains(ProjectAssetFiles.Key(projectDirectory, path), StringComparer.Ordinal))
            throw new InvalidOperationException($"The startup scene '{initial}' must exist and be included in the {(debug ? "Debug" : "Release")} build.");
        return scenes;
    }

    /// <summary>Packages native files at their project-relative locations and removes excluded scene copies.</summary>
    public static void Package(string projectDirectory, string scenesDirectory, string outputDirectory, IReadOnlyList<string> scenes)
    {
        var projectRoot = Path.GetFullPath(projectDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var sourceRoot = Path.GetFullPath(scenesDirectory ?? projectDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var outputRoot = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (sourceRoot.StartsWith(outputRoot, comparison) ||
            (!ProjectAssetFiles.PathComparer.Equals(sourceRoot, projectRoot) && outputRoot.StartsWith(sourceRoot, comparison)) || projectRoot.StartsWith(outputRoot, comparison))
            throw new InvalidOperationException("Native asset sources and build output must be separate folders.");
        var sources = scenes.Select(key => ProjectAssetFiles.Resolve(projectDirectory, SceneCatalog.NormalizeKey(key), ".vscene", scenesDirectory)
            ?? throw new FileNotFoundException($"Selected scene '{key}' no longer exists.")).ToArray();
        var prefabs = ProjectAssetFiles.Find(projectDirectory, ".vprefab", ".prefab");
        if (sources.Concat(prefabs).Any(p => Path.GetFullPath(p).StartsWith(outputRoot, comparison)))
            throw new InvalidOperationException("Native asset sources cannot live in build output.");
        foreach (var file in ProjectAssetFiles.Find(outputDirectory, ".vscene"))
        {
            File.Delete(file);
            if (File.Exists(file + ".meta")) File.Delete(file + ".meta");
        }
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources.Concat(prefabs))
        {
            var relative = Path.GetRelativePath(projectDirectory, source).Replace('\\', '/');
            var destination = Path.Combine(outputDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(source, destination, true);
            if (File.Exists(source + ".meta")) File.Copy(source + ".meta", destination + ".meta", true);
            paths.Add(relative);
        }

        var manifestPath = Path.Combine(outputDirectory, "Data", "assets.manifest");
        if (!File.Exists(manifestPath)) return;
        var manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifestPath));
        foreach (var id in manifest.Keys.ToArray())
        {
            var path = manifest[id]?.Replace('\\', '/');
            if (path?.EndsWith(".vscene", StringComparison.OrdinalIgnoreCase) == true && !paths.Contains(path)) manifest.Remove(id);
        }
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }
}
