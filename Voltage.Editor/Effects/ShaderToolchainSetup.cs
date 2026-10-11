using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Voltage.Diagnostics;

namespace Voltage.Editor.Effects;

/// <summary>Installs and verifies the per-user shader toolchain, with a local success stamp.</summary>
public sealed class ShaderToolchainSetup
{
	public static ShaderToolchainSetup Default { get; } = new(
		Environment.GetEnvironmentVariable("VOLTAGE_SHADER_SETUP_ROOT") ?? Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltage", "Tools", "shader-setup"));
	private readonly string _root;
	private readonly object _sync = new();
	private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
	private const string RuntimeUrl = "https://builds.dotnet.microsoft.com/dotnet/Runtime/8.0.31/dotnet-runtime-8.0.31-win-x64.zip";
	private const string RuntimeHash = "9c55c58694676ee64b0eed2cd6d8cbf58b9aa8288420acc66841e15ca0099c75d4af0182d23a641c2342e5a151a325df4a12fa0bde2e47c0fb7e9a33e7b09896";
	private const string HelperHash = "99464537d6d329eee9b7c5dce25915828692a0ee7ba48819e1656b900fd798a6";
	private const string DirectXHash = "721977f36c008af2b637aedd3f1b529f3cfed6feb10f68ebe17469acb1934986";
	private string SettingsPath => Path.Combine(_root, "installation.json");
	private string StampPath => Path.Combine(_root, "verified.json");
	public string ManagedPrefix => Path.Combine(_root, "prefix");
	public sealed class Installation { public string Loader { get; set; } public string Prefix { get; set; } }

	public ShaderToolchainSetup(string root) => _root = Path.GetFullPath(root);

	public void ApplyInstallation(ProcessStartInfo info)
	{
		lock (_sync)
		{
			try
			{
				if (!File.Exists(SettingsPath)) return;
				var saved = JsonSerializer.Deserialize<Installation>(File.ReadAllText(SettingsPath));
				if (!string.IsNullOrWhiteSpace(saved?.Loader)) info.Environment["WINELOADER"] = saved.Loader;
				if (!string.IsNullOrWhiteSpace(saved?.Prefix))
				{
					info.Environment["MGFXC_WINE_PATH"] = saved.Prefix;
					info.Environment["WINEPREFIX"] = saved.Prefix;
				}
				if (!string.IsNullOrWhiteSpace(saved?.Loader)) AddPath(info, Path.GetDirectoryName(saved.Loader));
			}
			catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
		}
	}

	public ProcessStartInfo EnvironmentInfo(string loader = null, string prefix = null)
	{
		var info = EffectToolchain.CreateStartInfo("mgfxc", Array.Empty<string>());
		EffectToolchain.ConfigureEnvironment(info, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
		ApplyInstallation(info);
		if (!string.IsNullOrWhiteSpace(loader)) { info.Environment["WINELOADER"] = Path.GetFullPath(loader); AddPath(info, Path.GetDirectoryName(Path.GetFullPath(loader))); }
		if (!string.IsNullOrWhiteSpace(prefix)) { info.Environment["MGFXC_WINE_PATH"] = Path.GetFullPath(prefix); info.Environment["WINEPREFIX"] = Path.GetFullPath(prefix); }
		return info;
	}

	private static void AddPath(ProcessStartInfo info, string directory) => info.Environment["PATH"] = directory + ":" + info.Environment["PATH"];

	public static string FindCommand(ProcessStartInfo info, string name) => (info.Environment["PATH"] ?? "")
		.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(p => Path.Combine(p, name)).FirstOrDefault(File.Exists);

	public static string Loader(ProcessStartInfo info) => info.Environment.TryGetValue("WINELOADER", out var loader) && !string.IsNullOrWhiteSpace(loader)
		? loader : FindCommand(info, "wine64") ?? FindCommand(info, "wine");

	/// <summary>Checks metadata only; no subprocesses, downloads or shader compilation.</summary>
	public bool IsVerified(ProcessStartInfo info, string version)
	{
		lock (_sync)
		{
			try { return File.Exists(StampPath) && File.ReadAllText(StampPath) == Signature(info, version); }
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
		}
	}

	public static string Signature(ProcessStartInfo info, string version)
	{
		var text = new StringBuilder("shader-setup-v1\n" + version + "\n");
		void Add(string path)
		{
			if (string.IsNullOrEmpty(path) || !File.Exists(path)) throw new FileNotFoundException("A shader tool is missing.", path);
			var file = new FileInfo(path);
			var resolved = file.ResolveLinkTarget(true);
			text.AppendLine(file.FullName + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks);
			if (!OperatingSystem.IsWindows()) text.AppendLine(File.GetUnixFileMode(path).ToString());
			if (resolved is FileInfo target)
			{
				text.AppendLine(target.FullName + "|" + target.Length + "|" + target.LastWriteTimeUtc.Ticks);
				if (!OperatingSystem.IsWindows()) text.AppendLine(File.GetUnixFileMode(target.FullName).ToString());
			}
		}
		var compiler = File.Exists(info.FileName) ? info.FileName : EffectToolchain.ToolPath(EffectToolchain.ToolDirectory(version), OperatingSystem.IsWindows());
		Add(compiler);
		var payload = Path.Combine(Path.GetDirectoryName(compiler), ".store", "dotnet-mgfxc", version);
		if (Directory.Exists(payload))
			foreach (var file in Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories)
				.Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal)) Add(file);
		if (!OperatingSystem.IsWindows())
		{
			Add(Loader(info)); Add(FindCommand(info, "winepath"));
			var prefix = info.Environment["MGFXC_WINE_PATH"];
			foreach (var relative in new[] { "fxccs.dll", "fxccs.runtimeconfig.json", "fxccs.deps.json", "SharpDX.dll", "SharpDX.D3DCompiler.dll", "windows/system32/d3dcompiler_47.dll" }) Add(Path.Combine(prefix, "drive_c", relative));
			var dotnet = new[] { "windows/system32", "Program Files/dotnet", "dotnet" }
				.Select(p => Path.Combine(prefix, "drive_c", p)).FirstOrDefault(p => File.Exists(Path.Combine(p, "dotnet.exe")));
			Add(Path.Combine(dotnet ?? "", "dotnet.exe"));
			var shared = Path.Combine(dotnet, "shared", "Microsoft.NETCore.App");
			if (!Directory.Exists(shared)) throw new DirectoryNotFoundException("The Windows .NET runtime is missing.");
			foreach (var file in Directory.EnumerateFiles(shared, "*.dll", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal)) Add(file);
			var hosts = Path.Combine(dotnet, "host", "fxr");
			if (!Directory.Exists(hosts)) throw new DirectoryNotFoundException("The Windows .NET host is missing.");
			foreach (var file in Directory.EnumerateFiles(hosts, "hostfxr.dll", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal)) Add(file);
		}
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
	}

	public void MarkVerified(ProcessStartInfo info, string version)
	{
		lock (_sync) { Directory.CreateDirectory(_root); File.WriteAllText(StampPath, Signature(info, version)); }
	}

	public void Invalidate()
	{
		lock (_sync) { if (File.Exists(StampPath)) File.Delete(StampPath); }
	}

	public void Verify(string version, Action<string> log, CancellationToken cancel, string loader = null, string prefix = null)
	{
		var info = EnvironmentInfo(loader, prefix);
		var problem = EffectToolchain.WineProblem(info, OperatingSystem.IsWindows());
		if (problem != null) throw new InvalidOperationException(problem);
		if (IsVerified(info, version))
		{
			if (loader != null || prefix != null) SaveInstallation(info);
			return;
		}
		var compiler = EffectToolchain.EnsureTool(version, log, cancel);
		info.FileName = compiler;
		var scratch = Path.Combine(_root, "check-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(scratch);
		try
		{
			log("Checking shader compiler");
			var shader = Path.Combine(scratch, "check.fx");
			File.WriteAllText(shader, "float4 Main(float2 uv:TEXCOORD0):COLOR0 {return float4(1,1,1,1);} technique T {pass P {PixelShader=compile ps_2_0 Main();}}");
			EffectToolchain.Compile(compiler, shader, Path.Combine(scratch, "check.mgfxo"), EffectToolchain.Fingerprint(shader, version), log, cancel, environment: info);
			cancel.ThrowIfCancellationRequested();
			MarkVerified(info, version);
			if (loader != null || prefix != null) SaveInstallation(info);
		}
		finally { Directory.Delete(scratch, true); }
	}

	private void SaveInstallation(ProcessStartInfo info)
	{
		lock (_sync) File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Installation { Loader = Loader(info), Prefix = info.Environment["MGFXC_WINE_PATH"] }));
	}

	public void Install(string version, Action<string> progress, CancellationToken cancel)
	{
		if (OperatingSystem.IsWindows()) { Verify(version, progress, cancel); return; }
		var info = EnvironmentInfo();
		var loader = Loader(info);
		if (string.IsNullOrEmpty(loader) || !File.Exists(loader))
		{
			progress("Installing Wine"); InstallWine(info, progress, cancel);
			info = EnvironmentInfo();
			info.Environment.Remove("WINELOADER"); loader = Loader(info);
		}
		if (string.IsNullOrEmpty(loader) || !File.Exists(loader)) throw new InvalidOperationException("Wine was not found. Choose an existing installation.");
		if (EffectToolchain.WineProblem(info, false) == null)
		{
			try { Verify(version, progress, cancel, loader); return; }
			catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception) { progress("Preparing a separate shader environment"); }
		}
		var prefix = ManagedPrefix;
		if (Directory.Exists(prefix)) prefix = ManagedPrefix + "-" + Guid.NewGuid().ToString("N");
		var staging = Path.Combine(_root, "download-" + Guid.NewGuid().ToString("N"));
		var verified = false;
		Directory.CreateDirectory(staging);
		try
		{
			progress("Preparing shader tools");
			var boot = EffectToolchain.CreateStartInfo(loader, new[] { "wineboot", "-u" });
			CopyEnvironment(info, boot); boot.Environment["WINEPREFIX"] = prefix; boot.Environment["WINEARCH"] = "win64";
			boot.Environment["WINEDLLOVERRIDES"] = "mscoree,mshtml="; boot.Environment["WINEDEBUG"] = "-all";
			if (EffectToolchain.Run(boot, _ => { }, cancel, TimeSpan.FromMinutes(3)) != 0) throw new InvalidOperationException("Wine could not create the shader environment.");
			progress("Downloading shader tools");
			var runtime = Download(RuntimeUrl, RuntimeHash, staging, cancel);
			ExtractZip(runtime, Path.Combine(prefix, "drive_c", "windows", "system32"), cancel);
			var helper = Download("https://monogame.net/downloads/fxccs.zip", HelperHash, staging, cancel);
			ExtractZip(helper, Path.Combine(prefix, "drive_c"), cancel);
			var directX = Download("https://download-installer.cdn.mozilla.net/pub/firefox/releases/62.0.3/win64/ach/Firefox%20Setup%2062.0.3.exe", DirectXHash, staging, cancel);
			var extractor = FindCommand(info, "7zz") ?? FindCommand(info, "7z");
			if (extractor == null) { InstallExtractor(info, progress, cancel); extractor = FindCommand(info, "7zz") ?? FindCommand(info, "7z"); }
			if (extractor == null) throw new InvalidOperationException("Install 7-Zip, then retry.");
			var extract = EffectToolchain.CreateStartInfo(extractor, new[] { "e", directX, "core/d3dcompiler_47.dll", "-o" + Path.Combine(prefix, "drive_c", "windows", "system32"), "-aoa" });
			if (EffectToolchain.Run(extract, _ => { }, cancel, TimeSpan.FromMinutes(1)) != 0) throw new InvalidOperationException("Could not extract the shader compiler library.");
			Verify(version, progress, cancel, loader, prefix);
			verified = true;
		}
		finally
		{
			Directory.Delete(staging, true);
			if (!verified && Directory.Exists(prefix)) Directory.Delete(prefix, true);
		}
	}

	private static void CopyEnvironment(ProcessStartInfo source, ProcessStartInfo target)
	{
		foreach (var pair in source.Environment) target.Environment[pair.Key] = pair.Value;
	}

	private static void InstallWine(ProcessStartInfo info, Action<string> log, CancellationToken cancel)
	{
		if (OperatingSystem.IsMacOS())
		{
			var brew = FindCommand(info, "brew") ?? throw new InvalidOperationException("Install Homebrew or choose an existing Wine installation.");
			RunInstall(brew, new[] { "install", "--cask", "wine-stable", "xquartz" }, log, cancel); return;
		}
		InstallLinuxPackages(new Dictionary<PackageManagerKind, string[]> {
			[PackageManagerKind.Apt] = new[] { "wine", "wine64" }, [PackageManagerKind.Dnf] = new[] { "wine-core", "wine-common" },
			[PackageManagerKind.Pacman] = new[] { "wine" }, [PackageManagerKind.Zypper] = new[] { "wine" }
		}, log, cancel);
	}

	private static void InstallExtractor(ProcessStartInfo info, Action<string> log, CancellationToken cancel)
	{
		log("Installing 7-Zip");
		if (OperatingSystem.IsMacOS())
		{
			var brew = FindCommand(info, "brew") ?? throw new InvalidOperationException("Install 7-Zip with Homebrew, then retry.");
			RunInstall(brew, new[] { "install", "sevenzip" }, log, cancel); return;
		}
		InstallLinuxPackages(new Dictionary<PackageManagerKind, string[]> {
			[PackageManagerKind.Apt] = new[] { "p7zip-full" }, [PackageManagerKind.Dnf] = new[] { "7zip" },
			[PackageManagerKind.Pacman] = new[] { "7zip" }, [PackageManagerKind.Zypper] = new[] { "7zip" }
		}, log, cancel);
	}

	private static void InstallLinuxPackages(Dictionary<PackageManagerKind, string[]> packages, Action<string> log, CancellationToken cancel)
	{
		if (HostPackageManager.IsImmutableRootFilesystem()) throw new InvalidOperationException("Use Wine in your development container, or choose a portable Wine installation.");
		var manager = HostPackageManager.Detect();
		if (!packages.TryGetValue(manager, out var names)) throw new InvalidOperationException("Install Wine and 7-Zip, then retry.");
		var command = HostPackageManager.BuildInstallCommand(manager, names).Split(' ', StringSplitOptions.RemoveEmptyEntries);
		RunInstall("pkexec", command, log, cancel);
	}

	private static void RunInstall(string executable, string[] args, Action<string> log, CancellationToken cancel)
	{
		if (EffectToolchain.Run(EffectToolchain.CreateStartInfo(executable, args), log, cancel, TimeSpan.FromMinutes(15)) != 0)
			throw new InvalidOperationException("Installation did not finish. Retry or choose an existing installation.");
	}

	public static string Download(string url, string hash, string directory, CancellationToken cancel)
	{
		var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".download");
		try
		{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
			deadline.CancelAfter(TimeSpan.FromMinutes(5));
			using var response = Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, deadline.Token).GetAwaiter().GetResult();
			response.EnsureSuccessStatusCode();
			using var input = response.Content.ReadAsStream(deadline.Token);
			using (var output = File.Create(path))
			{
				var buffer = new byte[81920];
				long total = 0;
				int count;
				while ((count = input.ReadAsync(buffer, deadline.Token).GetAwaiter().GetResult()) > 0)
				{
					if ((total += count) > 512L * 1024 * 1024) throw new InvalidDataException("Shader tool download is too large.");
					output.Write(buffer, 0, count);
				}
			}
			using var file = File.OpenRead(path);
			var actual = Convert.ToHexString(hash.Length == 128 ? SHA512.HashData(file) : SHA256.HashData(file));
			if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Shader tool download failed verification. Retry later.");
			return path;
		}
		catch { if (File.Exists(path)) File.Delete(path); throw; }
	}

	public static void ExtractZip(string archive, string destination, CancellationToken cancel)
	{
		using var zip = ZipFile.OpenRead(archive);
		if (zip.Entries.Count > 20000 || zip.Entries.Sum(entry => entry.Length) > 1024L * 1024 * 1024) throw new InvalidDataException("Shader tool archive is too large.");
		var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
		foreach (var entry in zip.Entries)
		{
			cancel.ThrowIfCancellationRequested();
			var path = Path.GetFullPath(Path.Combine(root, entry.FullName));
			if (!path.StartsWith(root, StringComparison.Ordinal)) throw new InvalidDataException("Unsafe shader tool archive.");
			if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(path); continue; }
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			entry.ExtractToFile(path, true);
		}
	}
}
