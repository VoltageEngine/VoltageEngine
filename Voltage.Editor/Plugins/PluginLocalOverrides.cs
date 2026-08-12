using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Voltage.Persistence;

namespace Voltage.Editor.Plugins
{
	/// <summary>
	/// Model of the gitignored <c>&lt;project&gt;/plugins.local.json</c>: which plugins this machine resolves
	/// from a folder of its own, instead of from where the project says they come from.
	///
	/// <para>A folder on one machine means nothing on another. Committing it makes every teammate's restore
	/// fail on a path only one person has, so the folder never goes near <c>plugins.json</c> - that file
	/// carries only sources a teammate can actually fetch (git, zip, registry, bundled, or a path inside the
	/// repository), and the machine-specific half lives here, next to it and ignored by git.</para>
	///
	/// <para>This is the same split npm draws between a dependency and <c>npm link</c>, and Cargo between a
	/// dependency and a <c>paths</c> override. The trade is the same one they accept: two people can be running
	/// different code for the same plugin id with nothing in git recording it, which is why the Plugin Manager
	/// marks an overridden plugin plainly.</para>
	/// </summary>
	public class PluginLocalOverrides
	{
		public const string FileName = "plugins.local.json";

		public int SchemaVersion = 1;

		public List<PluginLocalOverride> Overrides = new();

		public static string GetPath(string projectPath) => Path.Combine(projectPath, FileName);

		/// <summary>
		/// Loads the file, or an empty set when there is none. Never throws: this file is per-machine and
		/// hand-editable, and a typo in it must not be able to stop a project from opening.
		/// </summary>
		public static PluginLocalOverrides LoadFrom(string projectPath)
		{
			try
			{
				var path = GetPath(projectPath);
				if (!File.Exists(path))
					return new PluginLocalOverrides();

				return Json.FromJson<PluginLocalOverrides>(File.ReadAllText(path)) ?? new PluginLocalOverrides();
			}
			catch (Exception ex)
			{
				PluginLog.Warn($"Could not read {FileName}: {ex.Message}. Ignoring local plugin overrides.");
				return new PluginLocalOverrides();
			}
		}

		/// <summary>
		/// Writes the file only when there is something to say, or when one is already there to update. Keeps a
		/// clone that adopted nothing from sprouting an empty per-machine file it never asked for.
		/// </summary>
		public void SaveIfMeaningful(string projectPath)
		{
			if (Overrides.Count > 0 || File.Exists(GetPath(projectPath)))
				SaveTo(projectPath);
		}

		public void SaveTo(string projectPath)
		{
			Overrides.Sort((a, b) => string.Compare(a?.Id, b?.Id, StringComparison.OrdinalIgnoreCase));
			File.WriteAllText(GetPath(projectPath), Json.ToJson(this, prettyPrint: true));

			EnsureGitIgnored(projectPath);
		}

		/// <summary>
		/// Adds this file to the project's .gitignore if it is not covered already. New projects are generated
		/// with the rule, but every project that predates this file would otherwise commit the one thing that
		/// must never be committed - and it would be committed by the person for whom it works.
		/// </summary>
		private static void EnsureGitIgnored(string projectPath)
		{
			try
			{
				var path = Path.Combine(projectPath, ".gitignore");
				if (!File.Exists(path))
					return; // Not a git checkout, or ignores are managed elsewhere - not ours to create.

				var text = File.ReadAllText(path);
				if (text.Contains(FileName, StringComparison.OrdinalIgnoreCase))
					return;

				var prefix = text.EndsWith("\n", StringComparison.Ordinal) ? "" : Environment.NewLine;

				File.AppendAllText(path,
					prefix + Environment.NewLine +
					"# Voltage Engine - which plugins THIS machine resolves from a folder of its own. Paths here" +
					Environment.NewLine +
					"# mean nothing on anyone else's machine, which is why they stay out of plugins.json." +
					Environment.NewLine + FileName + Environment.NewLine);

				PluginLog.Log($"Added {FileName} to the project's .gitignore.");
			}
			catch (Exception ex)
			{
				// Worth saying, never worth failing over: the file itself is already written.
				PluginLog.Warn($"Could not add {FileName} to .gitignore: {ex.Message}");
			}
		}

		public PluginLocalOverride FindById(string id) =>
			id == null ? null : Overrides.FirstOrDefault(o => string.Equals(o?.Id, id, StringComparison.OrdinalIgnoreCase));

		public void Upsert(string id, string path)
		{
			var existing = FindById(id);
			if (existing != null)
			{
				existing.Path = path;
				return;
			}

			Overrides.Add(new PluginLocalOverride { Id = id, Path = path });
		}

		public bool RemoveById(string id) =>
			Overrides.RemoveAll(o => string.Equals(o?.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;

		/// <summary>
		/// True when this source is meaningful only on the machine that wrote it - an absolute folder, or a
		/// relative one that climbs out of the project. A path that stays inside the repository travels with
		/// it, so a vendored plugin is shareable and stays in the committed file.
		/// </summary>
		public static bool IsMachineLocal(PluginSourceSpec source, string projectPath)
		{
			if (source == null || string.IsNullOrWhiteSpace(source.Path))
				return false;

			return !IsInsideProject(source.Path, projectPath);
		}

		/// <summary>True when a relative path resolves to somewhere inside the project folder.</summary>
		public static bool IsInsideProject(string path, string projectPath)
		{
			if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(projectPath))
				return false;

			if (Path.IsPathRooted(path))
				return false;

			try
			{
				var root = Path.GetFullPath(projectPath)
					.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

				var full = Path.GetFullPath(Path.Combine(root, path));

				return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// Moves any machine-local path out of the committed config and into the overrides, for projects
		/// written before the two were separated. Returns true when something moved, so the caller knows to
		/// save both files.
		/// </summary>
		public static bool Migrate(ProjectPluginsConfig config, PluginLocalOverrides overrides, string projectPath)
		{
			if (config?.Plugins == null)
				return false;

			var moved = false;

			foreach (var entry in config.Plugins)
			{
				if (entry?.Id == null || !IsMachineLocal(entry.Source, projectPath))
					continue;

				var path = entry.Source.Path;

				// Only adopt the path as this machine's if the folder is actually here. A committed absolute
				// path in a clone belongs to whoever committed it, not to whoever opened it - taking it over
				// would leave this machine holding an override pointing at a folder it does not have, which
				// then looks like a broken local setup instead of a plugin nobody published.
				if (Exists(path, projectPath))
				{
					overrides.Upsert(entry.Id, path);

					PluginLog.Log(
						$"'{entry.Id}' was listed in {ProjectPluginsConfig.FileName} as a folder on this machine, " +
						$"which no teammate can resolve. The path moved to {FileName} (ignored by git); the " +
						"project still records that it uses the plugin. Publish it, or vendor it into the " +
						"repository, to make it something the team can actually get.");
				}
				else
				{
					PluginLog.Warn(
						$"'{entry.Id}' is listed in {ProjectPluginsConfig.FileName} as '{path}', which does not " +
						"exist here - that is somebody else's folder, committed before local paths were kept out " +
						"of the project file. The project still records that it uses the plugin, but nothing can " +
						"fetch it until it is published or vendored.");
				}

				// Either way the declaration stays and the path goes: the project still uses this plugin, and
				// that is the half a teammate needs to know.
				entry.Source = new PluginSourceSpec();
				entry.Dev = false;
				moved = true;
			}

			return moved;
		}

		/// <summary>Whether a plugin folder is actually on this machine, absolute or relative to the project.</summary>
		private static bool Exists(string path, string projectPath)
		{
			if (string.IsNullOrWhiteSpace(path))
				return false;

			try
			{
				var full = Path.IsPathRooted(path)
					? path
					: Path.GetFullPath(Path.Combine(projectPath ?? string.Empty, path));

				return Directory.Exists(full);
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// Makes sure every plugin this machine has as a local override is at least named in the committed
		/// config, with no source attached.
		///
		/// <para>Without this, a plugin that was only ever added from a folder leaves no trace whatsoever in the
		/// repository - so a teammate, or your own clone, opens the project to an empty plugin list, no warning,
		/// and a scene full of missing components with nothing to connect them to.</para>
		/// </summary>
		public static bool DeclareLocalOnly(ProjectPluginsConfig config, PluginLocalOverrides overrides)
		{
			if (config == null || overrides == null)
				return false;

			var added = false;

			foreach (var local in overrides.Overrides)
			{
				if (string.IsNullOrWhiteSpace(local?.Id))
					continue;

				var declared = config.Plugins.Any(p =>
					string.Equals(p?.Id, local.Id, StringComparison.OrdinalIgnoreCase));

				if (declared)
					continue;

				config.Plugins.Add(new ProjectPluginEntry
				{
					Id = local.Id,
					Source = new PluginSourceSpec(),
				});

				added = true;
			}

			return added;
		}

		/// <summary>
		/// The entries to actually restore: everything the project declares, with an overridden plugin pointed
		/// at its local folder, plus the local-only plugins that exist on this machine and nowhere in the
		/// committed file.
		/// </summary>
		public static List<ProjectPluginEntry> Apply(ProjectPluginsConfig config, PluginLocalOverrides overrides,
			string projectPath)
		{
			var result = new List<ProjectPluginEntry>();
			var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var entry in config?.Plugins ?? new List<ProjectPluginEntry>())
			{
				if (entry?.Id == null)
					continue;

				var local = overrides?.FindById(entry.Id);
				if (local == null || string.IsNullOrWhiteSpace(local.Path))
				{
					result.Add(entry);
					continue;
				}

				// Same id, this machine's copy. Unpinned by construction: a folder being worked in has no
				// content hash worth holding anyone else to.
				used.Add(entry.Id);
				result.Add(new ProjectPluginEntry
				{
					Id = entry.Id,
					Source = new PluginSourceSpec { Path = local.Path },
					Dev = true,
					Disabled = entry.Disabled,
				});
			}

			foreach (var local in overrides?.Overrides ?? new List<PluginLocalOverride>())
			{
				if (local?.Id == null || used.Contains(local.Id) || string.IsNullOrWhiteSpace(local.Path))
					continue;

				// Declared nowhere but here: a plugin that exists only on this machine. It loads for whoever
				// has it and is invisible to everyone else, which is what the Plugin Manager says about it.
				result.Add(new ProjectPluginEntry
				{
					Id = local.Id,
					Source = new PluginSourceSpec { Path = local.Path },
					Dev = true,
				});
			}

			return result;
		}

		/// <summary>
		/// Ids a teammate would not be able to get: this machine has them as a folder, and the project names no
		/// source anyone could fetch from. Being named in the committed config is not enough - a declaration
		/// with no source is exactly the "someone has this and you cannot" case.
		/// </summary>
		public static HashSet<string> LocalOnlyIds(ProjectPluginsConfig config, PluginLocalOverrides overrides)
		{
			var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var local in overrides?.Overrides ?? new List<PluginLocalOverride>())
			{
				if (local?.Id == null)
					continue;

				var declared = config?.Plugins?.FirstOrDefault(p =>
					string.Equals(p?.Id, local.Id, StringComparison.OrdinalIgnoreCase));

				if (declared == null || declared.Source == null || declared.Source.IsUnset)
					ids.Add(local.Id);
			}

			return ids;
		}
	}

	public class PluginLocalOverride
	{
		public string Id;

		/// <summary>Folder holding the plugin, absolute or relative to the project.</summary>
		public string Path;
	}
}
