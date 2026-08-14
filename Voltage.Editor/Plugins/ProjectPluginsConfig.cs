using System;
using System.Collections.Generic;
using System.IO;
using Voltage.Persistence;

namespace Voltage.Editor.Plugins
{
	/// <summary>The committed plugins.json: which plugins a project wants and where each comes from. With PluginLockFile it lets every teammate restore identical payloads.</summary>
	public class ProjectPluginsConfig
	{
		public const string FileName = "plugins.json";

		public int SchemaVersion = 1;

		public List<ProjectPluginEntry> Plugins = new();

		public static string GetPath(string projectPath) => Path.Combine(projectPath, FileName);

		/// <summary>Loads the project's plugins.json, or returns null when the project has none.</summary>
		public static ProjectPluginsConfig LoadFrom(string projectPath)
		{
			var path = GetPath(projectPath);
			if (!File.Exists(path))
				return null;

			var config = Json.FromJson<ProjectPluginsConfig>(File.ReadAllText(path));
			if (config == null)
				throw new PluginManifestException($"Failed to parse {path}: empty or invalid JSON");

			return config;
		}

		public void SaveTo(string projectPath)
		{
			// Sorted, so two teammates adding different plugins do not conflict positionally.
			Plugins.Sort((a, b) => string.Compare(a?.Id, b?.Id, StringComparison.OrdinalIgnoreCase));

			File.WriteAllText(GetPath(projectPath), Json.ToJson(this, prettyPrint: true));
		}
	}

	public class ProjectPluginEntry
	{
		/// <summary>Plugin id this entry pins. Must match the manifest's Id after resolution.</summary>
		public string Id;

		public PluginSourceSpec Source = new();

		/// <summary>Dev mode: re-syncs from the source folder every project open and is exempt from content hashing. Path sources only.</summary>
		public bool Dev;

		/// <summary>Disabled plugins stay listed (and locked) but are not synced or loaded.</summary>
		public bool Disabled;
	}

	/// <summary>Discriminated source of a plugin package: exactly one of Bundled, Git (+Ref), Zip or Path is set.</summary>
	public class PluginSourceSpec
	{
		public bool Bundled;
		public string Git;

		/// <summary>Git ref to pin: tag, branch, or commit SHA. Resolved to a commit SHA in the lockfile.</summary>
		public string Ref;

		public string Zip;
		public string Path;

		public bool IsValid()
		{
			var set = 0;
			if (Bundled) set++;
			if (!string.IsNullOrWhiteSpace(Git)) set++;
			if (!string.IsNullOrWhiteSpace(Zip)) set++;
			if (!string.IsNullOrWhiteSpace(Path)) set++;
			return set == 1;
		}

		/// <summary>No source: the project records that it uses this plugin but nowhere to fetch it from. Replaced once the plugin is published or vendored.</summary>
		public bool IsUnset =>
			!Bundled
			&& string.IsNullOrWhiteSpace(Git)
			&& string.IsNullOrWhiteSpace(Zip)
			&& string.IsNullOrWhiteSpace(Path);

		/// <summary>Short human-readable description for UI ("bundled", "git: ...", ...).</summary>
		public string Describe()
		{
			if (Bundled) return "bundled";
			if (!string.IsNullOrWhiteSpace(Git)) return string.IsNullOrWhiteSpace(Ref) ? $"git: {Git}" : $"git: {Git} @ {Ref}";
			if (!string.IsNullOrWhiteSpace(Zip)) return $"zip: {Zip}";
			if (!string.IsNullOrWhiteSpace(Path)) return $"path: {Path}";
			if (IsUnset) return "not published yet";
			return "(invalid source)";
		}

		/// <summary>Value equality - used to detect that a lock entry no longer matches plugins.json.</summary>
		public bool Matches(PluginSourceSpec other)
		{
			if (other == null) return false;
			return Bundled == other.Bundled
				&& string.Equals(Git, other.Git, StringComparison.Ordinal)
				&& string.Equals(Ref, other.Ref, StringComparison.Ordinal)
				&& string.Equals(Zip, other.Zip, StringComparison.Ordinal)
				&& string.Equals(Path, other.Path, StringComparison.Ordinal);
		}
	}
}
