using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Voltage.Persistence;

namespace Voltage.Editor.Plugins
{
	/// <summary>
	/// Model of the committed <c>&lt;project&gt;/plugins.lock.json</c> file: the exact resolution of every
	/// plugins.json entry — resolved version, git commit SHA, and a sha256 content hash of the payload.
	/// Restore verifies acquired payloads against these pins so every teammate (and CI) gets identical
	/// bytes; a mismatch is a hard error rather than silent drift.
	/// </summary>
	public class PluginLockFile
	{
		public const string FileName = "plugins.lock.json";

		public int SchemaVersion = 1;

		public List<PluginLockEntry> Resolved = new();

		public static string GetPath(string projectPath) => Path.Combine(projectPath, FileName);

		/// <summary>Loads the project's lockfile, or an empty lock when none exists yet.</summary>
		public static PluginLockFile LoadFrom(string projectPath)
		{
			var path = GetPath(projectPath);
			if (!File.Exists(path))
				return new PluginLockFile();

			var lockFile = Json.FromJson<PluginLockFile>(File.ReadAllText(path));
			return lockFile ?? new PluginLockFile();
		}

		public void SaveTo(string projectPath)
		{
			File.WriteAllText(GetPath(projectPath), Json.ToJson(this, prettyPrint: true));
		}

		public PluginLockEntry FindById(string id)
		{
			return Resolved.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>Inserts or replaces the entry for a plugin id.</summary>
		public void Upsert(PluginLockEntry entry)
		{
			var existing = FindById(entry.Id);
			if (existing != null)
				Resolved.Remove(existing);
			Resolved.Add(entry);
		}

		public void RemoveById(string id)
		{
			Resolved.RemoveAll(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
		}
	}

	public class PluginLockEntry
	{
		public string Id;

		/// <summary>Manifest version at resolution time.</summary>
		public string Version;

		public PluginSourceSpec Source = new();

		/// <summary>For git sources: the fully resolved 40-hex commit SHA the ref pointed at.</summary>
		public string Commit;

		/// <summary>
		/// sha256 over the package payload (sorted relative paths + file bytes), "sha256:&lt;hex&gt;".
		/// Null for dev-mode path plugins, which are intentionally unpinned.
		/// </summary>
		public string ContentHash;
	}
}
