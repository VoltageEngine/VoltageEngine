using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Voltage.Diagnostics
{
	/// <summary>
	/// Supported package managers across all platforms (with a sentinel for "none / instructions only").
	/// </summary>
	public enum PackageManagerKind
	{
		None,
		// Linux
		Pacman,   // Arch, SteamOS, Manjaro
		Apt,      // Debian, Ubuntu, Mint, Pop!_OS
		Dnf,      // Fedora, RHEL, CentOS Stream
		Zypper,   // openSUSE
		// macOS
		Homebrew, // brew
		// Windows
		Winget    // winget (Windows Package Manager)
	}

	/// <summary>The host OS family the preflight runs on.</summary>
	public enum HostPlatform
	{
		Other,
		Linux,
		Windows,
		MacOS
	}

	/// <summary>
	/// Detects the host Linux distribution and its package manager, and builds the install command
	/// template used by the dependency preflight. All detection is best-effort and side-effect free.
	/// </summary>
	public static class LinuxPackageManager
	{
		private static PackageManagerKind? _cachedKind;
		private static string _cachedDistroId;
		private static string _cachedDistroPretty;

		/// <summary>True when running on Linux.</summary>
		public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

		/// <summary>True when running on macOS.</summary>
		public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

		/// <summary>True when running on Windows.</summary>
		public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

		/// <summary>The host OS family the preflight is running on.</summary>
		public static HostPlatform Platform =>
			IsLinux ? HostPlatform.Linux :
			IsWindows ? HostPlatform.Windows :
			IsMacOS ? HostPlatform.MacOS :
			HostPlatform.Other;

		/// <summary>The raw <c>ID</c> field from /etc/os-release (e.g. "arch", "steamos", "ubuntu"), or "".</summary>
		public static string DistroId
		{
			get { EnsureDetected(); return _cachedDistroId ?? string.Empty; }
		}

		/// <summary>The <c>PRETTY_NAME</c> from /etc/os-release (e.g. "SteamOS Holo"), or the ID, or "Linux".</summary>
		public static string DistroPrettyName
		{
			get { EnsureDetected(); return _cachedDistroPretty ?? "Linux"; }
		}

		/// <summary>
		/// Detects the package manager. Prefers /etc/os-release ID / ID_LIKE, then falls back to probing
		/// for the package-manager executable on PATH. Result is cached.
		/// </summary>
		public static PackageManagerKind Detect()
		{
			EnsureDetected();
			return _cachedKind.Value;
		}

		private static void EnsureDetected()
		{
			if (_cachedKind.HasValue)
				return;

			if (IsMacOS)
			{
				_cachedDistroId = "macos";
				_cachedDistroPretty = "macOS";
				// Homebrew is the de-facto package manager; offer it only if actually installed.
				_cachedKind = ExecutableExists("brew") ? PackageManagerKind.Homebrew : PackageManagerKind.None;
				return;
			}

			if (IsWindows)
			{
				_cachedDistroId = "windows";
				_cachedDistroPretty = "Windows";
				// winget ships with modern Windows 10/11; offer it only if present, else instructions only.
				_cachedKind = ExecutableExists("winget") ? PackageManagerKind.Winget : PackageManagerKind.None;
				return;
			}

			if (!IsLinux)
			{
				_cachedKind = PackageManagerKind.None;
				_cachedDistroPretty = "this platform";
				_cachedDistroId = string.Empty;
				return;
			}

			ReadOsRelease(out var id, out var idLike, out var pretty);
			_cachedDistroId = id;
			_cachedDistroPretty = string.IsNullOrEmpty(pretty) ? (string.IsNullOrEmpty(id) ? "Linux" : id) : pretty;

			// 1) Map by os-release ID / ID_LIKE first (most accurate, especially for derivatives like SteamOS).
			var fromId = MapDistroToPackageManager(id, idLike);
			if (fromId != PackageManagerKind.None)
			{
				_cachedKind = fromId;
				return;
			}

			// 2) Fall back to probing the package-manager binaries on PATH.
			if (ExecutableExists("pacman")) _cachedKind = PackageManagerKind.Pacman;
			else if (ExecutableExists("apt-get") || ExecutableExists("apt")) _cachedKind = PackageManagerKind.Apt;
			else if (ExecutableExists("dnf")) _cachedKind = PackageManagerKind.Dnf;
			else if (ExecutableExists("zypper")) _cachedKind = PackageManagerKind.Zypper;
			else _cachedKind = PackageManagerKind.None;
		}

		private static PackageManagerKind MapDistroToPackageManager(string id, string idLike)
		{
			var haystack = $"{id} {idLike}".ToLowerInvariant();

			// Order matters: check the most specific families. ID_LIKE often lists the upstream family.
			if (Contains(haystack, "arch") || Contains(haystack, "steamos") || Contains(haystack, "manjaro"))
				return PackageManagerKind.Pacman;
			if (Contains(haystack, "debian") || Contains(haystack, "ubuntu") || Contains(haystack, "mint") ||
			    Contains(haystack, "pop") || Contains(haystack, "raspbian"))
				return PackageManagerKind.Apt;
			if (Contains(haystack, "fedora") || Contains(haystack, "rhel") || Contains(haystack, "centos") ||
			    Contains(haystack, "rocky") || Contains(haystack, "almalinux"))
				return PackageManagerKind.Dnf;
			if (Contains(haystack, "suse") || Contains(haystack, "opensuse") || Contains(haystack, "sles"))
				return PackageManagerKind.Zypper;

			return PackageManagerKind.None;

			static bool Contains(string s, string token) => s.Contains(token, StringComparison.Ordinal);
		}

		private static void ReadOsRelease(out string id, out string idLike, out string pretty)
		{
			id = idLike = pretty = string.Empty;

			// /etc/os-release is the standard; /usr/lib/os-release is the vendor fallback.
			string path = File.Exists("/etc/os-release") ? "/etc/os-release"
				: File.Exists("/usr/lib/os-release") ? "/usr/lib/os-release"
				: null;

			if (path == null)
				return;

			try
			{
				foreach (var raw in File.ReadAllLines(path))
				{
					var line = raw.Trim();
					if (line.Length == 0 || line[0] == '#')
						continue;

					var eq = line.IndexOf('=');
					if (eq <= 0)
						continue;

					var key = line.Substring(0, eq).Trim();
					var value = line.Substring(eq + 1).Trim().Trim('"');

					switch (key)
					{
						case "ID": id = value; break;
						case "ID_LIKE": idLike = value; break;
						case "PRETTY_NAME": pretty = value; break;
					}
				}
			}
			catch
			{
				// Best-effort: a parse/IO failure just means we fall back to PATH probing.
			}
		}

		/// <summary>
		/// Builds the package-install command for the detected package manager (without pkexec).
		/// e.g. pacman → "pacman -S --needed --noconfirm zlib clang binutils".
		/// Returns null if the package manager is unknown or no packages are supplied.
		/// </summary>
		public static string BuildInstallCommand(PackageManagerKind pm, IReadOnlyList<string> packages)
		{
			if (pm == PackageManagerKind.None || packages == null || packages.Count == 0)
				return null;

			var pkgs = string.Join(' ', packages);
			return pm switch
			{
				PackageManagerKind.Pacman => $"pacman -S --needed --noconfirm {pkgs}",
				PackageManagerKind.Apt => $"apt-get install -y {pkgs}",
				PackageManagerKind.Dnf => $"dnf install -y {pkgs}",
				PackageManagerKind.Zypper => $"zypper install -y {pkgs}",
				PackageManagerKind.Homebrew => $"brew install {pkgs}",
				PackageManagerKind.Winget => $"winget install {pkgs}",
				_ => null
			};
		}

		/// <summary>
		/// The same command a user would type manually in a terminal (uses sudo, includes an apt update for apt).
		/// Returned as a display string for the "manual instructions" panel.
		/// </summary>
		public static string BuildManualInstallCommand(PackageManagerKind pm, IReadOnlyList<string> packages)
		{
			if (pm == PackageManagerKind.None || packages == null || packages.Count == 0)
				return null;

			var pkgs = string.Join(' ', packages);
			return pm switch
			{
				PackageManagerKind.Pacman => $"sudo pacman -S --needed {pkgs}",
				PackageManagerKind.Apt => $"sudo apt-get update && sudo apt-get install {pkgs}",
				PackageManagerKind.Dnf => $"sudo dnf install {pkgs}",
				PackageManagerKind.Zypper => $"sudo zypper install {pkgs}",
				// brew and winget run unprivileged (no sudo); manual command == the real command.
				PackageManagerKind.Homebrew => $"brew install {pkgs}",
				PackageManagerKind.Winget => $"winget install {pkgs}",
				_ => null
			};
		}

		/// <summary>
		/// Returns true if an executable with the given name is found on PATH.
		/// Cross-platform: honors PATHEXT on Windows, plain entries elsewhere.
		/// </summary>
		public static bool ExecutableExists(string exe)
		{
			if (string.IsNullOrEmpty(exe))
				return false;

			// Absolute/relative path supplied directly.
			if (exe.Contains(Path.DirectorySeparatorChar) || exe.Contains(Path.AltDirectorySeparatorChar))
				return File.Exists(exe);

			var pathVar = Environment.GetEnvironmentVariable("PATH");
			if (string.IsNullOrEmpty(pathVar))
				return false;

			var exts = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
				? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
				: new[] { string.Empty };

			foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
			{
				foreach (var ext in exts)
				{
					try
					{
						var candidate = Path.Combine(dir.Trim(), exe + ext);
						if (File.Exists(candidate))
							return true;
					}
					catch
					{
						// Ignore malformed PATH entries.
					}
				}
			}

			return false;
		}

		/// <summary>
		/// SteamOS (and similar) ship a read-only root filesystem. Auto-install via a package manager will
		/// fail there unless the user runs <c>sudo steamos-readonly disable</c> first, which we never do for them.
		/// </summary>
		public static bool IsImmutableRootFilesystem()
		{
			if (!IsLinux)
				return false;

			// SteamOS exposes a helper script; its presence is the most reliable signal.
			if (ExecutableExists("steamos-readonly"))
			{
				try
				{
					// "steamos-readonly status" prints "enabled" when the root is read-only.
					var psi = new ProcessStartInfo
					{
						FileName = "steamos-readonly",
						Arguments = "status",
						RedirectStandardOutput = true,
						RedirectStandardError = true,
						UseShellExecute = false,
						CreateNoWindow = true
					};
					using var p = Process.Start(psi);
					if (p != null)
					{
						var output = p.StandardOutput.ReadToEnd();
						p.WaitForExit(2000);
						if (output.Contains("enabled", StringComparison.OrdinalIgnoreCase))
							return true;
						if (output.Contains("disabled", StringComparison.OrdinalIgnoreCase))
							return false;
					}
				}
				catch
				{
					// If the helper exists but we can't query it, assume immutable to be safe.
					return true;
				}
			}

			return false;
		}
	}
}
