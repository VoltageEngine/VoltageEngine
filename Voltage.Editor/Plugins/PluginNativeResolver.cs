using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Voltage.Editor.DebugUtils;

namespace Voltage.Editor.Plugins
{
	/// <summary>
	/// Makes a plugin's native libraries loadable inside the editor process without copying them next to
	/// the editor binary: registers a per-assembly DllImport resolver probing the plugin's
	/// <c>PluginLibs/&lt;id&gt;/native/&lt;current rid&gt;/</c> folder, and eagerly loads each listed native file
	/// so transitive dlopen dependencies (e.g. fmod.dll needed by fmodstudio.dll) resolve too.
	/// </summary>
	public static class PluginNativeResolver
	{
		/// <summary>SetDllImportResolver throws if called twice for the same assembly — remember who has one.</summary>
		private static readonly HashSet<Assembly> _registered = new();

		/// <summary>Native search directories per registered assembly.</summary>
		private static readonly Dictionary<Assembly, List<string>> _searchDirs = new();

		/// <summary>
		/// Registers native resolution for a plugin's managed assembly. Safe to call for plugins without
		/// natives (no-op) and repeatedly for the same assembly (subsequent dirs are appended).
		/// </summary>
		public static void Register(Assembly assembly, string payloadPath, PluginManifest manifest)
		{
			var nativeDirs = GetNativeDirsForCurrentPlatform(payloadPath, manifest);
			if (nativeDirs.Count == 0)
				return;

			lock (_registered)
			{
				if (_searchDirs.TryGetValue(assembly, out var existing))
				{
					foreach (var dir in nativeDirs)
					{
						if (!existing.Contains(dir, StringComparer.OrdinalIgnoreCase))
							existing.Add(dir);
					}
				}
				else
				{
					_searchDirs[assembly] = nativeDirs;
				}

				if (_registered.Add(assembly))
					NativeLibrary.SetDllImportResolver(assembly, ResolveNativeLibrary);
			}

			// Eager-load every native file listed for this platform so libraries that dlopen each other
			// (rather than P/Invoke) find their dependencies already in the process.
			foreach (var dir in nativeDirs)
			{
				foreach (var file in Directory.EnumerateFiles(dir))
				{
					if (!IsNativeLibraryFile(file))
						continue;

					if (NativeLibrary.TryLoad(file, out _))
						EditorDebug.Log($"Preloaded plugin native: {Path.GetFileName(file)}", "Plugins");
					else
						EditorDebug.Warn($"Could not preload plugin native '{file}' — P/Invoke resolution will still be attempted on demand.", "Plugins");
				}
			}
		}

		private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
		{
			List<string> dirs;
			lock (_registered)
			{
				if (!_searchDirs.TryGetValue(assembly, out dirs))
					return IntPtr.Zero;
			}

			foreach (var dir in dirs)
			{
				foreach (var candidate in CandidateFileNames(libraryName))
				{
					var path = Path.Combine(dir, candidate);
					if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
						return handle;
				}
			}

			return IntPtr.Zero; // Fall through to the default OS probing.
		}

		/// <summary>Platform-appropriate file name candidates for a P/Invoke library name.</summary>
		private static IEnumerable<string> CandidateFileNames(string libraryName)
		{
			yield return libraryName;

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				if (!libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
					yield return libraryName + ".dll";
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				if (!libraryName.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))
					yield return libraryName + ".dylib";
				if (!libraryName.StartsWith("lib", StringComparison.Ordinal))
				{
					yield return "lib" + libraryName + ".dylib";
					yield return "lib" + libraryName;
				}
			}
			else
			{
				if (!libraryName.EndsWith(".so", StringComparison.OrdinalIgnoreCase))
					yield return libraryName + ".so";
				if (!libraryName.StartsWith("lib", StringComparison.Ordinal))
				{
					yield return "lib" + libraryName + ".so";
					yield return "lib" + libraryName;
				}
			}
		}

		/// <summary>
		/// Existing native directories matching the current OS + architecture from the manifest's
		/// declared native sets (e.g. "osx-arm64" while editing on an Apple Silicon Mac).
		/// </summary>
		private static List<string> GetNativeDirsForCurrentPlatform(string payloadPath, PluginManifest manifest)
		{
			var result = new List<string>();
			var sets = manifest?.Gameplay?.Natives;
			if (sets == null || sets.Count == 0)
				return result;

			var currentRid = GetCurrentPortableRid();

			foreach (var set in sets)
			{
				if (!string.Equals(set.Rid, currentRid, StringComparison.OrdinalIgnoreCase))
					continue;

				// Files are globs like "native/win-x64/*.dll" — probe their parent directories.
				foreach (var glob in set.Files ?? Enumerable.Empty<string>())
				{
					var relDir = Path.GetDirectoryName(PluginManifest.NormalizeRelative(glob));
					if (string.IsNullOrEmpty(relDir))
						continue;

					var dir = Path.Combine(payloadPath, relDir);
					if (Directory.Exists(dir) && !result.Contains(dir, StringComparer.OrdinalIgnoreCase))
						result.Add(dir);
				}
			}

			return result;
		}

		/// <summary>Portable RID for the running editor process ("win-x64", "osx-arm64", "linux-x64", …).</summary>
		public static string GetCurrentPortableRid()
		{
			string os;
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				os = "win";
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				os = "osx";
			else
				os = "linux";

			var arch = RuntimeInformation.ProcessArchitecture switch
			{
				Architecture.X64 => "x64",
				Architecture.X86 => "x86",
				Architecture.Arm64 => "arm64",
				Architecture.Arm => "arm",
				_ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
			};

			return $"{os}-{arch}";
		}

		private static bool IsNativeLibraryFile(string path)
		{
			return path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
				|| path.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
				|| path.Contains(".so.", StringComparison.OrdinalIgnoreCase)
				|| path.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase);
		}
	}
}
