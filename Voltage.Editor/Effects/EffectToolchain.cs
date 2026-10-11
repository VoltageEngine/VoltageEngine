using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Voltage.Editor.Effects;

/// <summary>DesktopGL shader compilation, independent of the editor and its graphics device.</summary>
public static class EffectToolchain
{
	public const string Profile = "OpenGL";
	public const string SetupUrl = "https://docs.monogame.net/errors/mgfx0001/";
	private static readonly SemaphoreSlim InstallLock = new(1, 1);

	public static string ToolDirectory(string version) => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltage", "Tools", "mgfxc", version);

	public static string ToolPath(string directory, bool windows) => Path.Combine(directory, windows ? "mgfxc.exe" : "mgfxc");

	public static string FindDotnet()
	{
		var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
		if (!string.IsNullOrWhiteSpace(host) && File.Exists(host))
			return host;
		var name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
		var candidates = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
			.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Path.Combine(p, name)).Concat(new[]
			{
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", name),
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", name),
				"/usr/local/share/dotnet/dotnet", "/usr/share/dotnet/dotnet", "/opt/homebrew/bin/dotnet"
			});
		return candidates.FirstOrDefault(File.Exists) ?? name;
	}

	public static ProcessStartInfo CreateStartInfo(string executable, IEnumerable<string> arguments)
	{
		var info = new ProcessStartInfo(executable)
		{
			UseShellExecute = false, CreateNoWindow = true,
			RedirectStandardOutput = true, RedirectStandardError = true
		};
		foreach (var argument in arguments)
			info.ArgumentList.Add(argument);
		if (!info.Environment.TryGetValue("DOTNET_ROOT", out var dotnetRoot) || string.IsNullOrWhiteSpace(dotnetRoot))
		{
			var host = FindDotnet();
			var root = FindDotnetRoot(host);
			if (root != null) info.Environment["DOTNET_ROOT"] = root;
		}
		return info;
	}

	/// <summary>Finds the runtime root behind a direct host or a Homebrew launch wrapper.</summary>
	public static string FindDotnetRoot(string host)
	{
		if (!File.Exists(host)) return null;
		var resolved = new FileInfo(host).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? host;
		var directory = Path.GetDirectoryName(Path.GetFullPath(resolved));
		var candidates = new[] { directory, Path.GetFullPath(Path.Combine(directory, "..", "libexec")) };
		return candidates.FirstOrDefault(root => Directory.Exists(Path.Combine(root, "shared", "Microsoft.NETCore.App")));
	}

	public static void ConfigureEnvironment(ProcessStartInfo info, bool windows, bool mac, string home, string managedWineDirectory = null)
	{
		if (windows)
			return;
		if (managedWineDirectory == null && home == Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
			ShaderToolchainSetup.Default.ApplyInstallation(info);
		if (!info.Environment.TryGetValue("MGFXC_WINE_PATH", out var prefix) || string.IsNullOrWhiteSpace(prefix))
			prefix = Path.Combine(home, ".winemonogame");
		info.Environment["MGFXC_WINE_PATH"] = prefix;
		info.Environment["WINEPREFIX"] = prefix;
		info.Environment.TryGetValue("PATH", out var path);
		var directories = (path ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries).ToList();
		foreach (var directory in mac
			? new[] { "/opt/homebrew/bin", "/usr/local/bin", "/Library/Frameworks/Wine.framework/Versions/Current/bin", "/Applications/Wine Stable.app/Contents/Resources/wine/bin", "/usr/bin", "/bin" }
			: new[] { "/usr/local/bin", "/usr/bin", "/bin" })
			if (!directories.Contains(directory, StringComparer.Ordinal))
				directories.Add(directory);
		managedWineDirectory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltage", "Tools", "wine", "bin");
		if (!info.Environment.TryGetValue("WINELOADER", out var loader) || string.IsNullOrWhiteSpace(loader))
		{
			loader = new[] { "wine64", "wine" }.Select(name => Path.Combine(managedWineDirectory, name)).FirstOrDefault(File.Exists);
			if (loader != null)
			{
				info.Environment["WINELOADER"] = loader;
				directories.Remove(managedWineDirectory);
				directories.Insert(0, managedWineDirectory);
			}
		}
		info.Environment["PATH"] = string.Join(':', directories);
	}

	public static string WineProblem(ProcessStartInfo info, bool windows)
	{
		if (windows)
			return null;
		info.Environment.TryGetValue("MGFXC_WINE_PATH", out var prefix);
		info.Environment.TryGetValue("PATH", out var path);
		var dirs = (path ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries);
		bool HasCommand(string command) => dirs.Any(dir => File.Exists(Path.Combine(dir, command)));
		info.Environment.TryGetValue("WINELOADER", out var loader);
		bool hasLoader = !string.IsNullOrWhiteSpace(loader) && File.Exists(loader) || HasCommand("wine") || HasCommand("wine64");
		if (!hasLoader || !HasCommand("winepath"))
			return $"Shader compilation requires Wine and winepath. See {SetupUrl}";
		if (string.IsNullOrEmpty(prefix)) return $"MGFXC_WINE_PATH is missing. See {SetupUrl}";
		bool hasDotnet = File.Exists(Path.Combine(prefix, "drive_c", "windows", "system32", "dotnet.exe")) ||
			File.Exists(Path.Combine(prefix, "drive_c", "Program Files", "dotnet", "dotnet.exe")) ||
			File.Exists(Path.Combine(prefix, "drive_c", "dotnet", "dotnet.exe"));
		if (!hasDotnet || !File.Exists(Path.Combine(prefix, "drive_c", "windows", "system32", "d3dcompiler_47.dll")) ||
			!File.Exists(Path.Combine(prefix, "drive_c", "fxccs.dll")) ||
			!File.Exists(Path.Combine(prefix, "drive_c", "fxccs.runtimeconfig.json")))
			return $"MonoGame's Wine prefix is missing or incomplete at '{prefix}'. Configure MGFXC_WINE_PATH using {SetupUrl}";
		return null;
	}


	/// <summary>Uses a runnable Wine loader for MGFXC and winepath without changing the user's prefix or PATH.</summary>
	public static void ConfigureWineLauncher(ProcessStartInfo info, string launcherRoot, Action<string> log, CancellationToken cancel)
	{
		if (OperatingSystem.IsWindows()) return;
		info.Environment.TryGetValue("PATH", out var path);
		info.Environment.TryGetValue("WINELOADER", out var configured);
		var dirs = (path ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries);
		var candidates = new[] { configured }.Concat(new[] { "wine64", "wine" }
			.SelectMany(name => dirs.Select(dir => Path.Combine(dir, name))))
			.Where(candidate => !string.IsNullOrEmpty(candidate) && File.Exists(candidate)).Distinct();
		string loader = null;
		foreach (var candidate in candidates)
		{
			var probe = CreateStartInfo(candidate, new[] { "--version" });
			foreach (var pair in info.Environment) probe.Environment[pair.Key] = pair.Value;
			try
			{
				string version = "";
				var exit = Run(probe, message => { log(message); version += message; }, cancel, TimeSpan.FromSeconds(5));
				var match = System.Text.RegularExpressions.Regex.Match(version, @"wine-(\d+)");
				if (exit == 0 && match.Success && int.TryParse(match.Groups[1].Value, out var major) && major >= 8)
				{
					loader = candidate;
					break;
				}
			}
			catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or TimeoutException)
			{
				log($"Unable to run Wine loader '{candidate}': {ex.Message}");
			}
		}
		if (loader == null) throw new InvalidOperationException($"No runnable Wine loader found. See {SetupUrl}");
		loader = Path.GetFullPath(loader);
		var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(loader)));
		var directory = Path.Combine(launcherRoot, key);
		Directory.CreateDirectory(directory);
		var quotedLoader = "'" + loader.Replace("'", "'\"'\"'") + "'";
		var script = "#!/bin/sh\n" +
			"if [ \"$1\" = dotnet ] && [ \"$2\" = 'c:\\fxccs.dll' ] && [ \"$#\" -gt 8 ]; then\n" +
			"  host=$1; helper=$2; source=$3; entry=$4; profile=$5; flags=$6; shift 6\n" +
			"  display=$1; shift\n" +
			"  while [ \"$#\" -gt 1 ]; do display=\"$display $1\"; shift; done\n" +
			"  exec " + quotedLoader + " \"$host\" \"$helper\" \"$source\" \"$entry\" \"$profile\" \"$flags\" \"$display\" \"$1\"\n" +
			"fi\nexec " + quotedLoader + " \"$@\"\n";
		foreach (var command in new[] { "wine", "wine64" })
		{
			var shim = Path.Combine(directory, command);
			if (!File.Exists(shim) || File.ReadAllText(shim) != script) File.WriteAllText(shim, script);
			File.SetUnixFileMode(shim, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		}
		info.Environment["WINELOADER"] = loader;
		info.Environment["PATH"] = directory + ":" + path;
		log($"Using Wine loader: {loader}");
	}

	public static int Run(ProcessStartInfo info, Action<string> log, CancellationToken cancel, TimeSpan timeout)
	{
		cancel.ThrowIfCancellationRequested();
		using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {info.FileName}.");
		var stdout = process.StandardOutput.ReadToEndAsync();
		var stderr = process.StandardError.ReadToEndAsync();
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
		deadline.CancelAfter(timeout);
		try
		{
			Task.WhenAll(process.WaitForExitAsync(), stdout, stderr).WaitAsync(deadline.Token).GetAwaiter().GetResult();
		}
		catch (OperationCanceledException)
		{
			try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
			cancel.ThrowIfCancellationRequested();
			throw new TimeoutException($"{Path.GetFileName(info.FileName)} exceeded {timeout.TotalSeconds:0} seconds.");
		}
		log(stdout.GetAwaiter().GetResult());
		log(stderr.GetAwaiter().GetResult());
		return process.ExitCode;
	}

	public static string EnsureTool(string version, Action<string> log, CancellationToken cancel)
	{
		InstallLock.Wait(cancel);
		try
		{
			var directory = ToolDirectory(version);
			var executable = ToolPath(directory, OperatingSystem.IsWindows());
			if (File.Exists(executable))
				return executable;
			log($"Installing dotnet-mgfxc {version}; the first shader build requires network access.");
			var info = CreateStartInfo(FindDotnet(), new[] { "tool", "install", "dotnet-mgfxc", "--version", version, "--tool-path", directory });
			if (Run(info, log, cancel, TimeSpan.FromMinutes(5)) != 0 || !File.Exists(executable))
				throw new InvalidOperationException($"Unable to install dotnet-mgfxc {version}. Check the .NET SDK and NuGet access.");
			return executable;
		}
		finally { InstallLock.Release(); }
	}

	public static bool IsDesktopGl(byte[] bytes) => bytes.Length > 10 &&
		bytes[0] == 'M' && bytes[1] == 'G' && bytes[2] == 'F' && bytes[3] == 'X' && bytes[4] == 11 && bytes[5] == 0;

	public static string Fingerprint(string shader, string version)
	{
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		var visited = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
		void Add(string file)
		{
			file = Path.GetFullPath(file);
			if (!visited.Add(file)) return;
			if (!File.Exists(file))
			{
				hash.AppendData(Encoding.UTF8.GetBytes("missing:" + file));
				return;
			}
			var bytes = File.ReadAllBytes(file);
			hash.AppendData(Encoding.UTF8.GetBytes(file));
			hash.AppendData(bytes);
			foreach (System.Text.RegularExpressions.Match include in System.Text.RegularExpressions.Regex.Matches(
				Encoding.UTF8.GetString(bytes), "(?m)^\\s*#\\s*include\\s*[\"<]([^\">]+)[\">]"))
				Add(Path.Combine(Path.GetDirectoryName(file), include.Groups[1].Value));
		}
		hash.AppendData(Encoding.UTF8.GetBytes(version + ":" + Profile));
		Add(shader);
		return Convert.ToHexString(hash.GetHashAndReset());
	}

	public static string OutputStamp(string output, string fingerprint) => fingerprint + "\n" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output)));

	public static bool IsCurrent(string output, string fingerprint, string stamp = null) => File.Exists(output) &&
		File.Exists(stamp ?? output + ".sha256") && File.ReadAllText(stamp ?? output + ".sha256") == OutputStamp(output, fingerprint) && IsDesktopGl(File.ReadAllBytes(output));

	public static void Compile(string executable, string shader, string output, string fingerprint, Action<string> log, CancellationToken cancel, string stamp = null, ProcessStartInfo environment = null)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
		var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			var info = CreateStartInfo(executable, new[] { Path.GetFileName(shader), Path.GetFullPath(temporary), "/Profile:" + Profile });
			if (environment != null)
				foreach (var pair in environment.Environment) info.Environment[pair.Key] = pair.Value;
			info.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(shader));
			if (environment == null) ConfigureEnvironment(info, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
			var problem = WineProblem(info, OperatingSystem.IsWindows());
			if (problem != null) throw new InvalidOperationException(problem);
			if (!OperatingSystem.IsWindows())
				ConfigureWineLauncher(info, ToolDirectory("wine-launchers"), log, cancel);
			var diagnostics = new StringBuilder();
			var exitCode = Run(info, message =>
			{
				log(message);
				if (!string.IsNullOrWhiteSpace(message) && diagnostics.Length < 8000)
					diagnostics.AppendLine(message[..Math.Min(message.Length, 8000 - diagnostics.Length)]);
			}, cancel, TimeSpan.FromMinutes(2));
			if (exitCode != 0 || !File.Exists(temporary) || !IsDesktopGl(File.ReadAllBytes(temporary)))
				throw new InvalidOperationException($"Effect compilation failed (exit {exitCode}) or produced invalid DesktopGL output: {shader}\n{diagnostics.ToString().TrimEnd()}");
			cancel.ThrowIfCancellationRequested();
			File.Move(temporary, output, overwrite: true);
			stamp ??= output + ".sha256";
			Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(stamp)));
			File.WriteAllText(stamp, OutputStamp(output, fingerprint));
		}
		catch (Exception ex) when (ex is TimeoutException or System.ComponentModel.Win32Exception || ex.Message.Contains("Wine", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("fxccs", StringComparison.OrdinalIgnoreCase))
		{
			ShaderToolchainSetup.Default.Invalidate();
			throw;
		}
		finally { if (File.Exists(temporary)) File.Delete(temporary); }
	}
}
