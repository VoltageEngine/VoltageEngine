using System;
using System.Collections.Generic;

namespace Voltage.Diagnostics
{
	/// <summary>
	/// How a native dependency's presence is detected on the host system.
	/// </summary>
	public enum DependencyKind
	{
		/// <summary>An executable that must be discoverable on PATH (e.g. clang, ld, cl.exe).</summary>
		Executable,

		/// <summary>
		/// A shared library that must be present at RUNTIME — discoverable via the dynamic linker cache
		/// (e.g. libGL, SDL2). The versioned soname (libfoo.so.N) is sufficient.
		/// </summary>
		SharedLibrary,

		/// <summary>
		/// A library that must be LINKABLE at build time. Detecting the runtime soname (libz.so.1) is NOT
		/// enough: the linker resolves <c>-lfoo</c> to the unversioned dev symlink <c>libfoo.so</c> (or a
		/// static <c>libfoo.a</c>), shipped by the distro's <c>-dev</c>/<c>-devel</c> package. This kind
		/// verifies that linkable artifact, which is what NativeAOT actually needs.
		/// </summary>
		LinkLibrary
	}

	/// <summary>
	/// When a dependency is required, and how badly the editor/build is affected if it is missing.
	/// </summary>
	public enum DependencySeverity
	{
		/// <summary>
		/// Without it the editor cannot start at all (graphics/window init crashes before any UI).
		/// Checked very early in startup, before graphics init.
		/// </summary>
		Critical,

		/// <summary>
		/// The editor still runs, but a feature is degraded (e.g. no audio). Surfaced as an in-editor warning.
		/// </summary>
		Optional,

		/// <summary>
		/// Required only to produce a NativeAOT game build. Checked before the publish step, not at startup.
		/// </summary>
		Build
	}

	/// <summary>
	/// A descriptor for a single native dependency the engine/editor relies on: how to detect it, what
	/// package provides it per Linux package manager, and (for platforms without a package manager) a
	/// download URL and copy-pasteable manual instruction.
	/// </summary>
	/// <remarks>
	/// Intentionally platform-agnostic and free of any UI/ImGui dependency so it can live in the engine and
	/// be reused by the startup runtime check, the build-time check, and all of Windows/macOS/Linux.
	/// </remarks>
	public sealed class NativeDependency
	{
		/// <summary>Human-friendly name shown to the user, e.g. "OpenGL (libGL)".</summary>
		public string FriendlyName { get; init; }

		/// <summary>Detection strategy: executable on PATH, runtime shared library, or linkable library.</summary>
		public DependencyKind Kind { get; init; }

		/// <summary>
		/// For <see cref="DependencyKind.Executable"/>: the executable name to find on PATH (e.g. "clang").
		/// For <see cref="DependencyKind.SharedLibrary"/>: a soname substring matched against the dynamic
		/// linker cache and/or dlopen'd (e.g. "libGL.so").
		/// For <see cref="DependencyKind.LinkLibrary"/>: the linker name as in <c>-lz</c> WITHOUT the
		/// <c>lib</c> prefix or extension (e.g. "z" for zlib, "SDL2" for SDL2). Detection looks for the
		/// unversioned <c>libz.so</c> / static <c>libz.a</c> dev artifact in the library search paths.
		/// </summary>
		public string Probe { get; init; }

		/// <summary>
		/// Optional additional probes. For shared/runtime libraries these are extra sonames to try (covers
		/// distro naming differences, e.g. "libGL.so.1"). For executables these are alternate exe names.
		/// NOT used for <see cref="DependencyKind.LinkLibrary"/> (which derives artifact names from Probe).
		/// </summary>
		public string[] AltProbes { get; init; } = Array.Empty<string>();

		/// <summary>Severity: critical/optional runtime dep, or a build dep.</summary>
		public DependencySeverity Severity { get; init; }

		/// <summary>
		/// Package name per package manager (Linux pacman/apt/dnf/zypper, and macOS Homebrew / Windows winget
		/// where applicable). A dependency lacking an entry for the detected manager falls back to the
		/// <see cref="ManualInstruction"/> / <see cref="DownloadUrl"/> guidance.
		/// </summary>
		public IReadOnlyDictionary<PackageManagerKind, string> PackageNames { get; init; }

		/// <summary>
		/// A copy-pasteable, self-contained command or step to install this dependency when no package-manager
		/// install is available (e.g. "xcode-select --install"). Optional.
		/// </summary>
		public string ManualInstruction { get; init; }

		/// <summary>
		/// A download URL the user can open when there is no command-line install (e.g. the Visual Studio
		/// Build Tools installer). Optional.
		/// </summary>
		public string DownloadUrl { get; init; }

		/// <summary>
		/// Optional custom presence detector. When set it REPLACES the <see cref="Kind"/>-based probe entirely.
		/// Used for dependencies that cannot be located on PATH or in the dynamic linker cache — e.g. the
		/// Windows MSVC toolset and Windows SDK, which live in versioned install folders and are only added to
		/// PATH inside a Visual Studio Developer Command Prompt (see <see cref="WindowsToolchain"/>). Returns
		/// true when the dependency is present. Must be side-effect free and not throw.
		/// </summary>
		public Func<bool> DetectionOverride { get; init; }

		/// <summary>
		/// Returns the package name for the given package manager, or null if none is curated.
		/// </summary>
		public string PackageFor(PackageManagerKind pm) =>
			PackageNames != null && PackageNames.TryGetValue(pm, out var name) ? name : null;
	}
}
