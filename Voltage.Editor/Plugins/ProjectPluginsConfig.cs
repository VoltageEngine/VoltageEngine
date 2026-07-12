using System;
using System.Collections.Generic;
using System.IO;
using Voltage.Persistence;

namespace Voltage.Editor.Plugins
{
	/// <summary>
	/// Model of the committed <c>&lt;project&gt;/plugins.json</c> file: the list of plugins a project wants
	/// and where each comes from. Together with <see cref="PluginLockFile"/> (exact pins) it lets every
	/// teammate restore identical plugin payloads; the payloads themselves live in the gitignored
	/// PluginLibs folder and the per-user cache, never in source control.
	/// </summary>
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
			File.WriteAllText(GetPath(projectPath), Json.ToJson(this, prettyPrint: true));
		}
	}

	public class ProjectPluginEntry
	{
		/// <summary>Plugin id this entry pins. Must match the manifest's Id after resolution.</summary>
		public string Id;

		public PluginSourceSpec Source = new();

		/// <summary>
		/// Local development mode: the payload re-syncs from the source folder on every project open and is
		/// exempt from lockfile content hashing. Only meaningful with a Path source.
		/// </summary>
		public bool Dev;

		/// <summary>Disabled plugins stay listed (and locked) but are not synced or loaded.</summary>
		public bool Disabled;
	}

	/// <summary>
	/// Discriminated source of a plugin package — exactly one of the fields is set:
	/// <list type="bullet">
	///   <item><see cref="Bundled"/> — ships with the editor (BundledPlugins folder)</item>
	///   <item><see cref="Git"/> (+ <see cref="Ref"/>) — cloned at a pinned tag/commit; private repos use the
	///     user's ambient git credentials, which is how NDA plugins stay off public infrastructure</item>
	///   <item><see cref="Zip"/> — https zip archive, verified by content hash</item>
	///   <item><see cref="Path"/> — local folder, relative to the project root or absolute</item>
	/// </list>
	/// A future registry becomes one more field here (name → git/zip lookup) with no format change.
	/// </summary>
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

		/// <summary>Short human-readable description for UI ("bundled", "git: …", …).</summary>
		public string Describe()
		{
			if (Bundled) return "bundled";
			if (!string.IsNullOrWhiteSpace(Git)) return string.IsNullOrWhiteSpace(Ref) ? $"git: {Git}" : $"git: {Git} @ {Ref}";
			if (!string.IsNullOrWhiteSpace(Zip)) return $"zip: {Zip}";
			if (!string.IsNullOrWhiteSpace(Path)) return $"path: {Path}";
			return "(invalid source)";
		}

		/// <summary>Value equality — used to detect that a lock entry no longer matches plugins.json.</summary>
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
