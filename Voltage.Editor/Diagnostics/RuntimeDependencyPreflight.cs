using System;
using System.Linq;
using System.Text;
using Voltage.Diagnostics;

namespace Voltage.Editor.Diagnostics
{
	/// <summary>
	/// Startup-time runtime dependency preflight. Runs BEFORE MonoGame/SDL graphics init so a missing
	/// critical library (libGL, SDL2) produces a clear, actionable message on stdout/stderr and the
	/// engine log instead of a cryptic native crash deep inside SDL.
	/// </summary>
	/// <remarks>
	/// Non-critical (optional) runtime deps like OpenAL are NOT fatal here — they are deferred to an
	/// in-editor warning once the UI is up (see <see cref="StartupRuntimeWarnings"/>). On Windows/macOS
	/// the check is a trivial pass.
	/// </remarks>
	public static class RuntimeDependencyPreflight
	{
		/// <summary>
		/// The most recent runtime check result, captured so the editor can surface optional-dep warnings
		/// in the UI after graphics init succeeds. Null until <see cref="CheckCriticalOrExit"/> runs.
		/// </summary>
		public static DependencyCheckResult LastResult { get; private set; }

		/// <summary>
		/// Checks the runtime dependency set. If a CRITICAL dependency is missing, prints actionable install
		/// instructions and returns false (the caller should exit cleanly). Returns true when it is safe to
		/// proceed to graphics init (optional deps may still be missing — those are surfaced later in the UI).
		/// </summary>
		public static bool CheckCriticalOrExit()
		{
			// Cheap and safe on every platform; on non-Linux this reports everything present.
			var result = NativeDependencyChecker.Check(NativeDependencyCatalog.RuntimeDependencies);
			LastResult = result;

			if (!result.AnyCriticalMissing)
				return true;

			var message = BuildCriticalMessage(result);

			// stderr so it is visible even when stdout is redirected, and the engine log for the crash file.
			System.Console.Error.WriteLine(message);
			try { Debug.Error(message); } catch { /* logger may not be ready this early */ }

			return false;
		}

		private static string BuildCriticalMessage(DependencyCheckResult result)
		{
			var missingCritical = result.Missing
				.Where(s => s.Dependency.Severity == DependencySeverity.Critical)
				.Select(s => s.Dependency.FriendlyName)
				.ToList();

			var sb = new StringBuilder();
			sb.AppendLine();
			sb.AppendLine("======================================================================");
			sb.AppendLine(" Voltage Editor cannot start: required native libraries are missing.");
			sb.AppendLine("======================================================================");
			sb.AppendLine($" System:  {result.DistroName}");
			sb.AppendLine($" Missing: {string.Join(", ", missingCritical)}");
			sb.AppendLine();
			sb.AppendLine(" These libraries are needed for the window/graphics layer (SDL2/OpenGL).");
			sb.AppendLine(" Without them the editor would crash during graphics initialization.");
			sb.AppendLine();

			var packages = result.MissingPackages();
			var manual = LinuxPackageManager.BuildManualInstallCommand(result.PackageManager, packages);

			if (result.ImmutableRoot)
			{
				sb.AppendLine(" This system has a read-only root filesystem (SteamOS).");
				sb.AppendLine(" Unlock it, install, then re-lock:");
				sb.AppendLine();
				sb.AppendLine("   sudo steamos-readonly disable");
				if (!string.IsNullOrEmpty(manual))
					sb.AppendLine($"   {manual}");
				sb.AppendLine("   sudo steamos-readonly enable");
			}
			else if (!string.IsNullOrEmpty(manual))
			{
				sb.AppendLine(" To install them, run:");
				sb.AppendLine();
				sb.AppendLine($"   {manual}");
			}
			else
			{
				sb.AppendLine(" Install the libraries listed above using your distribution's package manager.");
			}

			sb.AppendLine();
			sb.AppendLine("======================================================================");
			return sb.ToString();
		}

		private static bool _optionalWarningsSurfaced;

		/// <summary>
		/// Once the editor UI is up, surface a one-time warning for any OPTIONAL runtime dependency that is
		/// missing (e.g. OpenAL → no audio). These are non-fatal so they were allowed to pass startup.
		/// Safe to call every frame; it only acts once. No-op when nothing optional is missing.
		/// </summary>
		public static void SurfaceOptionalWarningsOnce(Action<string> warn, Action<string> notify = null)
		{
			if (_optionalWarningsSurfaced || LastResult == null)
				return;
			_optionalWarningsSurfaced = true;

			var optionalMissing = LastResult.Missing
				.Where(s => s.Dependency.Severity == DependencySeverity.Optional)
				.Select(s => s.Dependency.FriendlyName)
				.ToList();

			if (optionalMissing.Count == 0)
				return;

			var names = string.Join(", ", optionalMissing);
			var packages = LastResult.MissingPackages();
			var manual = LinuxPackageManager.BuildManualInstallCommand(LastResult.PackageManager, packages);

			var msg = $"Optional native libraries are missing: {names}. " +
			          "Some features (e.g. audio) may be unavailable.";
			if (!string.IsNullOrEmpty(manual))
				msg += $" To enable them, run: {manual}";

			warn?.Invoke(msg);
			notify?.Invoke($"Missing optional libraries: {names}");
		}
	}
}
