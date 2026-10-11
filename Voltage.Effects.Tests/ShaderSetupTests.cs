using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using Voltage.Editor.Effects;

namespace Voltage.Effects.Tests;

[TestFixture]
public class ShaderSetupTests
{
	private string _scratch;
	private ShaderToolchainSetup _setup;
	private ProcessStartInfo _info;
	private string _prefix;

	[SetUp]
	public void SetUp()
	{
		_scratch = Path.Combine(Path.GetTempPath(), "shader-setup-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_scratch);
		_setup = new ShaderToolchainSetup(Path.Combine(_scratch, "setup"));
		_prefix = Path.Combine(_scratch, "prefix");
		_info = new ProcessStartInfo(Path.Combine(_scratch, "mgfxc"));
		_info.Environment["PATH"] = _scratch;
		_info.Environment["WINELOADER"] = Path.Combine(_scratch, "wine");
		_info.Environment["MGFXC_WINE_PATH"] = _prefix;
		foreach (var name in new[] { "mgfxc", "wine", "winepath" }) Write(Path.Combine(_scratch, name));
		foreach (var relative in new[] { "fxccs.dll", "fxccs.runtimeconfig.json", "fxccs.deps.json", "SharpDX.dll", "SharpDX.D3DCompiler.dll", "windows/system32/d3dcompiler_47.dll", "windows/system32/dotnet.exe", "windows/system32/shared/Microsoft.NETCore.App/8.0.31/coreclr.dll", "windows/system32/host/fxr/8.0.31/hostfxr.dll" }) Write(Path.Combine(_prefix, "drive_c", relative));
	}

	[TearDown] public void TearDown() => Directory.Delete(_scratch, true);
	private static void Write(string path) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "test"); }

	[Test]
	public void Verified_setup_survives_restart_and_needs_no_process_or_network()
	{
		_setup.MarkVerified(_info, "test-version");
		Assert.That(new ShaderToolchainSetup(Path.Combine(_scratch, "setup")).IsVerified(_info, "test-version"), Is.True);
		Assert.That(_setup.IsVerified(_info, "new-compiler-version"), Is.False);
		_setup.Invalidate();
		Assert.That(_setup.IsVerified(_info, "test-version"), Is.False);
	}

	[TestCase("wine")]
	[TestCase("winepath")]
	[TestCase("mgfxc")]
	[TestCase("prefix/drive_c/fxccs.dll")]
	[TestCase("prefix/drive_c/windows/system32/d3dcompiler_47.dll")]
	[TestCase("prefix/drive_c/windows/system32/shared/Microsoft.NETCore.App/8.0.31/coreclr.dll")]
	public void Missing_or_changed_tool_invalidates_the_success_stamp(string relative)
	{
		if (OperatingSystem.IsWindows() && relative != "mgfxc") Assert.Ignore("Wine is not used on Windows.");
		_setup.MarkVerified(_info, "test-version");
		var path = Path.Combine(_scratch, relative);
		File.WriteAllText(path, "corrupted or replaced");
		Assert.That(_setup.IsVerified(_info, "test-version"), Is.False);
		_setup.MarkVerified(_info, "test-version");
		File.Delete(path);
		Assert.That(_setup.IsVerified(_info, "test-version"), Is.False);
	}

	[Test]
	public void Selecting_an_already_verified_installation_still_saves_it()
	{
		if (OperatingSystem.IsWindows()) Assert.Ignore("Wine is not used on Windows.");
		var version = "setup-selection-test-" + Guid.NewGuid().ToString("N");
		_info.FileName = EffectToolchain.ToolPath(EffectToolchain.ToolDirectory(version), false);
		Write(_info.FileName);
		try
		{
			_setup.MarkVerified(_info, version);
			_setup.Verify(version, _ => Assert.Fail("Verified setup must not run tools."), CancellationToken.None, _info.Environment["WINELOADER"], _prefix);
			var restarted = new ShaderToolchainSetup(Path.Combine(_scratch, "setup"));
			var environment = new ProcessStartInfo();
			restarted.ApplyInstallation(environment);
			Assert.That(environment.Environment["WINELOADER"], Is.EqualTo(_info.Environment["WINELOADER"]));
			Assert.That(environment.Environment["MGFXC_WINE_PATH"], Is.EqualTo(_prefix));
		}
		finally { Directory.Delete(EffectToolchain.ToolDirectory(version), true); }
	}

	[Test]
	public void Changing_wine_prefix_invalidates_verification()
	{
		if (OperatingSystem.IsWindows()) Assert.Ignore("Wine is not used on Windows.");
		_setup.MarkVerified(_info, "test-version");
		_info.Environment["MGFXC_WINE_PATH"] = Path.Combine(_scratch, "different-prefix");
		Assert.That(_setup.IsVerified(_info, "test-version"), Is.False);
	}

	[Test]
	public void Removing_execute_permission_invalidates_verification()
	{
		if (OperatingSystem.IsWindows()) Assert.Ignore("Unix permissions.");
		var path = _info.Environment["WINELOADER"];
		File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
		_setup.MarkVerified(_info, "test-version");
		File.SetUnixFileMode(path, UnixFileMode.UserRead);
		Assert.That(_setup.IsVerified(_info, "test-version"), Is.False);
	}

	[Test]
	public void Archive_cannot_escape_the_managed_prefix()
	{
		var archive = Path.Combine(_scratch, "unsafe.zip");
		using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
		using (var writer = new StreamWriter(zip.CreateEntry("../outside.txt").Open())) writer.Write("bad");
		Assert.Throws<InvalidDataException>(() => ShaderToolchainSetup.ExtractZip(archive, Path.Combine(_scratch, "destination"), CancellationToken.None));
		Assert.That(File.Exists(Path.Combine(_scratch, "outside.txt")), Is.False);
	}

	[Test]
	public void Cancelled_extraction_does_not_write_files()
	{
		var archive = Path.Combine(_scratch, "tools.zip");
		using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
		using (var writer = new StreamWriter(zip.CreateEntry("tool.dll").Open())) writer.Write("tool");
		using var cancel = new CancellationTokenSource(); cancel.Cancel();
		Assert.Throws<OperationCanceledException>(() => ShaderToolchainSetup.ExtractZip(archive, Path.Combine(_scratch, "destination"), cancel.Token));
		Assert.That(Directory.Exists(Path.Combine(_scratch, "destination")), Is.False);
	}
}
