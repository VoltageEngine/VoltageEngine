using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Voltage.Editor.DebugUtils;

namespace Voltage.Editor.Scripting
{
	/// <summary>
	/// Responsible for copying the running engine's DLLs into a game project's EngineLibs folder.
	/// This ensures the Roslyn script compiler and the game project's IDE always reference the exact
	/// same assemblies the editor is running with, preventing stale-DLL issues.
	/// </summary>
	public static class EngineLibsSync
	{
		public const string EngineLibsFolderName = "EngineLibs";

		/// <summary>
		/// Whether the EngineLibs folder exists and contains at least the core Voltage DLLs.
		/// </summary>
		public static bool IsReady(string projectPath)
		{
			if (string.IsNullOrEmpty(projectPath))
				return false;

			var engineLibsPath = GetEngineLibsPath(projectPath);
			if (!Directory.Exists(engineLibsPath))
				return false;

			var voltageDlls = Directory.GetFiles(engineLibsPath, "Voltage.*.dll");
			return voltageDlls.Length > 0;
		}

		/// <summary>
		/// Returns the EngineLibs directory path for a given project.
		/// </summary>
		public static string GetEngineLibsPath(string projectPath)
		{
			return Path.Combine(projectPath, EngineLibsFolderName);
		}

		/// <summary>
		/// Copies all currently loaded Voltage and MonoGame/FNA assemblies (and their PDBs)
		/// into the project's EngineLibs folder. Returns the number of files copied.
		/// </summary>
		public static int SyncToProject(string projectPath)
		{
			if (string.IsNullOrEmpty(projectPath))
			{
				EditorDebug.Error("Cannot sync EngineLibs: project path is null or empty.", "EngineLibsSync");
				return 0;
			}

			var engineLibsPath = GetEngineLibsPath(projectPath);
			Directory.CreateDirectory(engineLibsPath);

			var assemblies = GetAssembliesToSync();
			int synced = 0;

			foreach (var assembly in assemblies)
			{
				if (string.IsNullOrEmpty(assembly.Location) || !File.Exists(assembly.Location))
					continue;

				try
				{
					var dllName = Path.GetFileName(assembly.Location);
					var destDll = Path.Combine(engineLibsPath, dllName);

					if (ShouldCopyFile(assembly.Location, destDll))
					{
						File.Copy(assembly.Location, destDll, overwrite: true);
						synced++;

						// Also copy the PDB if it exists (needed for script debugging)
						var sourcePdb = Path.ChangeExtension(assembly.Location, ".pdb");
						if (File.Exists(sourcePdb))
						{
							var destPdb = Path.Combine(engineLibsPath, Path.GetFileName(sourcePdb));
							File.Copy(sourcePdb, destPdb, overwrite: true);
						}
					}
				}
				catch (Exception ex)
				{
					EditorDebug.Error($"Failed to copy {assembly.GetName().Name}: {ex.Message}", "EngineLibsSync");
				}
			}

			// The source generator DLL is a Roslyn analyzer (netstandard2.0) and is never loaded
			// into the AppDomain, so it must be copied separately by file path.
			SyncSourceGeneratorDll(engineLibsPath);

			if (synced > 0)
				EditorDebug.Log($"Synced {synced} engine DLL(s) to: {engineLibsPath}", "EngineLibsSync");
			else
				EditorDebug.Log("EngineLibs already up to date.", "EngineLibsSync");

			return synced;
		}

		/// <summary>
		/// Determines whether the source file should be copied to the destination.
		/// Copies if the destination doesn't exist, or if file sizes or write times differ.
		/// </summary>
		private static bool ShouldCopyFile(string source, string dest)
		{
			if (!File.Exists(dest))
				return true;

			var sourceInfo = new FileInfo(source);
			var destInfo = new FileInfo(dest);

			if (sourceInfo.Length != destInfo.Length)
				return true;

			if (sourceInfo.LastWriteTimeUtc > destInfo.LastWriteTimeUtc)
				return true;

			return false;
		}

		/// <summary>
		/// The file name of the Roslyn source generator DLL.
		/// This assembly is never loaded into the editor's AppDomain (it is a netstandard2.0 analyzer),
		/// so it must be located and copied by file path rather than through reflection.
		/// </summary>
		public const string SourceGeneratorDllName = "Voltage.SourceGenerators.dll";

		/// <summary>
		/// Copies Voltage.SourceGenerators.dll into the project's EngineLibs folder.
		/// The generator is a Roslyn analyzer (netstandard2.0) and is never loaded into the
		/// editor's AppDomain, so it cannot be discovered via GetAssembliesToSync().
		/// We locate it by searching next to the currently executing editor assembly.
		/// Returns true if the file was found and copied (or was already up to date).
		/// </summary>
		private static bool SyncSourceGeneratorDll(string engineLibsPath)
		{
			// The generator DLL is deployed alongside the editor executable.
			var editorDir = Path.GetDirectoryName(typeof(EngineLibsSync).Assembly.Location);
			if (string.IsNullOrEmpty(editorDir))
				return false;

			var sourcePath = Path.Combine(editorDir, SourceGeneratorDllName);
			if (!File.Exists(sourcePath))
			{
				EditorDebug.Warn($"Source generator DLL not found at: {sourcePath}", "EngineLibsSync");
				return false;
			}

			var destPath = Path.Combine(engineLibsPath, SourceGeneratorDllName);
			if (!ShouldCopyFile(sourcePath, destPath))
				return true;

			try
			{
				File.Copy(sourcePath, destPath, overwrite: true);
				EditorDebug.Log($"Copied {SourceGeneratorDllName} to EngineLibs.", "EngineLibsSync");
				return true;
			}
			catch (Exception ex)
			{
				EditorDebug.Error($"Failed to copy {SourceGeneratorDllName}: {ex.Message}", "EngineLibsSync");
				return false;
			}
		}

		/// <summary>
		/// Collects all assemblies that should be synced: Voltage.* and MonoGame/FNA.
		/// Note: Voltage.SourceGenerators is intentionally excluded here because it is a
		/// netstandard2.0 Roslyn analyzer and is never loaded into the AppDomain at runtime.
		/// It is handled separately by SyncSourceGeneratorDll().
		/// </summary>
		private static List<Assembly> GetAssembliesToSync()
		{
			var result = new List<Assembly>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			// Voltage assemblies (excludes Voltage.SourceGenerators — it is never loaded at runtime)
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				var name = asm.GetName().Name;
				if (name == null)
					continue;

				if (name.StartsWith("Voltage", StringComparison.OrdinalIgnoreCase))
				{
					if (seen.Add(name))
						result.Add(asm);
				}
			}

			// MonoGame/FNA assemblies (scripts reference Vector2, Texture2D, etc.)
			var frameworkAssemblies = new[]
			{
				typeof(Microsoft.Xna.Framework.Vector2).Assembly,
				typeof(Microsoft.Xna.Framework.Graphics.Texture2D).Assembly,
			};

			foreach (var asm in frameworkAssemblies)
			{
				var name = asm.GetName().Name;
				if (name != null && seen.Add(name))
					result.Add(asm);
			}

			return result;
		}
	}
}
