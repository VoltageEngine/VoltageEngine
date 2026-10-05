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
			if (File.Exists(host))
			{
				var resolved = new FileInfo(host).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? host;
				info.Environment["DOTNET_ROOT"] = Path.GetDirectoryName(Path.GetFullPath(resolved));
			}
		}
		return info;
	}

	public static void ConfigureEnvironment(ProcessStartInfo info, bool windows, bool mac, string home)
	{
		if (windows)
			return;
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
		if (!HasCommand("wine") && !HasCommand("wine64") || !HasCommand("winepath"))
			return $"Shader compilation requires Wine and winepath. See {SetupUrl}";
		if (string.IsNullOrEmpty(prefix)) return $"MGFXC_WINE_PATH is missing. See {SetupUrl}";
		bool hasDotnet = File.Exists(Path.Combine(prefix, "drive_c", "windows", "system32", "dotnet.exe")) ||
			File.Exists(Path.Combine(prefix, "drive_c", "Program Files", "dotnet", "dotnet.exe"));
		if (!hasDotnet || !File.Exists(Path.Combine(prefix, "drive_c", "windows", "system32", "d3dcompiler_47.dll")))
			return $"MonoGame's Wine prefix is missing or incomplete at '{prefix}'. Configure MGFXC_WINE_PATH using {SetupUrl}";
		return null;
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

	public static void Compile(string executable, string shader, string output, string fingerprint, Action<string> log, CancellationToken cancel, string stamp = null)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
		var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			var info = CreateStartInfo(executable, new[] { shader, Path.GetFullPath(temporary), "/Profile:" + Profile });
			ConfigureEnvironment(info, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
			var problem = WineProblem(info, OperatingSystem.IsWindows());
			if (problem != null) throw new InvalidOperationException(problem);
			if (Run(info, log, cancel, TimeSpan.FromMinutes(2)) != 0 || !File.Exists(temporary) || !IsDesktopGl(File.ReadAllBytes(temporary)))
				throw new InvalidOperationException($"Effect compilation failed or produced invalid DesktopGL output: {shader}");
			cancel.ThrowIfCancellationRequested();
			File.Move(temporary, output, overwrite: true);
			stamp ??= output + ".sha256";
			Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(stamp)));
			File.WriteAllText(stamp, OutputStamp(output, fingerprint));
		}
		finally { if (File.Exists(temporary)) File.Delete(temporary); }
	}
}
