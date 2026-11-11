using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Voltage.Editor.Utils;

/// <summary>Apps opened from the Dock or Finder inherit launchd's bare PATH, which hides dotnet and Homebrew from every tool the editor runs.</summary>
internal static class LaunchPath
{
	/// <summary>Appends the directories a login shell would have to PATH; entries already present keep their order.</summary>
	internal static void Complete()
	{
		if (!OperatingSystem.IsMacOS())
			return;

		var path = Environment.GetEnvironmentVariable("PATH") ?? "";
		var completed = Complete(path, Environment.GetEnvironmentVariable("DOTNET_ROOT"),
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Directory.Exists);
		if (completed != path)
			Environment.SetEnvironmentVariable("PATH", completed);
	}

	internal static string Complete(string path, string dotnetRoot, string home, Func<string, bool> exists)
	{
		var entries = path.Split(':', StringSplitOptions.RemoveEmptyEntries).ToList();
		var candidates = new List<string>();
		if (!string.IsNullOrWhiteSpace(dotnetRoot))
			candidates.Add(dotnetRoot);
		// The same sources macOS's path_helper reads for a login shell, then Homebrew and per-user .NET installs.
		candidates.AddRange(Lines("/etc/paths"));
		if (Directory.Exists("/etc/paths.d"))
			foreach (var file in Directory.GetFiles("/etc/paths.d").OrderBy(f => f, StringComparer.Ordinal))
				candidates.AddRange(Lines(file));
		candidates.AddRange(new[] { "/opt/homebrew/bin", "/usr/local/bin", "/usr/local/share/dotnet",
			Path.Combine(home, ".dotnet"), Path.Combine(home, ".dotnet", "tools") });

		foreach (var candidate in candidates)
			if (!string.IsNullOrWhiteSpace(candidate) && !entries.Contains(candidate, StringComparer.Ordinal) && exists(candidate))
				entries.Add(candidate);
		return string.Join(':', entries);
	}

	private static IEnumerable<string> Lines(string file)
	{
		try { return File.ReadAllLines(file).Select(line => line.Trim()); }
		catch (Exception) { return Array.Empty<string>(); }
	}
}
