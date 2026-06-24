using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Voltage.Diagnostics
{
	/// <summary>The outcome of checking a single dependency.</summary>
	public sealed class DependencyStatus
	{
		public NativeDependency Dependency { get; init; }
		public bool IsPresent { get; init; }
	}

	/// <summary>The aggregate result of a preflight check over a set of dependencies.</summary>
	public sealed class DependencyCheckResult
	{
		public IReadOnlyList<DependencyStatus> Statuses { get; init; } = Array.Empty<DependencyStatus>();
		public PackageManagerKind PackageManager { get; init; }
		public string DistroName { get; init; }
		public bool ImmutableRoot { get; init; }

		public IEnumerable<DependencyStatus> Missing => Statuses.Where(s => !s.IsPresent);
		public bool AllPresent => Statuses.All(s => s.IsPresent);
		public bool AnyCriticalMissing => Missing.Any(s => s.Dependency.Severity == DependencySeverity.Critical);

		/// <summary>
		/// The packages to install for every missing dependency that has a curated name for the detected
		/// package manager. Distinct, in catalog order. Empty when the package manager is unknown.
		/// </summary>
		public IReadOnlyList<string> MissingPackages()
		{
			if (PackageManager == PackageManagerKind.None)
				return Array.Empty<string>();

			var seen = new HashSet<string>(StringComparer.Ordinal);
			var result = new List<string>();
			foreach (var status in Missing)
			{
				var pkg = status.Dependency.PackageFor(PackageManager);
				if (!string.IsNullOrEmpty(pkg) && seen.Add(pkg))
					result.Add(pkg);
			}
			return result;
		}

		/// <summary>
		/// True when an opt-in package-manager install is worth offering as a button. Supported on Linux
		/// (via pkexec/polkit) and Windows (via winget). macOS CLT is installed via xcode-select (handled as
		/// a manual instruction, not this button). Never offered on a read-only root (SteamOS).
		/// </summary>
		public bool CanAttemptAutoInstall
		{
			get
			{
				if (ImmutableRoot || MissingPackages().Count == 0)
					return false;

				return LinuxPackageManager.Platform switch
				{
					HostPlatform.Linux =>
						PackageManager != PackageManagerKind.None && LinuxPackageManager.ExecutableExists("pkexec"),
					HostPlatform.Windows =>
						PackageManager == PackageManagerKind.Winget && LinuxPackageManager.ExecutableExists("winget"),
					_ => false
				};
			}
		}

		/// <summary>
		/// Missing dependencies that have a manual instruction and/or a download URL but no package-manager
		/// install for the detected manager — i.e. the user must follow steps / open a link manually.
		/// </summary>
		public IEnumerable<DependencyStatus> ManualOnlyMissing => Missing.Where(s =>
			string.IsNullOrEmpty(s.Dependency.PackageFor(PackageManager)) &&
			(!string.IsNullOrEmpty(s.Dependency.ManualInstruction) || !string.IsNullOrEmpty(s.Dependency.DownloadUrl)));
	}

	/// <summary>
	/// Detects which native dependencies are present and, on explicit user request, installs the missing
	/// ones via the system package manager behind a graphical polkit (pkexec) prompt.
	/// </summary>
	/// <remarks>
	/// Detection is reliable without pulling in extra native dependencies: executables are found on PATH and
	/// shared libraries are matched against the dynamic linker cache reported by <c>ldconfig -p</c>, with a
	/// best-effort <c>NativeLibrary.TryLoad</c> fallback. The whole system is a trivial pass on Windows/macOS.
	/// </remarks>
	public static class NativeDependencyChecker
	{
		private static string[] _ldconfigCache;

		/// <summary>
		/// Runs a preflight over the given dependency set and returns the aggregate result. On non-Linux
		/// platforms every dependency is reported present (the editor ships its own libs there).
		/// </summary>
		public static DependencyCheckResult Check(IReadOnlyList<NativeDependency> dependencies)
		{
			var pm = LinuxPackageManager.Detect();
			var statuses = new List<DependencyStatus>(dependencies.Count);

			foreach (var dep in dependencies)
			{
				// Run real detection on every platform. The catalog already supplies an empty set where a
				// platform has nothing to check (e.g. runtime deps on Windows/macOS), so this stays trivial
				// there while build-toolchain deps are genuinely verified on Windows and macOS.
				bool present = IsPresent(dep);
				statuses.Add(new DependencyStatus { Dependency = dep, IsPresent = present });
			}

			return new DependencyCheckResult
			{
				Statuses = statuses,
				PackageManager = pm,
				DistroName = LinuxPackageManager.DistroPrettyName,
				ImmutableRoot = LinuxPackageManager.IsImmutableRootFilesystem()
			};
		}

		/// <summary>Re-reads the ldconfig cache and re-checks (used after an install attempt).</summary>
		public static DependencyCheckResult ReCheck(IReadOnlyList<NativeDependency> dependencies)
		{
			_ldconfigCache = null;
			_linkerSearchDirs = null;
			return Check(dependencies);
		}

		private static bool IsPresent(NativeDependency dep)
		{
			return dep.Kind switch
			{
				DependencyKind.Executable => ExecutablePresent(dep),
				DependencyKind.SharedLibrary => SharedLibraryPresent(dep),
				DependencyKind.LinkLibrary => LinkLibraryPresent(dep),
				_ => false
			};
		}

		/// <summary>
		/// Verifies a library is LINKABLE (not merely present at runtime). For a linker name like "z"
		/// (as in <c>-lz</c>) the linker resolves to the UNVERSIONED dev artifact <c>libz.so</c> or the
		/// static <c>libz.a</c> — shipped by the distro's <c>-dev</c>/<c>-devel</c> package. The versioned
		/// runtime soname <c>libz.so.1</c> does NOT satisfy the linker, which is exactly why the user saw
		/// "cannot find -lz" despite libz.so.1 being installed and our old runtime probe passing.
		/// </summary>
		private static bool LinkLibraryPresent(NativeDependency dep)
		{
			if (!LinuxPackageManager.IsLinux)
				return true; // On Windows/macOS link deps are modeled as executables / SDKs, not -l libs.

			var name = dep.Probe; // e.g. "z", "SDL2"
			if (string.IsNullOrEmpty(name))
				return false;

			var unversioned = $"lib{name}.so"; // libz.so  (must be the bare symlink, NOT libz.so.1)
			var staticLib = $"lib{name}.a";    // libz.a

			foreach (var dir in GetLinkerSearchDirs())
			{
				try
				{
					if (File.Exists(Path.Combine(dir, unversioned)) ||
					    File.Exists(Path.Combine(dir, staticLib)))
						return true;
				}
				catch
				{
					// Ignore unreadable dirs.
				}
			}

			return false;
		}

		private static string[] _linkerSearchDirs;

		/// <summary>
		/// Builds the set of directories the system linker searches for <c>-l</c> libraries: the compiler's
		/// own library dirs (via <c>cc -print-search-dirs</c>), the directories from <c>ldconfig -p</c>, and
		/// the conventional lib roots (incl. Debian/Ubuntu multiarch). Cached per check.
		/// </summary>
		private static string[] GetLinkerSearchDirs()
		{
			if (_linkerSearchDirs != null)
				return _linkerSearchDirs;

			var dirs = new HashSet<string>(StringComparer.Ordinal);

			// Conventional roots, including 64-bit and common Debian/Ubuntu multiarch triplets.
			foreach (var d in new[]
			{
				"/usr/lib", "/usr/lib64", "/lib", "/lib64",
				"/usr/local/lib", "/usr/local/lib64",
				"/usr/lib/x86_64-linux-gnu", "/usr/lib/aarch64-linux-gnu",
				"/usr/lib/i386-linux-gnu"
			})
			{
				if (Directory.Exists(d)) dirs.Add(d);
			}

			// Directories that ldconfig reports (covers distro-specific lib paths).
			foreach (var line in GetLdconfigCache())
			{
				var arrow = line.IndexOf("=> ", StringComparison.Ordinal);
				if (arrow < 0) continue;
				var full = line.Substring(arrow + 3).Trim();
				try
				{
					var dir = Path.GetDirectoryName(full);
					if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
						dirs.Add(dir);
				}
				catch { /* ignore */ }
			}

			// The compiler driver's own search dirs ("cc -print-search-dirs" → "libraries: =a:b:c").
			foreach (var d in GetCompilerLibraryDirs())
			{
				if (Directory.Exists(d)) dirs.Add(d);
			}

			_linkerSearchDirs = dirs.ToArray();
			return _linkerSearchDirs;
		}

		private static IReadOnlyList<string> GetCompilerLibraryDirs()
		{
			foreach (var cc in new[] { "cc", "gcc", "clang" })
			{
				if (!LinuxPackageManager.ExecutableExists(cc))
					continue;

				var dirs = new List<string>();
				try
				{
					var psi = new ProcessStartInfo
					{
						FileName = cc,
						Arguments = "-print-search-dirs",
						RedirectStandardOutput = true,
						RedirectStandardError = true,
						UseShellExecute = false,
						CreateNoWindow = true
					};
					using var p = Process.Start(psi);
					if (p == null) continue;
					var output = p.StandardOutput.ReadToEnd();
					p.WaitForExit(4000);

					foreach (var raw in output.Split('\n'))
					{
						var line = raw.Trim();
						if (!line.StartsWith("libraries:", StringComparison.Ordinal))
							continue;
						// Format: "libraries: =/path/a:/path/b:..."
						var eq = line.IndexOf('=');
						var list = eq >= 0 ? line.Substring(eq + 1) : line.Substring("libraries:".Length);
						foreach (var part in list.Split(':', StringSplitOptions.RemoveEmptyEntries))
						{
							try { dirs.Add(Path.GetFullPath(part.Trim())); }
							catch { /* skip malformed path entry */ }
						}
					}
				}
				catch
				{
					// Try the next compiler.
					continue;
				}

				// First working compiler is enough.
				return dirs;
			}

			return Array.Empty<string>();
		}

		private static bool ExecutablePresent(NativeDependency dep)
		{
			if (LinuxPackageManager.ExecutableExists(dep.Probe))
				return true;
			return dep.AltProbes.Any(LinuxPackageManager.ExecutableExists);
		}

		private static bool SharedLibraryPresent(NativeDependency dep)
		{
			// 1) ldconfig cache: authoritative for libraries registered with the dynamic linker.
			var cache = GetLdconfigCache();
			if (cache.Length > 0)
			{
				if (LibInCache(cache, dep.Probe) || dep.AltProbes.Any(p => LibInCache(cache, p)))
					return true;
			}

			// 2) Fallback: ask the runtime loader to resolve the soname directly. This catches libs that are
			//    present but not in the ldconfig cache (e.g. on minimal containers without a refreshed cache).
			if (TryLoadLibrary(dep.Probe) || dep.AltProbes.Any(TryLoadLibrary))
				return true;

			return false;
		}

		private static bool LibInCache(string[] cache, string token) =>
			cache.Any(line => line.Contains(token, StringComparison.Ordinal));

		private static bool TryLoadLibrary(string soname)
		{
			if (string.IsNullOrEmpty(soname))
				return false;
			try
			{
				if (NativeLibrary.TryLoad(soname, out var handle))
				{
					// We intentionally do NOT free: keeping the handle is harmless and avoids any chance of
					// unloading a library the host process is already using.
					return handle != IntPtr.Zero;
				}
			}
			catch
			{
				// TryLoad shouldn't throw, but guard anyway.
			}
			return false;
		}

		private static string[] GetLdconfigCache()
		{
			if (_ldconfigCache != null)
				return _ldconfigCache;

			if (!LinuxPackageManager.IsLinux)
				return _ldconfigCache = Array.Empty<string>();

			try
			{
				var psi = new ProcessStartInfo
				{
					FileName = "ldconfig",
					Arguments = "-p",
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
					CreateNoWindow = true
				};
				using var p = Process.Start(psi);
				if (p == null)
					return _ldconfigCache = Array.Empty<string>();

				var output = p.StandardOutput.ReadToEnd();
				p.WaitForExit(4000);
				_ldconfigCache = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
			}
			catch
			{
				// ldconfig not found / not permitted — fall back to dlopen-only detection.
				_ldconfigCache = Array.Empty<string>();
			}

			return _ldconfigCache;
		}

		/// <summary>The result of an auto-install attempt.</summary>
		public sealed class InstallOutcome
		{
			public bool Started { get; init; }
			public bool Succeeded { get; init; }
			public int ExitCode { get; init; }
			public string Message { get; init; }
		}

		/// <summary>
		/// Attempts to install the missing packages from <paramref name="result"/> via the detected package
		/// manager. On Linux this is elevated through <c>pkexec</c> (graphical polkit prompt); on Windows it
		/// runs <c>winget install</c> (which prompts via UAC itself). MUST only be called as the direct
		/// consequence of an explicit user action. Best-effort: never throws, returns a structured outcome so
		/// the caller can fall back to manual instructions / download links.
		/// </summary>
		public static InstallOutcome TryAutoInstall(DependencyCheckResult result)
		{
			if (result.ImmutableRoot)
				return Fail("The root filesystem is read-only (SteamOS). Run 'sudo steamos-readonly disable' first, then install manually.");

			var packages = result.MissingPackages();
			if (packages.Count == 0)
				return Fail("No installable packages are known for this system. Please follow the manual instructions.");

			return LinuxPackageManager.Platform switch
			{
				HostPlatform.Linux => RunLinuxInstall(result, packages),
				HostPlatform.Windows => RunWingetInstall(result, packages),
				_ => Fail("Automatic install is not supported on this platform. Please follow the manual instructions.")
			};
		}

		private static InstallOutcome RunLinuxInstall(DependencyCheckResult result, IReadOnlyList<string> packages)
		{
			var pmCommand = LinuxPackageManager.BuildInstallCommand(result.PackageManager, packages);
			if (string.IsNullOrEmpty(pmCommand))
				return Fail("Could not build an install command for this package manager. Please install manually.");

			if (!LinuxPackageManager.ExecutableExists("pkexec"))
				return Fail("'pkexec' is not available, so a graphical privilege prompt cannot be shown. Please install the dependencies manually in a terminal.");

			// pkexec takes a program + argv (not a shell string), so pass the package-manager argv directly.
			var parts = pmCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			var args = new List<string>(parts);

			var outcome = RunProcess("pkexec", args, packages, out int exit, out string err, out string outp);
			if (outcome != null)
				return outcome;

			// pkexec exit code 126 = user dismissed the auth dialog; 127 = auth could not be obtained.
			string reason = exit switch
			{
				126 => "The authorization dialog was dismissed.",
				127 => "Authorization could not be obtained.",
				_ => string.IsNullOrWhiteSpace(err) ? outp : err
			};
			return Failure(exit, reason);
		}

		private static InstallOutcome RunWingetInstall(DependencyCheckResult result, IReadOnlyList<string> packages)
		{
			if (result.PackageManager != PackageManagerKind.Winget || !LinuxPackageManager.ExecutableExists("winget"))
				return Fail("winget is not available. Please install the build tools from the download link in the instructions.");

			// winget install <id> [--id <id2> ...]. winget handles its own UAC elevation prompt.
			var args = new List<string> { "install", "--accept-package-agreements", "--accept-source-agreements" };
			foreach (var pkg in packages)
				args.Add(pkg);

			var outcome = RunProcess("winget", args, packages, out int exit, out string err, out string outp);
			if (outcome != null)
				return outcome;

			return Failure(exit, string.IsNullOrWhiteSpace(err) ? outp : err);
		}

		/// <summary>
		/// Runs an install process and returns a SUCCESS outcome (or null to signal "process ran but failed",
		/// with exit/err/out populated for the caller to format a platform-specific failure message).
		/// </summary>
		private static InstallOutcome RunProcess(
			string fileName, IReadOnlyList<string> args, IReadOnlyList<string> packages,
			out int exitCode, out string stderr, out string stdout)
		{
			exitCode = -1; stderr = string.Empty; stdout = string.Empty;
			try
			{
				var psi = new ProcessStartInfo
				{
					FileName = fileName,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
					CreateNoWindow = true
				};
				foreach (var a in args)
					psi.ArgumentList.Add(a);

				using var process = Process.Start(psi);
				if (process == null)
					return Fail($"Failed to start the install process ({fileName}).");

				stdout = process.StandardOutput.ReadToEnd();
				stderr = process.StandardError.ReadToEnd();
				process.WaitForExit();
				exitCode = process.ExitCode;

				if (exitCode == 0)
				{
					Debug.Success($"[DependencyCheck] Installed: {string.Join(", ", packages)}");
					return new InstallOutcome
					{
						Started = true,
						Succeeded = true,
						ExitCode = 0,
						Message = $"Installed: {string.Join(", ", packages)}"
					};
				}

				Debug.Warn($"[DependencyCheck] Install failed (exit {exitCode}).");
				return null; // signal: ran but non-zero; caller formats the reason
			}
			catch (Exception ex)
			{
				Debug.Error($"[DependencyCheck] Auto-install threw: {ex.Message}");
				return Fail($"Automatic install could not run: {ex.Message}. Please install manually.");
			}
		}

		private static InstallOutcome Failure(int exit, string reason) => new InstallOutcome
		{
			Started = true,
			Succeeded = false,
			ExitCode = exit,
			Message = $"Install failed (exit code {exit}). {reason}".Trim()
		};

		private static InstallOutcome Fail(string msg) => new InstallOutcome
		{
			Started = false,
			Succeeded = false,
			ExitCode = -1,
			Message = msg
		};
	}
}
