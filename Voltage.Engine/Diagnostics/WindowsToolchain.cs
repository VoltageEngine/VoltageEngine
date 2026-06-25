using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Voltage.Diagnostics
{
	/// <summary>
	/// Detects the Windows NativeAOT toolchain (the MSVC C++ build tools and the Windows SDK) by their real
	/// install locations rather than by probing PATH.
	/// </summary>
	/// <remarks>
	/// <para><b>Why this exists:</b> <c>cl.exe</c>/<c>link.exe</c> and the Windows SDK's <c>rc.exe</c>/<c>mt.exe</c>
	/// are NOT added to the machine PATH by the installer — they are only put on PATH inside a Visual Studio
	/// "Developer Command Prompt" / "x64 Native Tools" prompt. The editor runs as an ordinary GUI process, so a
	/// plain PATH probe reports them missing even when they are installed, and a "Recheck" repeats the same
	/// failing probe forever. The NativeAOT publish, by contrast, locates the toolset itself (via vswhere), so
	/// the build would actually succeed — the PATH gate is a false negative.</para>
	/// <para>This helper mirrors how MSBuild / NativeAOT find the toolset: <c>vswhere.exe</c> for the Visual
	/// Studio install (then a direct check that <c>cl.exe</c> exists under it), and the <c>Windows Kits</c>
	/// folder for the SDK. It falls back to a PATH probe last so a Developer Command Prompt still works. All
	/// probing is best-effort and side-effect free.</para>
	/// </remarks>
	public static class WindowsToolchain
	{
		/// <summary>True when an MSVC C++ toolset (cl.exe / link.exe) is installed.</summary>
		public static bool HasMsvcBuildTools()
		{
			// 1) Authoritative: ask vswhere for an install that provides the x64/x86 VC tools, then confirm
			//    cl.exe actually exists under it (the component may be listed but files pruned/corrupt).
			foreach (var installPath in QueryVsInstallsWithVcTools())
			{
				if (HasClUnder(installPath))
					return true;
			}

			// 2) Fallback for setups where vswhere is unavailable: scan the conventional VS install roots.
			foreach (var root in EnumerateVsInstallRoots())
			{
				if (HasClUnder(root))
					return true;
			}

			// 3) Last resort: a Developer Command Prompt puts cl.exe directly on PATH.
			return HostPackageManager.ExecutableExists("cl");
		}

		/// <summary>True when a Windows SDK (providing rc.exe) is installed.</summary>
		public static bool HasWindowsSdk()
		{
			foreach (var binRoot in EnumerateWindowsKitsBinRoots())
			{
				if (FindToolUnder(binRoot, "rc.exe"))
					return true;
			}

			// A Developer Command Prompt puts the SDK tools on PATH.
			return HostPackageManager.ExecutableExists("rc");
		}

		/// <summary>
		/// Runs vswhere (if present) and returns the installation paths of every VS instance that provides the
		/// x86/x64 VC tools component. Empty when vswhere is missing or fails.
		/// </summary>
		private static List<string> QueryVsInstallsWithVcTools()
		{
			var results = new List<string>();

			var vswhere = VsWherePath();
			if (vswhere == null)
				return results;

			try
			{
				var psi = new ProcessStartInfo
				{
					FileName = vswhere,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
					CreateNoWindow = true
				};
				foreach (var a in new[]
				{
					"-products", "*",
					"-prerelease",
					"-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
					"-property", "installationPath",
					"-utf8"
				})
					psi.ArgumentList.Add(a);

				using var p = Process.Start(psi);
				if (p == null)
					return results;

				var output = p.StandardOutput.ReadToEnd();
				p.WaitForExit(5000);

				foreach (var raw in output.Split('\n'))
				{
					var line = raw.Trim();
					if (line.Length > 0)
						results.Add(line);
				}
			}
			catch
			{
				// vswhere missing/blocked — caller falls back to a filesystem scan, then PATH.
			}

			return results;
		}

		/// <summary>
		/// vswhere ships at a fixed, documented path regardless of where VS itself is installed
		/// (<c>%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe</c>). Null if not found.
		/// </summary>
		private static string VsWherePath()
		{
			foreach (var pf in new[]
			{
				Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
				Environment.GetEnvironmentVariable("ProgramFiles")
			})
			{
				if (string.IsNullOrEmpty(pf))
					continue;
				var candidate = Path.Combine(pf, "Microsoft Visual Studio", "Installer", "vswhere.exe");
				if (File.Exists(candidate))
					return candidate;
			}
			return null;
		}

		/// <summary>True when <paramref name="installPath"/> contains a versioned MSVC toolset with cl.exe.</summary>
		private static bool HasClUnder(string installPath)
		{
			if (string.IsNullOrEmpty(installPath))
				return false;

			// Layout: <install>\VC\Tools\MSVC\<version>\bin\Host{X64|X86}\{x64|x86}\cl.exe
			var msvcRoot = Path.Combine(installPath, "VC", "Tools", "MSVC");
			if (!Directory.Exists(msvcRoot))
				return false;

			foreach (var versionDir in SafeGetDirectories(msvcRoot))
			{
				var binRoot = Path.Combine(versionDir, "bin");
				if (!Directory.Exists(binRoot))
					continue;

				foreach (var hostDir in SafeGetDirectories(binRoot))      // HostX64, HostX86
					foreach (var archDir in SafeGetDirectories(hostDir))  // x64, x86, arm64
						if (File.Exists(Path.Combine(archDir, "cl.exe")))
							return true;
			}

			return false;
		}

		/// <summary>The conventional VS install roots (Program Files [x86] × edition), for vswhere-less setups.</summary>
		private static IEnumerable<string> EnumerateVsInstallRoots()
		{
			var editions = new[] { "Enterprise", "Professional", "Community", "BuildTools", "Preview" };

			foreach (var pf in new[]
			{
				Environment.GetEnvironmentVariable("ProgramFiles"),
				Environment.GetEnvironmentVariable("ProgramFiles(x86)")
			})
			{
				if (string.IsNullOrEmpty(pf))
					continue;

				var vsRoot = Path.Combine(pf, "Microsoft Visual Studio");
				if (!Directory.Exists(vsRoot))
					continue;

				foreach (var yearDir in SafeGetDirectories(vsRoot))   // 2019, 2022, ...
					foreach (var edition in editions)
					{
						var path = Path.Combine(yearDir, edition);
						if (Directory.Exists(path))
							yield return path;
					}
			}
		}

		/// <summary>The <c>Windows Kits\&lt;ver&gt;\bin</c> roots that exist on this machine.</summary>
		private static IEnumerable<string> EnumerateWindowsKitsBinRoots()
		{
			foreach (var pf in new[]
			{
				Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
				Environment.GetEnvironmentVariable("ProgramFiles")
			})
			{
				if (string.IsNullOrEmpty(pf))
					continue;

				foreach (var kit in new[] { "10", "8.1" })
				{
					var bin = Path.Combine(pf, "Windows Kits", kit, "bin");
					if (Directory.Exists(bin))
						yield return bin;
				}
			}
		}

		/// <summary>
		/// Looks for <paramref name="exeName"/> under a Windows Kits bin root, handling both the newer
		/// <c>bin\&lt;version&gt;\&lt;arch&gt;\</c> layout and the older <c>bin\&lt;arch&gt;\</c> layout.
		/// </summary>
		private static bool FindToolUnder(string binRoot, string exeName)
		{
			var arches = new[] { "x64", "x86", "arm64" };

			// Newer SDKs: bin\<version>\<arch>\<exe>
			foreach (var versionDir in SafeGetDirectories(binRoot))
				foreach (var arch in arches)
					if (File.Exists(Path.Combine(versionDir, arch, exeName)))
						return true;

			// Older SDKs: bin\<arch>\<exe>
			foreach (var arch in arches)
				if (File.Exists(Path.Combine(binRoot, arch, exeName)))
					return true;

			return false;
		}

		private static IEnumerable<string> SafeGetDirectories(string path)
		{
			try
			{
				return Directory.GetDirectories(path);
			}
			catch
			{
				return Array.Empty<string>();
			}
		}
	}
}
