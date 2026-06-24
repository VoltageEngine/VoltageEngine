using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Voltage.Editor.DebugUtils;

namespace Voltage.Editor.Builders;

/// <summary>
/// Runs the NativeAOT <c>dotnet publish</c> for Linux targets inside an
/// old-glibc container so the produced executable has a low glibc floor and
/// runs on stock SteamOS / older distros — not just the (newer-glibc) machine
/// the editor happens to be building on.
///
/// <para><b>Why this exists:</b> NativeAOT links the final native executable
/// against whatever glibc the build machine provides. The editor typically runs
/// inside a recent distrobox (e.g. Fedora 44, glibc 2.43), so a directly
/// published binary demands <c>GLIBC_2.43</c> and refuses to start on the
/// SteamOS host (glibc 2.41) or any older distro with the loader error
/// "version `GLIBC_2.xx' not found". glibc is backward- but not
/// forward-compatible, so the fix is to build against the <i>oldest</i> glibc we
/// want to support. This compiles the AOT binary inside Ubuntu 20.04
/// (glibc 2.31); the result runs on glibc 2.31 and everything newer.</para>
///
/// <para>The container runtime is podman, reached on the host. When the editor
/// runs inside a distrobox the host's podman is invoked through
/// <c>distrobox-host-exec</c> / <c>host-spawn</c>; on a bare host podman (or
/// docker) is used directly. Because distrobox shares <c>/home</c>, host paths
/// and in-distrobox paths are identical, so volume mounts need no translation.</para>
/// </summary>
public static class LinuxContainerBuild
{
	/// <summary>Result of attempting a container-based publish.</summary>
	public enum Outcome
	{
		/// <summary>The AOT binary was published successfully inside the container.</summary>
		Succeeded,

		/// <summary>No usable container runtime / image could be prepared — caller should fall back.</summary>
		Unavailable,

		/// <summary>The container ran but the publish itself failed — this is a real build error.</summary>
		Failed
	}

	/// <summary>Tag of the locally-built AOT build image.</summary>
	public const string ImageTag = "voltage-aot-build:ubuntu2004-net8";

	/// <summary>
	/// Container recipe: Ubuntu 20.04 (glibc 2.31) + the NativeAOT native
	/// toolchain (clang, zlib, C toolchain) + the .NET 8 SDK. Built once and
	/// cached locally; rebuilt only if the image is missing.
	/// </summary>
	private const string Containerfile =
		"FROM ubuntu:20.04\n" +
		"ENV DEBIAN_FRONTEND=noninteractive\n" +
		// libicu66 is required by the .NET SDK/CLI (globalization) — the minimal Ubuntu
		// 20.04 image does not ship it, and dotnet aborts on startup without it.
		"RUN apt-get update \\\n" +
		"    && apt-get install -y --no-install-recommends \\\n" +
		"        ca-certificates curl clang zlib1g-dev build-essential libicu66 \\\n" +
		"    && rm -rf /var/lib/apt/lists/*\n" +
		"RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \\\n" +
		"    && chmod +x /tmp/dotnet-install.sh \\\n" +
		"    && /tmp/dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet \\\n" +
		"    && ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet \\\n" +
		"    && rm /tmp/dotnet-install.sh\n" +
		"ENV DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1\n";

	/// <summary>
	/// True when <paramref name="platform"/> is a Linux target that benefits from
	/// the glibc-compat container.
	/// </summary>
	public static bool IsLinux(BuildPlatform platform) =>
		platform != null && platform.RuntimeIdentifier.StartsWith("linux", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Publishes the game as a NativeAOT binary inside the old-glibc container.
	/// </summary>
	/// <param name="csprojPath">Absolute path to the game .csproj.</param>
	/// <param name="projectName">Project name (used for TrimmerRootAssembly).</param>
	/// <param name="projectPath">Game project root (mounted into the container).</param>
	/// <param name="runtimeIdentifier">Target RID, e.g. "linux-x64".</param>
	/// <param name="configuration">"Debug" or "Release".</param>
	/// <param name="buildDir">Output directory (lives under <paramref name="projectPath"/>).</param>
	/// <param name="cancellationToken">Cancels the image build / publish.</param>
	/// <param name="message">Human-readable detail about the outcome.</param>
	public static Outcome Publish(
		string csprojPath,
		string projectName,
		string projectPath,
		string runtimeIdentifier,
		string configuration,
		string buildDir,
		CancellationToken cancellationToken,
		out string message)
	{
		var invoker = ResolveInvoker();
		if (invoker == null)
		{
			message = "no podman/docker runtime found (and not reachable through distrobox-host-exec/host-spawn)";
			return Outcome.Unavailable;
		}

		// Verify the runtime actually responds before committing to it.
		if (RunContainerCommand(invoker, new[] { "--version" }, cancellationToken, out _, out var verErr) != 0)
		{
			message = $"container runtime not responding: {Trim(verErr)}";
			return Outcome.Unavailable;
		}

		if (cancellationToken.IsCancellationRequested)
		{
			message = "cancelled";
			return Outcome.Failed;
		}

		if (!EnsureImage(invoker, projectPath, cancellationToken, out var imgErr))
		{
			message = $"could not prepare build image: {imgErr}";
			return Outcome.Unavailable;
		}

		// Writable, persistent HOME/NuGet locations so the container (running as the
		// mapped host user under rootless podman) can write caches without polluting
		// the project root or the per-build output that gets wiped each run.
		var containerHome = Path.Combine(projectPath, "Build", ".aot-container-home");
		var nugetCache = GetNuGetCachePath();
		TryCreateDir(containerHome);
		TryCreateDir(nugetCache);

		var args = new List<string>
		{
			"run", "--rm",
			"-v", $"{projectPath}:{projectPath}",
			"-v", $"{nugetCache}:{nugetCache}",
			"-e", $"HOME={containerHome}",
			"-e", $"DOTNET_CLI_HOME={containerHome}",
			"-e", $"NUGET_PACKAGES={nugetCache}",
			"-e", "DOTNET_NOLOGO=1",
			"-e", "DOTNET_CLI_TELEMETRY_OPTOUT=1",
			"-w", projectPath,
			ImageTag,
			"dotnet", "publish", csprojPath,
			"-c", configuration,
			"-r", runtimeIdentifier,
			"-o", buildDir,
			"--self-contained", "true",
			"-p:PublishAot=true",
			"-p:PublishTrimmed=true",
			"-p:TrimMode=link",
			$"-p:TrimmerRootAssembly={projectName}",
			"-p:IncludeNativeLibrariesForSelfExtract=true"
		};

		EditorDebug.Log($"Publishing Linux AOT build inside {ImageTag} (glibc 2.31 floor)...", "LinuxContainerBuild");

		var exit = RunContainerCommand(invoker, args, cancellationToken, out var stdout, out var stderr);
		if (exit != 0)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				message = "cancelled";
				return Outcome.Failed;
			}

			if (!string.IsNullOrWhiteSpace(stderr))
				EditorDebug.Error($"Container publish stderr:\n{stderr}", "LinuxContainerBuild");
			if (!string.IsNullOrWhiteSpace(stdout))
				EditorDebug.Error($"Container publish stdout:\n{stdout}", "LinuxContainerBuild");

			message = $"dotnet publish failed inside container (exit {exit})";
			return Outcome.Failed;
		}

		message = $"published against glibc 2.31 inside {ImageTag}";
		return Outcome.Succeeded;
	}

	/// <summary>
	/// Builds the AOT build image if it is not already present locally.
	/// </summary>
	private static bool EnsureImage(List<string> invoker, string projectPath, CancellationToken cancellationToken, out string error)
	{
		error = null;

		// Fast path: image already built.
		if (RunContainerCommand(invoker, new[] { "image", "exists", ImageTag }, cancellationToken, out _, out _) == 0)
			return true;

		if (cancellationToken.IsCancellationRequested)
		{
			error = "cancelled";
			return false;
		}

		// Write the Containerfile to a host-visible context dir and build from it.
		var contextDir = Path.Combine(projectPath, "Build", ".aot-container-build");
		try
		{
			Directory.CreateDirectory(contextDir);
			File.WriteAllText(Path.Combine(contextDir, "Containerfile"), Containerfile);
		}
		catch (Exception ex)
		{
			error = $"could not stage Containerfile: {ex.Message}";
			return false;
		}

		EditorDebug.Log(
			"Building the Linux glibc-compat image (Ubuntu 20.04 + .NET 8 SDK + AOT toolchain). " +
			"This is a one-time step and may take several minutes...", "LinuxContainerBuild");

		var buildArgs = new[]
		{
			"build", "-t", ImageTag,
			"-f", Path.Combine(contextDir, "Containerfile"),
			contextDir
		};

		var exit = RunContainerCommand(invoker, buildArgs, cancellationToken, out var stdout, out var stderr);
		if (exit != 0)
		{
			if (!string.IsNullOrWhiteSpace(stderr))
				EditorDebug.Warn($"Image build stderr:\n{Trim(stderr, 4000)}", "LinuxContainerBuild");
			if (!string.IsNullOrWhiteSpace(stdout))
				EditorDebug.Warn($"Image build stdout:\n{Trim(stdout, 4000)}", "LinuxContainerBuild");
			error = $"image build failed (exit {exit})";
			return false;
		}

		EditorDebug.Log($"Built {ImageTag}.", "LinuxContainerBuild");
		return true;
	}

	/// <summary>
	/// Resolves how to invoke the container runtime, accounting for the editor
	/// possibly running inside a distrobox where podman lives on the host.
	/// Returns the command + leading fixed args (e.g. ["distrobox-host-exec", "podman"]),
	/// or null if nothing usable is found.
	/// </summary>
	private static List<string> ResolveInvoker()
	{
		if (IsOnPath("podman")) return new List<string> { "podman" };
		if (IsOnPath("docker")) return new List<string> { "docker" };
		if (IsOnPath("distrobox-host-exec")) return new List<string> { "distrobox-host-exec", "podman" };
		if (IsOnPath("host-spawn")) return new List<string> { "host-spawn", "podman" };
		return null;
	}

	private static bool IsOnPath(string executable)
	{
		var pathVar = Environment.GetEnvironmentVariable("PATH");
		if (string.IsNullOrEmpty(pathVar))
			return false;

		foreach (var dir in pathVar.Split(Path.PathSeparator))
		{
			if (string.IsNullOrEmpty(dir))
				continue;
			try
			{
				if (File.Exists(Path.Combine(dir, executable)))
					return true;
			}
			catch
			{
				// Ignore malformed PATH entries.
			}
		}

		return false;
	}

	/// <summary>
	/// Runs <c>invoker + args</c> as a child process, capturing stdout/stderr and
	/// honouring cancellation. Returns the process exit code, or -1 if it could
	/// not be started / was cancelled.
	/// </summary>
	private static int RunContainerCommand(List<string> invoker, IEnumerable<string> args, CancellationToken cancellationToken, out string stdout, out string stderr)
	{
		stdout = string.Empty;
		stderr = string.Empty;

		try
		{
			var psi = new ProcessStartInfo
			{
				FileName = invoker[0],
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};

			for (int i = 1; i < invoker.Count; i++)
				psi.ArgumentList.Add(invoker[i]);
			foreach (var a in args)
				psi.ArgumentList.Add(a);

			using var process = Process.Start(psi);
			if (process == null)
			{
				stderr = $"failed to start {invoker[0]}";
				return -1;
			}

			var outTask = process.StandardOutput.ReadToEndAsync();
			var errTask = process.StandardError.ReadToEndAsync();

			while (!process.WaitForExit(200))
			{
				if (cancellationToken.IsCancellationRequested)
				{
					try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
					stderr = "cancelled";
					return -1;
				}
			}

			// Ensure async output is fully drained after exit.
			process.WaitForExit();
			stdout = outTask.Result;
			stderr = errTask.Result;
			return process.ExitCode;
		}
		catch (Exception ex)
		{
			stderr = ex.Message;
			return -1;
		}
	}

	/// <summary>Returns the NuGet global packages cache path, shared with the host build.</summary>
	private static string GetNuGetCachePath()
	{
		var env = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
		if (!string.IsNullOrEmpty(env))
			return env;

		var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		return Path.Combine(userProfile, ".nuget", "packages");
	}

	private static void TryCreateDir(string path)
	{
		try
		{
			if (!string.IsNullOrEmpty(path))
				Directory.CreateDirectory(path);
		}
		catch
		{
			// Non-fatal: the container mount will surface a clearer error if this matters.
		}
	}

	private static string Trim(string s, int max = 1000)
	{
		if (string.IsNullOrEmpty(s))
			return s;
		s = s.Trim();
		return s.Length <= max ? s : s.Substring(0, max) + "…";
	}
}
