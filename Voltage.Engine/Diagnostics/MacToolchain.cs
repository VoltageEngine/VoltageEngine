using System;
using System.Diagnostics;
using System.IO;

namespace Voltage.Diagnostics
{
	/// <summary>
	/// Detects the macOS NativeAOT toolchain (the Xcode Command Line Tools, which provide <c>clang</c> and
	/// <c>ld</c>) by asking the system where the active developer directory is, rather than by a naive PATH /
	/// file-existence probe.
	/// </summary>
	/// <remarks>
	/// <para><b>Why this exists:</b> <c>/usr/bin/clang</c> is a SHIM that exists on a stock macOS even when the
	/// Command Line Tools are NOT installed — invoking it merely triggers Apple's "install developer tools"
	/// prompt. So a bare <c>File.Exists("/usr/bin/clang")</c> (or a PATH probe, since <c>/usr/bin</c> is always
	/// on PATH) is a FALSE POSITIVE: it reports the toolchain present when it is not. This helper instead asks
	/// <c>xcode-select -p</c> / <c>xcrun --find clang</c> — which only succeed once the tools are actually
	/// installed — and confirms a real <c>clang</c> binary under the active developer directory.</para>
	/// <para>Detection is identical on Intel and Apple Silicon: the Command Line Tools / Xcode install to the
	/// same paths on both, and <c>clang</c> is a universal binary. The CPU architecture only affects the AOT
	/// publish <i>target</i> (<c>osx-x64</c> vs <c>osx-arm64</c>), not whether the toolchain is present. All
	/// probing is best-effort and side-effect free.</para>
	/// </remarks>
	public static class MacToolchain
	{
		/// <summary>True when the Xcode Command Line Tools (clang + ld) are installed and usable.</summary>
		public static bool HasCommandLineTools()
		{
			// 1) Authoritative: xcode-select -p prints the active developer dir and exits 0 only when the
			//    Command Line Tools / Xcode are configured. Confirm a real clang exists under it.
			if (TryRun(out var devDir, "xcode-select", "-p") && !string.IsNullOrEmpty(devDir))
			{
				if (Directory.Exists(devDir) && ClangUnder(devDir))
					return true;
			}

			// 2) xcrun resolves the active clang; exit 0 + an existing path means the toolchain is usable.
			if (TryRun(out var clangPath, "xcrun", "--find", "clang") &&
			    !string.IsNullOrEmpty(clangPath) && File.Exists(clangPath))
				return true;

			// 3) Well-known install roots (identical on Intel and Apple Silicon — clang is a universal binary).
			foreach (var dev in new[]
			{
				"/Library/Developer/CommandLineTools",
				"/Applications/Xcode.app/Contents/Developer"
			})
			{
				if (ClangUnder(dev))
					return true;
			}

			// NOTE: deliberately NOT a bare File.Exists("/usr/bin/clang") — that shim exists even when the
			// Command Line Tools are absent, so it would falsely report the toolchain present.
			return false;
		}

		/// <summary>True when an actual clang binary lives under the given developer directory.</summary>
		private static bool ClangUnder(string developerDir)
		{
			if (string.IsNullOrEmpty(developerDir))
				return false;

			// CLT layout:   <dev>/usr/bin/clang
			// Xcode layout: <dev>/Toolchains/XcodeDefault.xctoolchain/usr/bin/clang
			foreach (var candidate in new[]
			{
				Path.Combine(developerDir, "usr", "bin", "clang"),
				Path.Combine(developerDir, "Toolchains", "XcodeDefault.xctoolchain", "usr", "bin", "clang")
			})
			{
				try
				{
					if (File.Exists(candidate))
						return true;
				}
				catch
				{
					// Ignore unreadable paths.
				}
			}

			return false;
		}

		/// <summary>
		/// Runs <paramref name="fileName"/> with <paramref name="args"/>, returning true only on exit code 0,
		/// and sets <paramref name="firstLine"/> to the first non-empty trimmed stdout line. Never throws.
		/// </summary>
		private static bool TryRun(out string firstLine, string fileName, params string[] args)
		{
			firstLine = null;
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

				using var p = Process.Start(psi);
				if (p == null)
					return false;

				var output = p.StandardOutput.ReadToEnd();
				if (!p.WaitForExit(4000))
				{
					try { p.Kill(); } catch { /* best effort */ }
					return false;
				}

				if (p.ExitCode != 0)
					return false;

				foreach (var raw in output.Split('\n'))
				{
					var line = raw.Trim();
					if (line.Length > 0)
					{
						firstLine = line;
						break;
					}
				}
				return true;
			}
			catch
			{
				return false;
			}
		}
	}
}
