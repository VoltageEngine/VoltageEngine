using System;
using System.Collections.Generic;

namespace Voltage.Diagnostics
{
	/// <summary>
	/// The curated list of native dependencies the engine/editor needs, split into the runtime set
	/// (checked at editor startup) and the build set (checked before a NativeAOT game publish). The build
	/// set is platform-aware: the NativeAOT toolchain differs per OS (clang/ld/zlib on Linux, MSVC + Windows
	/// SDK on Windows, Xcode Command Line Tools on macOS).
	/// </summary>
	public static class NativeDependencyCatalog
	{
		/// <summary>
		/// Runtime libraries the editor links against. These are Linux concerns — on Windows/macOS the editor
		/// ships (or the OS provides) the equivalent graphics/audio stack, so this set is empty there and the
		/// startup check trivially passes. libGL and SDL2 are CRITICAL; OpenAL is OPTIONAL (audio only).
		/// </summary>
		public static IReadOnlyList<NativeDependency> RuntimeDependencies =>
			LinuxPackageManager.IsLinux ? LinuxRuntimeDependencies : Array.Empty<NativeDependency>();

		/// <summary>
		/// Toolchain dependencies required by the NativeAOT publish, selected for the current host platform.
		/// </summary>
		public static IReadOnlyList<NativeDependency> BuildDependencies => LinuxPackageManager.Platform switch
		{
			HostPlatform.Linux => LinuxBuildDependencies,
			HostPlatform.Windows => WindowsBuildDependencies,
			HostPlatform.MacOS => MacBuildDependencies,
			_ => Array.Empty<NativeDependency>()
		};

		// ----------------------------------------------------------------------------------------------
		// Linux
		// ----------------------------------------------------------------------------------------------

		private static readonly IReadOnlyList<NativeDependency> LinuxRuntimeDependencies = new List<NativeDependency>
		{
			new NativeDependency
			{
				FriendlyName = "OpenGL (libGL)",
				Kind = DependencyKind.SharedLibrary,
				Probe = "libGL.so",
				AltProbes = new[] { "libGL.so.1" },
				Severity = DependencySeverity.Critical,
				PackageNames = new Dictionary<PackageManagerKind, string>
				{
					{ PackageManagerKind.Pacman, "libglvnd" },
					{ PackageManagerKind.Apt, "libgl1" },
					{ PackageManagerKind.Dnf, "libglvnd-glx" },
					{ PackageManagerKind.Zypper, "Mesa-libGL1" }
				}
			},
			new NativeDependency
			{
				FriendlyName = "SDL2",
				Kind = DependencyKind.SharedLibrary,
				Probe = "libSDL2",
				AltProbes = new[] { "libSDL2-2.0.so.0", "libSDL2.so" },
				Severity = DependencySeverity.Critical,
				PackageNames = new Dictionary<PackageManagerKind, string>
				{
					{ PackageManagerKind.Pacman, "sdl2" },
					{ PackageManagerKind.Apt, "libsdl2-2.0-0" },
					{ PackageManagerKind.Dnf, "SDL2" },
					{ PackageManagerKind.Zypper, "libSDL2-2_0-0" }
				}
			},
			new NativeDependency
			{
				FriendlyName = "OpenAL (audio)",
				Kind = DependencyKind.SharedLibrary,
				Probe = "libopenal",
				AltProbes = new[] { "libopenal.so.1", "libopenal.so" },
				Severity = DependencySeverity.Optional,
				PackageNames = new Dictionary<PackageManagerKind, string>
				{
					{ PackageManagerKind.Pacman, "openal" },
					{ PackageManagerKind.Apt, "libopenal1" },
					{ PackageManagerKind.Dnf, "openal-soft" },
					{ PackageManagerKind.Zypper, "libopenal1" }
				}
			}
		};

		private static readonly IReadOnlyList<NativeDependency> LinuxBuildDependencies = new List<NativeDependency>
		{
			new NativeDependency
			{
				FriendlyName = "clang (C/C++ compiler driver)",
				Kind = DependencyKind.Executable,
				Probe = "clang",
				Severity = DependencySeverity.Build,
				PackageNames = new Dictionary<PackageManagerKind, string>
				{
					{ PackageManagerKind.Pacman, "clang" },
					{ PackageManagerKind.Apt, "clang" },
					{ PackageManagerKind.Dnf, "clang" },
					{ PackageManagerKind.Zypper, "clang" }
				}
			},
			new NativeDependency
			{
				FriendlyName = "ld / binutils (system linker)",
				Kind = DependencyKind.Executable,
				Probe = "ld.bfd",
				AltProbes = new[] { "ld" },
				Severity = DependencySeverity.Build,
				PackageNames = new Dictionary<PackageManagerKind, string>
				{
					{ PackageManagerKind.Pacman, "binutils" },
					{ PackageManagerKind.Apt, "binutils" },
					{ PackageManagerKind.Dnf, "binutils" },
					{ PackageManagerKind.Zypper, "binutils" }
				}
			},
			new NativeDependency
			{
				// LinkLibrary (NOT SharedLibrary): the AOT linker needs the unversioned libz.so dev symlink,
				// which only the -dev/-devel package ships. The runtime libz.so.1 is not enough — this is the
				// bug where publish failed on "cannot find -lz" yet our old probe reported zlib present.
				FriendlyName = "zlib dev (libz.so, required by AOT linker -lz)",
				Kind = DependencyKind.LinkLibrary,
				Probe = "z",
				Severity = DependencySeverity.Build,
				PackageNames = new Dictionary<PackageManagerKind, string>
				{
					{ PackageManagerKind.Pacman, "zlib" },
					{ PackageManagerKind.Apt, "zlib1g-dev" },
					{ PackageManagerKind.Dnf, "zlib-devel" },
					{ PackageManagerKind.Zypper, "zlib-devel" }
				}
			}
		};

		// ----------------------------------------------------------------------------------------------
		// Windows — NativeAOT needs the MSVC toolset (cl.exe / link.exe) and the Windows SDK, both provided
		// by the "Build Tools for Visual Studio" Desktop C++ workload. No silent auto-install; offer the
		// winget package as a convenience only when winget is present, otherwise a download link.
		// ----------------------------------------------------------------------------------------------

		private const string VsBuildToolsUrl = "https://visualstudio.microsoft.com/downloads/";
		private const string VsBuildToolsWinget = "Microsoft.VisualStudio.2022.BuildTools";
		private const string WinSdkUrl = "https://developer.microsoft.com/windows/downloads/windows-sdk/";

		private static readonly IReadOnlyList<NativeDependency> WindowsBuildDependencies = new List<NativeDependency>
		{
			new NativeDependency
			{
				FriendlyName = "MSVC build tools (cl.exe / link.exe)",
				Kind = DependencyKind.Executable,
				Probe = "cl",          // cl.exe — only on PATH inside a Developer/VS command prompt
				AltProbes = new[] { "link" },
				Severity = DependencySeverity.Build,
				PackageNames = new Dictionary<PackageManagerKind, string>
				{
					{ PackageManagerKind.Winget, VsBuildToolsWinget }
				},
				ManualInstruction =
					"Install \"Build Tools for Visual Studio\" and select the \"Desktop development with C++\" workload.",
				DownloadUrl = VsBuildToolsUrl
			},
			new NativeDependency
			{
				FriendlyName = "Windows SDK",
				Kind = DependencyKind.Executable,
				Probe = "rc",          // rc.exe (Resource Compiler) ships with the Windows SDK
				AltProbes = new[] { "mt" },
				Severity = DependencySeverity.Build,
				PackageNames = new Dictionary<PackageManagerKind, string>
				{
					// The C++ workload includes the Windows SDK, so the same Build Tools package covers it.
					{ PackageManagerKind.Winget, VsBuildToolsWinget }
				},
				ManualInstruction =
					"The Windows SDK is included with the Visual Studio \"Desktop development with C++\" workload, " +
					"or can be installed standalone.",
				DownloadUrl = WinSdkUrl
			}
		};

		// ----------------------------------------------------------------------------------------------
		// macOS — NativeAOT uses the Xcode Command Line Tools (provides clang + ld). One install covers both.
		// ----------------------------------------------------------------------------------------------

		private static readonly IReadOnlyList<NativeDependency> MacBuildDependencies = new List<NativeDependency>
		{
			new NativeDependency
			{
				FriendlyName = "Xcode Command Line Tools (clang + ld)",
				Kind = DependencyKind.Executable,
				Probe = "clang",
				AltProbes = new[] { "ld" },
				Severity = DependencySeverity.Build,
				// Homebrew is not the right tool for the CLT; the canonical install is xcode-select.
				PackageNames = new Dictionary<PackageManagerKind, string>(),
				ManualInstruction = "xcode-select --install",
				DownloadUrl = "https://developer.apple.com/xcode/resources/"
			}
		};
	}
}
