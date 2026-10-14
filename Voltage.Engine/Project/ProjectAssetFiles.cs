using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Voltage.Project;

/// <summary>Discovers native project files without walking generated output or linked directories.</summary>
public static class ProjectAssetFiles
{
	private static readonly HashSet<string> GeneratedDirectories = new(StringComparer.OrdinalIgnoreCase)
	{
		"bin", "obj", "Build", "EngineLibs", "PluginLibs", "node_modules", "temp", "tmp", "__pycache__"
	};
	public static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

	public static bool IsIgnoredDirectory(string path) =>
		Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal) || GeneratedDirectories.Contains(Path.GetFileName(path)) ||
		(File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
		Directory.EnumerateFiles(path, "*.runtimeconfig.json", SearchOption.TopDirectoryOnly).Any();

	public static List<string> Find(string root, params string[] extensions)
	{
		var files = new List<string>();
		if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return files;
		var pending = new Stack<string>();
		pending.Push(Path.GetFullPath(root));
		while (pending.Count > 0)
		{
			var directory = pending.Pop();
			foreach (var file in Directory.EnumerateFiles(directory))
				if (extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) files.Add(file);
			foreach (var child in Directory.EnumerateDirectories(directory))
				if (!IsIgnoredDirectory(child)) pending.Push(child);
		}
		files.Sort(PathComparer);
		return files;
	}

	public static string Key(string root, string path) => Path.ChangeExtension(Path.GetRelativePath(root, path), null).Replace('\\', '/');

	/// <summary>Uses exact paths first, then an unambiguous legacy short name or folder-relative suffix.</summary>
	public static string Resolve(string root, string name, string extension, string preferredDirectory = null)
	{
		if (string.IsNullOrWhiteSpace(name)) return null;
		name = name.Replace('\\', '/');
		if (Path.IsPathRooted(name)) return File.Exists(name) ? Path.GetFullPath(name) : null;
		if (string.IsNullOrWhiteSpace(root)) return null;
		if (name.Contains(':') || name.Split('/').Any(p => p.Length == 0 || p == "." || p == ".."))
			throw new ArgumentException("Asset paths must stay inside the project.", nameof(name));
		var relative = name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? name : name + extension;
		var direct = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
		if (File.Exists(direct)) return Path.GetFullPath(direct);
		if (!string.IsNullOrWhiteSpace(preferredDirectory))
		{
			var preferred = Path.Combine(preferredDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
			if (File.Exists(preferred)) return Path.GetFullPath(preferred);
		}
		var key = relative[..^extension.Length];
		var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		var matches = Find(root, extension).Where(file =>
			string.Equals(Key(root, file), key, comparison) || Key(root, file).EndsWith("/" + key, comparison)).ToArray();
		if (matches.Length > 1)
			throw new InvalidOperationException($"More than one asset matches '{name}'. Use its project-relative path: {string.Join(", ", matches.Select(p => Path.GetRelativePath(root, p)))}");
		return matches.SingleOrDefault();
	}
}
