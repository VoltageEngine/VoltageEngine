using System;
using System.IO;
using System.Text.RegularExpressions;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.ProjectFile;

namespace Voltage.Editor.Plugins
{
	/// <summary>
	/// One-time, idempotent migration of pre-plugin-system projects. Farseer used to be a hard engine
	/// reference (force-loaded by the editor, synced into EngineLibs, referenced by the game csproj);
	/// it is a bundled plugin now. To keep every existing project working with zero user action:
	/// <list type="bullet">
	///   <item>projects without a plugins.json get one with the bundled Farseer plugin enabled
	///     (matching the old always-loaded behavior; one-click disable in the Plugin Manager)</item>
	///   <item>the legacy <c>&lt;Reference Include="Voltage.FarseerPhysics"&gt;</c> EngineLibs block is
	///     stripped from the game csproj (the plugin props supply the reference instead — leaving both
	///     would double-reference the assembly)</item>
	///   <item>the stale EngineLibs copy of the DLL is deleted so nothing resolves against it</item>
	/// </list>
	/// </summary>
	public static class PluginMigration
	{
		private const string FarseerPluginId = "voltage.farseer";
		private const string FarseerAssemblyName = "Voltage.FarseerPhysics";

		/// <summary>Runs before plugin restore on every project open. Idempotent and failure-tolerant.</summary>
		public static void MigrateProjectIfNeeded(IGameProject project)
		{
			try
			{
				EnsurePluginsConfig(project.ProjectPath);
				RemoveLegacyFarseerReference(project);
				RemoveStaleFarseerEngineLib(project.ProjectPath);
			}
			catch (Exception ex)
			{
				EditorDebug.Error($"Plugin migration failed (project still opens): {ex.Message}", "Plugins");
			}
		}

		/// <summary>
		/// Creates a default plugins.json with Farseer enabled for projects that predate the plugin
		/// system. Projects that already have a plugins.json are the author's business — never edited.
		/// </summary>
		private static void EnsurePluginsConfig(string projectPath)
		{
			if (File.Exists(ProjectPluginsConfig.GetPath(projectPath)))
				return;

			var config = new ProjectPluginsConfig();
			config.Plugins.Add(new ProjectPluginEntry
			{
				Id = FarseerPluginId,
				Source = new PluginSourceSpec { Bundled = true },
			});
			config.SaveTo(projectPath);

			EditorDebug.Log(
				"Created plugins.json with the bundled Farseer plugin enabled (pre-plugin-system project migration). " +
				"Disable it in the Plugin Manager if this project does not use FS* physics components.", "Plugins");
		}

		/// <summary>
		/// Strips the legacy EngineLibs-based Farseer Reference block from the game csproj.
		/// String-level edit in the style of GameBuilder.EnsureGenerateAssemblyInfoDisabled.
		/// </summary>
		private static void RemoveLegacyFarseerReference(IGameProject project)
		{
			var csprojPath = Path.Combine(project.ProjectPath, $"{project.ProjectName}.csproj");
			if (!File.Exists(csprojPath))
				return;

			var content = File.ReadAllText(csprojPath);

			// Only target the legacy EngineLibs HintPath form — a PluginLibs reference (from
			// Plugins.g.props) never appears in the csproj itself.
			var pattern = new Regex(
				"[ \\t]*<Reference\\s+Include=\"" + Regex.Escape(FarseerAssemblyName) + "\"\\s*>" +
				".*?EngineLibs.*?</Reference>\\r?\\n?",
				RegexOptions.Singleline);

			if (!pattern.IsMatch(content))
				return;

			content = pattern.Replace(content, "");
			File.WriteAllText(csprojPath, content);
			EditorDebug.Log("Removed legacy Voltage.FarseerPhysics EngineLibs reference from the game csproj (now supplied by the Farseer plugin).", "Plugins");
		}

		/// <summary>Deletes the stale EngineLibs copy (and PDB) left behind by earlier editor versions.</summary>
		private static void RemoveStaleFarseerEngineLib(string projectPath)
		{
			var engineLibs = Path.Combine(projectPath, Scripting.EngineLibsSync.EngineLibsFolderName);
			foreach (var ext in new[] { ".dll", ".pdb" })
			{
				var path = Path.Combine(engineLibs, FarseerAssemblyName + ext);
				if (!File.Exists(path))
					continue;

				try
				{
					File.Delete(path);
					EditorDebug.Log($"Deleted stale EngineLibs/{FarseerAssemblyName}{ext} (Farseer is a plugin now).", "Plugins");
				}
				catch (Exception ex)
				{
					// Locked because it was loaded by an older editor session — harmless, retried next open.
					EditorDebug.Warn($"Could not delete stale {path}: {ex.Message}", "Plugins");
				}
			}
		}
	}
}
