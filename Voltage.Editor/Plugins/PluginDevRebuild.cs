using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Voltage.Editor.Plugins
{
	/// <summary>
	/// Keeps locally-edited plugins in step with their own sources and with the editor they load into.
	///
	/// <para>A plugin added from its own source checkout is built once, when its declared assemblies are found
	/// missing (see <see cref="PluginSourceBuild"/>). Everything after that - every edit made to it, and every
	/// rebuild of the editor itself - leaves those assemblies exactly as they were, and the sync faithfully
	/// mirrors the stale ones into the project. The plugin you are in the middle of writing is then the one
	/// plugin guaranteed not to be running the code you just wrote.</para>
	///
	/// <para>So a checkout is rebuilt when its sources are newer than what was built from them, or when this
	/// editor's own assemblies are newer than the plugin's - the second is what makes a plugin come back rebuilt
	/// against the editor after the editor is rebuilt, rather than bound to whichever one happened to be there
	/// when it was added.</para>
	///
	/// <para>Timing is not a detail here: plugin assemblies load through <c>Assembly.LoadFrom</c>, which is not
	/// collectible, so nothing can be swapped in a running editor. A rebuild is only picked up if it happens
	/// before the plugins load - which is why this runs at the top of a restore, and why the editor's own build
	/// can run the same target ahead of time so the next launch has nothing left to do.</para>
	/// </summary>
	public static class PluginDevRebuild
	{
		/// <summary>Folders that hold build output or history rather than sources, and so never date a rebuild.</summary>
		private static readonly HashSet<string> IgnoredFolders = new(StringComparer.OrdinalIgnoreCase)
		{
			"bin", "obj", ".git", ".vs", ".idea", ".vscode", "lib", "editor-lib", "editor", "PluginLibs",
		};

		public sealed class Candidate
		{
			public string Id;
			public string Folder;
			public string ProjectPath;

			/// <summary>Why it needs building, for the log. Null when it is already up to date.</summary>
			public string Reason;
		}

		/// <summary>
		/// Rebuilds every local source checkout in <paramref name="entries"/> that has fallen behind. Never
		/// throws: a plugin that cannot be rebuilt is left exactly as it was, and the restore that follows
		/// reports it the same way it always did.
		/// </summary>
		/// <param name="force">Rebuild every checkout found, whether or not it looks stale.</param>
		/// <returns>How many plugins were rebuilt.</returns>
		public static int RebuildStale(IEnumerable<ProjectPluginEntry> entries, string projectPath, bool force = false)
		{
			var rebuilt = 0;

			foreach (var candidate in FindCandidates(entries, projectPath))
			{
				if (!force && candidate.Reason == null)
					continue;

				PluginLog.Log($"Rebuilding local plugin '{candidate.Id}' - {candidate.Reason ?? "forced"}.");

				string log;

				try
				{
					if (PluginSourceBuild.Rebuild(candidate.Folder, out log))
					{
						rebuilt++;
						PluginLog.Log($"Rebuilt '{candidate.Id}' from source.");
						continue;
					}
				}
				catch (Exception ex)
				{
					PluginLog.Error($"Could not rebuild '{candidate.Id}': {ex.Message}");
					continue;
				}

				// The previously built assemblies are still there, so the plugin loads as it did before - which
				// is the right outcome for a broken edit: the editor still opens, and the log says why it is
				// running the older build.
				PluginLog.Error(
					$"Could not rebuild '{candidate.Id}' - the editor will load the last assemblies that built. " +
					Tail(log, 400));
			}

			return rebuilt;
		}

		/// <summary>
		/// Every local source checkout among these entries, each marked with why it needs rebuilding (or not).
		/// Used by the rebuild pass and by the Plugin Manager, which shows the same set as "local".
		/// </summary>
		public static List<Candidate> FindCandidates(IEnumerable<ProjectPluginEntry> entries, string projectPath)
		{
			var candidates = new List<Candidate>();

			if (entries == null)
				return candidates;

			foreach (var entry in entries)
			{
				if (entry == null || entry.Disabled)
					continue;

				var folder = ResolveFolder(entry, projectPath);
				if (folder == null)
					continue;

				var project = PluginSourceBuild.FindPackagingProject(folder);
				if (project == null)
					continue;

				candidates.Add(new Candidate
				{
					Id = entry.Id,
					Folder = folder,
					ProjectPath = project,
					Reason = StaleReason(folder),
				});
			}

			return candidates;
		}

		/// <summary>True when this entry is a local folder holding sources and a project that can package them.</summary>
		public static bool IsLocalCheckout(ProjectPluginEntry entry, string projectPath)
		{
			var folder = ResolveFolder(entry, projectPath);
			return folder != null && PluginSourceBuild.FindPackagingProject(folder) != null;
		}

		/// <summary>The absolute folder a Path-source entry points at, or null for any other source kind.</summary>
		public static string ResolveFolder(ProjectPluginEntry entry, string projectPath)
		{
			var path = entry?.Source?.Path;
			if (string.IsNullOrWhiteSpace(path))
				return null;

			try
			{
				var full = Path.IsPathRooted(path) ? path : Path.Combine(projectPath ?? "", path);
				full = Path.GetFullPath(full);
				return Directory.Exists(full) ? full : null;
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		/// Why this checkout is behind, or null when it is current. Sources newer than the build is the obvious
		/// half; the editor being newer than the build is the half that matters after the editor is rebuilt,
		/// since a plugin compiled against the previous one is exactly what this is meant to stop shipping.
		/// </summary>
		private static string StaleReason(string folder)
		{
			var manifest = TryReadManifest(folder);
			if (manifest == null)
				return null;

			var built = DeclaredAssemblies(folder, manifest);
			if (built.Count == 0)
				return null;

			DateTime oldestBuild;

			try
			{
				if (built.Any(file => !File.Exists(file)))
					return "some declared assemblies are missing";

				oldestBuild = built.Min(file => File.GetLastWriteTimeUtc(file));
			}
			catch
			{
				return null;
			}

			var newestSource = NewestSourceTime(folder);
			if (newestSource > oldestBuild)
				return "local changes since it was last built";

			var editorTime = EditorAssemblyTime();
			if (editorTime > oldestBuild)
				return "it was built against an older editor";

			return null;
		}

		/// <summary>
		/// The most recent write among the files a build would actually read. Output and history folders are
		/// skipped, which is also what stops a rebuild from dating itself and running again on the next open.
		/// </summary>
		private static DateTime NewestSourceTime(string folder)
		{
			var newest = DateTime.MinValue;

			void Walk(string directory, int depth)
			{
				// Deep enough for any real repository, and a hard stop against a symlink loop.
				if (depth > 12)
					return;

				try
				{
					foreach (var file in Directory.EnumerateFiles(directory))
					{
						var time = File.GetLastWriteTimeUtc(file);
						if (time > newest)
							newest = time;
					}

					foreach (var child in Directory.EnumerateDirectories(directory))
					{
						if (IgnoredFolders.Contains(Path.GetFileName(child)))
							continue;

						Walk(child, depth + 1);
					}
				}
				catch
				{
					// An unreadable folder is not a reason to skip the rebuild decision for everything else.
				}
			}

			Walk(folder, 0);
			return newest;
		}

		/// <summary>When this editor's own assemblies were built - what a plugin has to be no older than.</summary>
		private static DateTime EditorAssemblyTime()
		{
			var newest = DateTime.MinValue;

			foreach (var name in new[] { "Voltage.dll", "Voltage.Editor.dll" })
			{
				try
				{
					var path = Path.Combine(PluginSourceBuild.EngineAssembliesPath(), name);
					if (!File.Exists(path))
						continue;

					var time = File.GetLastWriteTimeUtc(path);
					if (time > newest)
						newest = time;
				}
				catch
				{
					// Fall through: a missing timestamp just means this half of the check says nothing.
				}
			}

			return newest;
		}

		private static List<string> DeclaredAssemblies(string folder, PluginManifest manifest)
		{
			var declared = new List<string>();

			if (manifest.Gameplay != null)
			{
				declared.AddRange(manifest.Gameplay.ManagedAssemblies ?? new List<string>());
				declared.AddRange(manifest.Gameplay.EditorManagedAssemblies ?? new List<string>());
			}

			if (manifest.Editor != null)
				declared.AddRange(manifest.Editor.Assemblies ?? new List<string>());

			return declared
				.Where(rel => !string.IsNullOrWhiteSpace(rel))
				.Select(rel => Path.Combine(folder, PluginManifest.NormalizeRelative(rel)))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		private static PluginManifest TryReadManifest(string folder)
		{
			try
			{
				var path = Path.Combine(folder, PluginManifest.FileName);
				return File.Exists(path)
					? Voltage.Persistence.Json.FromJson<PluginManifest>(File.ReadAllText(path))
					: null;
			}
			catch
			{
				return null;
			}
		}

		private static string Tail(string log, int characters)
		{
			if (string.IsNullOrWhiteSpace(log))
				return "";

			var trimmed = log.Trim();
			return trimmed.Length <= characters ? trimmed : "..." + trimmed.Substring(trimmed.Length - characters);
		}
	}
}
