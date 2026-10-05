using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;
using Voltage.Editor.Effects;
using Voltage.Systems;

namespace Voltage.Effects.Tests;

[TestFixture]
public class EffectsTests
{
	private string scratch;
	[SetUp] public void SetUp() { scratch = Path.Combine(Path.GetTempPath(), "voltage-effects-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratch); }
	[TearDown] public void TearDown() { EffectResource.BuiltinOverrideDirectory = null; Directory.Delete(scratch, true); }

	[TestCase(true, false)]
	[TestCase(false, true)]
	[TestCase(false, false)]
	public void Environment_handles_windows_mac_linux_without_shell_profiles(bool windows, bool mac)
	{
		var info = EffectToolchain.CreateStartInfo("mgfxc", Array.Empty<string>());
		info.Environment.Clear();
		info.Environment["PATH"] = windows ? "C:/system" : "/custom/bin";
		EffectToolchain.ConfigureEnvironment(info, windows, mac, scratch);
		Assert.That(info.Environment.ContainsKey("MGFXC_WINE_PATH"), Is.EqualTo(!windows));
		if (windows) { Assert.That(EffectToolchain.WineProblem(info, true), Is.Null); return; }
		Assert.That(info.Environment["MGFXC_WINE_PATH"], Is.EqualTo(Path.Combine(scratch, ".winemonogame")));
		Assert.That(info.Environment["PATH"], Does.StartWith("/custom/bin:"));
		Assert.That(info.Environment["PATH"].Contains("/opt/homebrew/bin"), Is.EqualTo(mac));
	}

	[TestCase(true)]
	[TestCase(false)]
	public void Custom_wine_prefix_is_validated_instead_of_default_home_prefix(bool mac)
	{
		var prefix = Path.Combine(scratch, "custom prefix Ελληνικά");
		var binaries = Path.Combine(scratch, "bin");
		Directory.CreateDirectory(binaries);
		File.WriteAllText(Path.Combine(binaries, "wine"), "");
		File.WriteAllText(Path.Combine(binaries, "winepath"), "");
		var info = EffectToolchain.CreateStartInfo("mgfxc", Array.Empty<string>());
		info.Environment.Clear(); info.Environment["PATH"] = binaries; info.Environment["MGFXC_WINE_PATH"] = prefix;
		EffectToolchain.ConfigureEnvironment(info, false, mac, scratch);
		Assert.That(EffectToolchain.WineProblem(info, false), Does.Contain("incomplete"));
		var system = Path.Combine(prefix, "drive_c", "windows", "system32"); Directory.CreateDirectory(system);
		File.WriteAllText(Path.Combine(system, "dotnet.exe"), ""); File.WriteAllText(Path.Combine(system, "d3dcompiler_47.dll"), "");
		Assert.That(EffectToolchain.WineProblem(info, false), Is.Null);
		Assert.That(info.Environment["WINEPREFIX"], Is.EqualTo(prefix));
		File.Delete(Path.Combine(binaries, "winepath"));
		info.Environment["PATH"] = binaries;
		Assert.That(EffectToolchain.WineProblem(info, false), Does.Contain("winepath"));
	}

	[Test]
	public void Process_preserves_arguments_with_spaces_unicode_and_shell_characters()
	{
		var expected = new[] { "path with spaces Ελληνικά.fx", "a\"b", "$() & ; `" };
		string output = "";
		var info = Child(expected);
		Assert.That(EffectToolchain.Run(info, s => output += s, CancellationToken.None, TimeSpan.FromSeconds(10)), Is.Zero);
		Assert.That(JsonSerializer.Deserialize<string[]>(output), Is.EqualTo(expected));
	}

	[Test]
	public void Both_process_pipes_are_drained_without_deadlock()
	{
		int size = 0;
		Assert.That(EffectToolchain.Run(Child(new[] { "flood" }), s => size += s.Length, CancellationToken.None, TimeSpan.FromSeconds(10)), Is.Zero);
		Assert.That(size, Is.GreaterThan(2_000_000));
	}

	[Test]
	public void Hung_process_is_terminated_by_timeout()
	{
		var watch = Stopwatch.StartNew();
		Assert.Throws<TimeoutException>(() => EffectToolchain.Run(Child(new[] { "hang" }), _ => { }, CancellationToken.None, TimeSpan.FromSeconds(1)));
		Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(8));
	}

	[Test]
	public void Cancellation_terminates_running_process()
	{
		using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(1));
		Assert.Throws<OperationCanceledException>(() => EffectToolchain.Run(Child(new[] { "hang" }), _ => { }, cancel.Token, TimeSpan.FromSeconds(20)));
	}

	[Test]
	public void Cache_invalidates_for_nested_includes_tool_version_and_invalid_output()
	{
		var shader = Path.Combine(scratch, "test.fx"); var include = Path.Combine(scratch, "shared.fxh"); var nested = Path.Combine(scratch, "nested.inc");
		File.WriteAllText(shader, "#include \"shared.fxh\""); File.WriteAllText(include, "#include \"nested.inc\""); File.WriteAllText(nested, "one");
		var fingerprint = EffectToolchain.Fingerprint(shader, "3.8.5.1");
		Assert.That(EffectToolchain.Fingerprint(shader, "3.8.5.1"), Is.EqualTo(fingerprint));
		Assert.That(EffectToolchain.Fingerprint(shader, "3.8.5.2"), Is.Not.EqualTo(fingerprint));
		var output = Path.Combine(scratch, "test.mgfxo");
		File.WriteAllBytes(output, EffectResource.GetFileResourceBytes("Content/Voltage/Effects/Invert.mgfxo")); File.WriteAllText(output + ".sha256", EffectToolchain.OutputStamp(output, fingerprint));
		Assert.That(EffectToolchain.IsCurrent(output, fingerprint), Is.True);
		File.WriteAllText(nested, "two"); Assert.That(EffectToolchain.IsCurrent(output, EffectToolchain.Fingerprint(shader, "3.8.5.1")), Is.False);
		var directX = File.ReadAllBytes(output); directX[5] = 1; File.WriteAllBytes(output, directX);
		Assert.That(EffectToolchain.IsCurrent(output, fingerprint), Is.False);
		directX[5] = 0; directX[^1] ^= 1; File.WriteAllBytes(output, directX);
		Assert.That(EffectToolchain.IsCurrent(output, fingerprint), Is.False);
	}

	[Test]
	public void Bundled_effects_cover_every_source_without_loose_content_or_compiler()
	{
		Assert.That(EffectResource.BundledEffectNames.Length, Is.EqualTo(34));
		var assembly = typeof(EffectResource).Assembly;
		foreach (var name in EffectResource.BundledEffectNames)
		{
			using var stream = assembly.GetManifestResourceStream(name); using var buffer = new MemoryStream(); stream.CopyTo(buffer);
			var bytes = buffer.ToArray(); Assert.That(EffectToolchain.IsDesktopGl(bytes), Is.True, name);
			Assert.That(Encoding.UTF8.GetString(bytes), Does.Not.Contain("C:\\Users\\"));
		}
		Assert.That(EffectToolchain.IsDesktopGl(EffectResource.GetFileResourceBytes("Content/Voltage/Effects/transitions/Squares.mgfxo")), Is.True);
		EffectResource.BuiltinOverrideDirectory = scratch;
		File.WriteAllText(Path.Combine(scratch, "Invert.mgfxo"), "bad old output");
		Assert.That(EffectToolchain.IsDesktopGl(EffectResource.GetFileResourceBytes("Content/Voltage/Effects/Invert.mgfxo")), Is.True);
	}

	[TestCase("Windows", "\r\n")]
	[TestCase("macOS", "\n")]
	[TestCase("Linux", "\n")]
	public void Shader_manifest_matches_platform_checkout_line_endings(string platform, string newline)
	{
		var root = new DirectoryInfo(AppContext.BaseDirectory);
		while (root != null && !File.Exists(Path.Combine(root.FullName, "Voltage.Engine", "Voltage.Engine.csproj"))) root = root.Parent;
		Assert.That(root, Is.Not.Null);
		var folder = Path.Combine(root.FullName, "Voltage.Engine", "Graphics", "Effects", "Compiled");
		var manifest = XElement.Load(Path.Combine(folder, "manifest.props"));
		foreach (var item in manifest.Descendants("_BundledEffectInput").Where(i => i.Attribute("Include").Value.EndsWith(".fx")))
		{
			var path = Path.GetFullPath(item.Attribute("Include").Value.Replace("$(MSBuildThisFileDirectory)", folder + Path.DirectorySeparatorChar));
			var text = Encoding.UTF8.GetString(File.ReadAllBytes(path)).Replace("\r\n", "\n").Replace("\n", newline);
			var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
			Assert.That(hash, Is.EqualTo(item.Element(platform == "Windows" ? "WindowsHash" : "ExpectedHash").Value), path);
		}
	}

	[Test]
	public void Native_shader_compile_is_incremental_and_preserves_valid_output_on_error()
	{
		var compiler = Environment.GetEnvironmentVariable("VOLTAGE_TEST_MGFXC");
		if (string.IsNullOrEmpty(compiler)) Assert.Ignore("Set VOLTAGE_TEST_MGFXC to the pinned compiler to exercise native shader compilation.");
		var source = Path.Combine(scratch, "shader Ελληνικά.fx"); var output = Path.Combine(scratch, "result.mgfxo");
		File.WriteAllText(source, "#if 0\n#include \"unused-missing.fxh\"\n#endif\nsampler s0; float4 Main(float2 uv:TEXCOORD0):COLOR0 {return tex2D(s0,uv);} technique T {pass P {PixelShader=compile ps_2_0 Main();}}");
		var hash = EffectToolchain.Fingerprint(source, "3.8.5.1");
		EffectToolchain.Compile(compiler, source, output, hash, TestContext.WriteLine, CancellationToken.None);
		var good = File.ReadAllBytes(output); Assert.That(EffectToolchain.IsCurrent(output, hash), Is.True);
		File.WriteAllText(source, "invalid shader");
		Assert.Throws<InvalidOperationException>(() => EffectToolchain.Compile(compiler, source, output, EffectToolchain.Fingerprint(source, "3.8.5.1"), TestContext.WriteLine, CancellationToken.None));
		Assert.That(File.ReadAllBytes(output), Is.EqualTo(good)); Assert.That(Directory.GetFiles(scratch, "*.tmp"), Is.Empty);
	}

	[Test]
	public void Native_gpu_accepts_all_bundled_effects()
	{
		if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("VOLTAGE_TEST_GPU") != "1") Assert.Ignore("Requires a native desktop graphics session.");
		using var game = new EffectGame(scratch); game.Run(); Assert.That(game.Loaded, Is.EqualTo(34));
		Assert.That(game.CustomLoaded, Is.EqualTo(6));
	}

	private static ProcessStartInfo Child(string[] arguments) => EffectToolchain.CreateStartInfo(EffectToolchain.FindDotnet(),
		new[] { typeof(Program).Assembly.Location }.Concat(arguments));

	private sealed class EffectGame : Game
	{
		public int Loaded;
		public int CustomLoaded;
		private readonly string project;
		public EffectGame(string root) { project = root; var manager = new GraphicsDeviceManager(this) { PreferredBackBufferWidth = 128, PreferredBackBufferHeight = 64 }; }
		protected override void LoadContent()
		{
			var assembly = typeof(EffectResource).Assembly;
			foreach (var name in EffectResource.BundledEffectNames)
			{
				using var stream = assembly.GetManifestResourceStream(name); using var buffer = new MemoryStream(); stream.CopyTo(buffer);
				using var effect = new Effect(GraphicsDevice, buffer.ToArray()); effect.CurrentTechnique.Passes[0].Apply(); Loaded++;
			}
			var oldRoot = VoltageContentManager.ContentRoot;
			var oldDevice = Core.GraphicsDevice;
			try
			{
				VoltageContentManager.ContentRoot = project;
				Core.GraphicsDevice = GraphicsDevice;
				var content = Path.Combine(project, "Content");
				var folder = Path.Combine(content, "Effects"); Directory.CreateDirectory(folder);
				var file = Path.Combine(folder, "shader Ελληνικά.mgfxo");
				File.WriteAllBytes(file, EffectResource.GetFileResourceBytes("Content/Voltage/Effects/Invert.mgfxo"));
				foreach (var root in new[] { "Content", content })
				{
					using var manager = new VoltageContentManager(Services, root);
					foreach (var path in new[] { "Effects/shader Ελληνικά.mgfxo", "Content/Effects/shader Ελληνικά.mgfxo", file })
					{
						var effect = manager.LoadEffect<Effect>(path); effect.CurrentTechnique.Passes[0].Apply(); CustomLoaded++;
					}
				}
			}
			finally { VoltageContentManager.ContentRoot = oldRoot; Core.GraphicsDevice = oldDevice; }
			Exit();
		}
	}
}
