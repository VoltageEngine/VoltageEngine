using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Voltage.Persistence;

namespace Voltage.Editor.Plugins
{
	/// <summary>
	/// Builds a plugin that was added from its own source checkout rather than from a release package.
	///
	/// <para>A published plugin is a folder holding <c>plugin.json</c> next to the assemblies it declares -
	/// <c>lib/</c>, <c>editor-lib/</c>, <c>editor/</c>. A plugin <em>repository</em> holds the same
	/// <c>plugin.json</c> next to the <em>sources</em>, and gitignores those folders, because they are
	/// release artifacts that CI builds and attaches to a tag. Clone such a repository and add it as a
	/// local folder and every declared assembly is missing, so the manifest fails to validate and the
	/// plugin cannot be added at all - which is correct, and completely unhelpful, since the sources and
	/// the project that builds them are sitting right there.</para>
	///
	/// <para>So: when a local folder is missing exactly the files its own packaging target produces, run
	/// that target. The build is the same one the release workflow runs, pointed at the engine assemblies
	/// of the editor doing the asking, so a plugin can never be built against a different Voltage than the
	/// one that will load it.</para>
	///
	/// <para>Only ever triggered by files being <em>absent</em>. Rebuilding whenever a source file looked
	/// newer would mean an unpredictable multi-minute build on project open; picking up your own edits
	/// stays an explicit rebuild, the same as it is for any other plugin.</para>
	/// </summary>
	public static class PluginSourceBuild
	{
		/// <summary>The MSBuild target a plugin repository exposes to stage its package layout.</summary>
		public const string PackageTarget = "PackagePlugin";

		public enum Outcome
		{
			/// <summary>Every declared assembly is already present - this is a package, not a source checkout.</summary>
			NotNeeded,

			/// <summary>The packaging target ran and produced what the manifest declares.</summary>
			Built,

			/// <summary>Files are missing and no project here knows how to produce them.</summary>
			NoPackagingProject,

			/// <summary>
			/// Buildable, but not from this call. Building shells out to MSBuild for minutes; on the thread
			/// that draws the editor that is indistinguishable from a hang, so only the install worker does it.
			/// </summary>
			NotBuilt,

			/// <summary>The packaging target ran and failed, or produced less than the manifest declares.</summary>
			Failed,
		}

		public class Result
		{
			public Outcome Outcome;

			/// <summary>Manifest-declared payload files that were missing before the build.</summary>
			public List<string> MissingBefore = new();

			/// <summary>Still missing after the build, if one ran.</summary>
			public List<string> MissingAfter = new();

			/// <summary>The project the packaging target was found in, if any.</summary>
			public string ProjectPath;

			/// <summary>Build output worth showing when it failed.</summary>
			public string BuildLog;

			/// <summary>True when this folder looks like a plugin source checkout rather than a package.</summary>
			public bool IsSourceCheckout => Outcome != Outcome.NotNeeded;
		}

		/// <summary>
		/// Stages the package layout when - and only when - the manifest declares assemblies this folder
		/// does not have. Never throws: the caller's manifest validation is what reports a folder that is
		/// still incomplete, and this only adds the context for why.
		/// </summary>
		/// <param name="allowBuild">
		/// False on any path that must stay responsive. The build takes minutes, so running it from the
		/// thread that draws the editor looks exactly like a crash; those callers report what is missing
		/// instead and leave the building to the install worker.
		/// </param>
		public static Result EnsureBuilt(string pluginFolder, bool allowBuild)
		{
			var result = new Result();

			var manifest = TryReadManifest(pluginFolder);
			if (manifest == null)
				return result; // No readable manifest: not ours to diagnose, LoadFrom will say so properly.

			result.MissingBefore = MissingPayloadFiles(pluginFolder, manifest);
			if (result.MissingBefore.Count == 0)
			{
				result.Outcome = Outcome.NotNeeded;
				return result;
			}

			result.ProjectPath = FindPackagingProject(pluginFolder);
			if (result.ProjectPath == null)
			{
				result.Outcome = Outcome.NoPackagingProject;
				result.MissingAfter = result.MissingBefore;
				return result;
			}

			if (!allowBuild)
			{
				result.Outcome = Outcome.NotBuilt;
				result.MissingAfter = result.MissingBefore;
				return result;
			}

			PluginLog.Log(
				$"'{manifest.Id}' is a source checkout - {result.MissingBefore.Count} declared file(s) are not " +
				$"built yet. Running {PackageTarget} in {Path.GetFileName(result.ProjectPath)}; this can take a " +
				"few minutes the first time.");

			var built = RunPackaging(pluginFolder, result.ProjectPath, out var log);
			result.BuildLog = log;
			result.MissingAfter = MissingPayloadFiles(pluginFolder, manifest);

			if (built && result.MissingAfter.Count == 0)
			{
				result.Outcome = Outcome.Built;
				PluginLog.Log($"Built '{manifest.Id}' from source.");
				return result;
			}

			result.Outcome = Outcome.Failed;
			PluginLog.Error($"Could not build '{manifest.Id}' from source. {Tail(log, 400)}");
			return result;
		}

		/// <summary>
		/// What the user should do about it, in the terms of their own checkout. Every one of these
		/// otherwise surfaces as "file 'lib/Whatever.dll' not found in the package", which reads as the
		/// plugin being broken rather than unbuilt.
		/// </summary>
		public static string Explain(Result result, string pluginFolder)
		{
			var missing = string.Join(", ", result.MissingAfter.Take(4))
			              + (result.MissingAfter.Count > 4 ? ", ..." : "");

			switch (result.Outcome)
			{
				case Outcome.NotBuilt:
					return
						$"This is a plugin source checkout and its assemblies are not built yet: plugin.json " +
						$"declares {missing}, which a plugin repository gitignores because CI builds them for a " +
						"tagged release.\n\n" +
						"Add it again from Plugin Manager > Add Plugin - that builds it in the background, with " +
						"progress, instead of stalling the editor. Or build it yourself:\n" +
						$"    dotnet build \"{result.ProjectPath}\" -t:{PackageTarget} " +
						$"-p:VoltageEnginePath=\"{EngineAssembliesPath()}\"";

				case Outcome.NoPackagingProject:
					return
						$"This looks like a plugin source checkout rather than a built package: plugin.json declares " +
						$"{missing}, which {pluginFolder} does not contain. Those are release artifacts - a plugin " +
						"repository gitignores them and CI builds them for a tagged release.\n\n" +
						$"There is no project here exposing a '{PackageTarget}' target, so the editor cannot build " +
						"them for you. Either build the plugin by hand and add the folder again, or install the " +
						"published release instead (Plugin Manager > Browse Plugins).";

				case Outcome.Failed:
					return
						$"This is a plugin source checkout, and building it did not produce {missing}.\n\n" +
						$"Build it yourself to see the full errors:\n" +
						$"    dotnet build \"{result.ProjectPath}\" -t:{PackageTarget} " +
						$"-p:VoltageEnginePath=\"{EngineAssembliesPath()}\"\n\n" +
						Tail(result.BuildLog, 800);

				default:
					return null;
			}
		}

		/// <summary>
		/// Manifest-declared payload files that are not on disk. Read without validating, because an
		/// unbuilt checkout is exactly the case where validation refuses to hand back a manifest.
		/// </summary>
		private static List<string> MissingPayloadFiles(string pluginFolder, PluginManifest manifest)
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
				.Where(rel => !File.Exists(Path.Combine(pluginFolder, PluginManifest.NormalizeRelative(rel))))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		private static PluginManifest TryReadManifest(string pluginFolder)
		{
			try
			{
				var path = Path.Combine(pluginFolder, PluginManifest.FileName);
				return File.Exists(path) ? Json.FromJson<PluginManifest>(File.ReadAllText(path)) : null;
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		/// The project at the plugin root that exposes the packaging target. Root only: a project nested in
		/// a source folder builds one assembly, while the target that stages the whole package is by
		/// convention next to plugin.json.
		/// </summary>
		public static string FindPackagingProject(string pluginFolder)
		{
			IEnumerable<string> candidates;
			try
			{
				candidates = Directory.EnumerateFiles(pluginFolder, "*.*proj", SearchOption.TopDirectoryOnly);
			}
			catch
			{
				return null;
			}

			// Deterministic across machines: EnumerateFiles order is filesystem-dependent, and picking a
			// different project on someone else's clone would be a miserable thing to debug.
			foreach (var project in candidates.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
			{
				try
				{
					var text = File.ReadAllText(project);
					if (text.Contains($"\"{PackageTarget}\"", StringComparison.Ordinal))
						return project;
				}
				catch
				{
					// Unreadable project file - try the next one.
				}
			}

			return null;
		}

		/// <summary>
		/// Restores, then runs the packaging target. Restore is separate because the target drives msbuild
		/// directly for each configuration, and msbuild does not restore on its own - the release workflow
		/// does the same two steps for the same reason.
		/// </summary>
		private static bool RunPackaging(string pluginFolder, string projectPath, out string log)
		{
			var output = new StringBuilder();
			var engine = EngineAssembliesPath();

			// Both properties point at this editor's own folder: it is self-contained, so the engine
			// assemblies, the editor assembly and ImGui all sit together there. A plugin built against
			// anything else could load into an editor whose Voltage.dll disagrees with it.
			var properties = $"-p:VoltageEnginePath=\"{engine}\" -p:VoltageEditorPath=\"{engine}\"";

			foreach (var project in Directory.EnumerateFiles(pluginFolder, "*.*proj", SearchOption.TopDirectoryOnly)
				         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
			{
				if (!RunDotnet(pluginFolder, $"restore \"{project}\" {properties}", output))
				{
					log = output.ToString();
					return false;
				}
			}

			var ok = RunDotnet(pluginFolder,
				$"msbuild \"{projectPath}\" -t:{PackageTarget} {properties} -nologo -verbosity:minimal", output);

			log = output.ToString();
			return ok;
		}

		/// <summary>Folder holding the engine/editor assemblies a plugin must bind to: this editor's own.</summary>
		public static string EngineAssembliesPath() =>
			AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		private static bool RunDotnet(string workingDirectory, string arguments, StringBuilder output)
		{
			try
			{
				var info = new ProcessStartInfo
				{
					FileName = "dotnet",
					Arguments = arguments,
					WorkingDirectory = workingDirectory,
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					CreateNoWindow = true,
				};

				// A plugin build is not the place to discover the SDK wants to phone home or print a
				// first-run banner into the log we are about to show the user.
				info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
				info.Environment["DOTNET_NOLOGO"] = "1";

				using var process = Process.Start(info);
				if (process == null)
				{
					output.AppendLine("Could not start dotnet. Is the .NET SDK installed and on PATH?");
					return false;
				}

				var stdout = process.StandardOutput.ReadToEndAsync();
				var stderr = process.StandardError.ReadToEndAsync();

				// Generous: a first build restores packages and compiles the engine-facing projects twice.
				if (!process.WaitForExit(10 * 60 * 1000))
				{
					try { process.Kill(true); } catch { /* already gone */ }
					output.AppendLine($"dotnet {arguments.Split(' ')[0]} timed out after 10 minutes.");
					return false;
				}

				output.Append(stdout.Result);
				output.Append(stderr.Result);
				return process.ExitCode == 0;
			}
			catch (Exception ex)
			{
				output.AppendLine($"Could not run dotnet: {ex.Message}");
				return false;
			}
		}

		/// <summary>The end of a build log - where MSBuild puts the errors and the summary.</summary>
		private static string Tail(string log, int characters)
		{
			if (string.IsNullOrWhiteSpace(log))
				return "";

			var trimmed = log.Trim();
			return trimmed.Length <= characters ? trimmed : "..." + trimmed.Substring(trimmed.Length - characters);
		}
	}
}
