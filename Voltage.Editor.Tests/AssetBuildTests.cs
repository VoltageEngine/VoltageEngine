using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Voltage.Cli;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture]
public class AssetBuildTests
{
	[Test]
	public void Settings_round_trip_through_project_settings()
	{
		var set = Call("assetbuild.settings", new { enabled = true, compileAudio = true, textureFormat = "Compressed" });
		Assert.That(set.GetProperty("saved").GetBoolean(), Is.True);

		var read = Call("assetbuild.settings");
		Assert.That(read.GetProperty("enabled").GetBoolean(), Is.True);
		Assert.That(read.GetProperty("compileAudio").GetBoolean(), Is.True);
		Assert.That(read.GetProperty("textureFormat").GetString(), Is.EqualTo("Compressed"));

		var file = Path.Combine(Path.GetDirectoryName(ProjectFile)!, "ProjectSettings.json");
		Assert.That(File.ReadAllText(file), Does.Contain("\"AssetBuild\""));

		Call("assetbuild.settings", new { enabled = false, compileAudio = false, textureFormat = "Color" });
		Assert.That(Call("assetbuild.settings").GetProperty("enabled").GetBoolean(), Is.False);
	}

	[Test]
	public void Rules_round_trip_and_override_the_built_in_table()
	{
		var set = Call("assetbuild.rule.set", new { name = "CopyPng", extensions = new[] { ".png" }, action = "copy" });
		Assert.That(set.GetProperty("created").GetBoolean(), Is.True);
		var rules = Call("assetbuild.rules").GetProperty("project").EnumerateArray().ToList();
		Assert.That(rules.Any(r => r.GetProperty("name").GetString() == "CopyPng" && r.GetProperty("action").GetString() == "copy"), Is.True);

		var file = Path.Combine(Path.GetDirectoryName(ProjectFile)!, "ProjectSettings.json");
		Assert.That(File.ReadAllText(file), Does.Contain("\"CopyPng\""));

		Assert.That(Call("assetbuild.rule.remove", new { name = "CopyPng" }).GetProperty("removed").GetString(), Is.EqualTo("CopyPng"));
		Assert.That(Call("assetbuild.rules").GetProperty("project").GetArrayLength(), Is.EqualTo(0));
	}

	[Test]
	public void Rules_outside_the_project_or_with_line_breaks_are_refused_everywhere()
	{
		Assert.That(Assert.Throws<CliException>(() => Call("assetbuild.rule.set", new { name = "Evil", extensions = new[] { ".x" }, action = "compile", importer = "A", processor = "B", assembly = "../evil.dll" }))!.Message,
			Does.Contain("inside the project"));
		Assert.That(Assert.Throws<CliException>(() => Call("assetbuild.rule.set", new { name = "Evil", extensions = new[] { ".x" }, action = "compile", importer = "A", processor = "B", parameters = new[] { "Key=a\n/reference:evil" } }))!.Message,
			Does.Contain("Key=Value"));

		// A hand-edited settings file bypasses the gateway, so the plan itself must fail the rule's files.
		var png = Path.Combine(Path.GetDirectoryName(EditorExe)!, "DefaultContent", "Fonts", "VoltageDefaultBMFont.png");
		if (!File.Exists(png))
			Assert.Ignore($"sample texture not found at {png}");
		Call("asset.import", new { source = png, destination = "Textures", name = "Evil.png", overwrite = true });

		var file = Path.Combine(Path.GetDirectoryName(ProjectFile)!, "ProjectSettings.json");
		var json = File.ReadAllText(file);
		var evil = "\"Rules\": [{\"Name\":\"Evil\",\"Extensions\":[\".png\"],\"Action\":\"compile\",\"Importer\":\"A\",\"Processor\":\"B\",\"Parameters\":[],\"Assembly\":\"../evil.dll\"}]";
		Assert.That(json, Does.Contain("\"Rules\""), "the settings file has no Rules entry to replace");
		var start = json.IndexOf("\"Rules\"", StringComparison.Ordinal);
		var end = json.IndexOf(']', start) + 1;
		File.WriteAllText(file, json.Substring(0, start) + evil + json.Substring(end));
		try
		{
			Call("project.load", new { path = ProjectFile });
			var report = Call("assetbuild.run", new { copyRaw = false });
			var item = report.GetProperty("items").EnumerateArray().First(i => i.GetProperty("path").GetString()!.EndsWith("Textures/Evil.png", StringComparison.OrdinalIgnoreCase));
			Assert.That(item.GetProperty("outcome").GetString(), Is.EqualTo("failed"));
			Assert.That(item.GetProperty("error").GetString(), Does.Contain("inside the project"));
			Assert.That(report.GetProperty("mgcb").GetString() == null || !File.Exists(report.GetProperty("mgcb").GetString()!) || !File.ReadAllText(report.GetProperty("mgcb").GetString()!).Contains("evil.dll"), Is.True, "the response file references the outside assembly");
		}
		finally
		{
			Call("assetbuild.rule.remove", new { name = "Evil" });
		}
	}

	[Test]
	public void Run_compiles_an_imported_png_and_writes_the_index()
	{
		var png = Path.Combine(Path.GetDirectoryName(EditorExe)!, "DefaultContent", "Fonts", "VoltageDefaultBMFont.png");
		if (!File.Exists(png))
			Assert.Ignore($"sample texture not found at {png}");

		var imported = Call("asset.import", new { source = png, destination = "Textures", name = "Sample.png", overwrite = true });
		Assert.That(imported.GetProperty("path").GetString(), Does.EndWith("Sample.png"));

		JsonElement report;
		using (var slow = SlowConnection())
		{
			try
			{
				report = slow.Call("assetbuild.run", JsonSerializer.SerializeToElement(new { clean = true }));
			}
			catch (CliException ex) when (ex.Message.Contains("tool restore", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("NuGet", StringComparison.OrdinalIgnoreCase))
			{
				Assert.Ignore($"dotnet-mgcb could not be restored here: {ex.Message}");
				return;
			}
		}

		var items = report.GetProperty("items").EnumerateArray().ToList();
		var sample = items.FirstOrDefault(i => i.GetProperty("path").GetString()!.EndsWith("Textures/Sample.png", StringComparison.OrdinalIgnoreCase));
		Assert.That(sample.ValueKind, Is.EqualTo(JsonValueKind.Object), "the imported PNG is missing from the report");
		Assert.That(sample.GetProperty("outcome").GetString(), Is.EqualTo("compiled"), sample.TryGetProperty("error", out var err) ? err.ToString() : "");
		Assert.That(report.GetProperty("success").GetBoolean(), Is.True, string.Join("\n", report.GetProperty("errors").EnumerateArray().Select(e => e.ToString())));

		var index = report.GetProperty("index").GetString();
		Assert.That(File.Exists(index), Is.True, "content.index was not written");
		var line = File.ReadAllLines(index!).FirstOrDefault(l => l.EndsWith("\tTextures/Sample", StringComparison.Ordinal));
		Assert.That(line, Is.Not.Null, "content.index has no entry for the PNG");
		Assert.That(File.Exists(Path.Combine(report.GetProperty("output").GetString()!, "Textures", "Sample.xnb")), Is.True);

		var status = Call("assetbuild.status");
		Assert.That(status.GetProperty("restored").GetBoolean(), Is.True);
		Assert.That(status.GetProperty("indexEntries").GetInt32(), Is.GreaterThanOrEqualTo(1));
	}

	[Test]
	public void Incremental_build_reuses_outputs_in_fresh_destinations_and_rebuilds_changed_sources()
	{
		var png = Path.Combine(Path.GetDirectoryName(EditorExe)!, "DefaultContent", "Fonts", "VoltageDefaultBMFont.png");
		if (!File.Exists(png)) Assert.Ignore($"sample texture not found at {png}");
		Call("asset.import", new { source = png, destination = "Textures", name = "Stable.png", overwrite = true });
		Call("asset.import", new { source = png, destination = "Textures", name = "Changed.png", overwrite = true });
		var project = Path.GetDirectoryName(ProjectFile)!;
		using var slow = SlowConnection();
		JsonElement Run(string output, bool clean = false) => slow.Call("assetbuild.run",
			JsonSerializer.SerializeToElement(new { output = Path.Combine(project, "obj", "IncrementalTests", output), clean, copyRaw = true }));
		var first = Run("First", true);
		Assert.That(first.GetProperty("success").GetBoolean(), Is.True, first.ToString());
		var second = Run("Second");
		Assert.That(second.GetProperty("success").GetBoolean(), Is.True, second.ToString());
		Assert.That(second.GetProperty("upToDate").GetInt32(), Is.EqualTo(second.GetProperty("compiled").GetInt32()));
		var stableOutput = Path.Combine(second.GetProperty("output").GetString()!, "Textures", "Stable.xnb");
		var stableTime = File.GetLastWriteTimeUtc(stableOutput);
		File.SetLastWriteTimeUtc(Path.Combine(project, "Content", "Textures", "Changed.png"), DateTime.UtcNow);
		var third = Run("Second");
		Assert.That(third.GetProperty("success").GetBoolean(), Is.True, third.ToString());
		Assert.That(third.GetProperty("upToDate").GetInt32(), Is.EqualTo(third.GetProperty("compiled").GetInt32() - 1));
		Assert.That(File.GetLastWriteTimeUtc(stableOutput), Is.EqualTo(stableTime));
		Assert.That(File.ReadAllText(third.GetProperty("index").GetString()!), Does.Contain("Textures/Stable"));
		Assert.That(File.Exists(Path.Combine(third.GetProperty("output").GetString()!, "Textures", "Changed.xnb")), Is.True);
		var cache = File.ReadLines(third.GetProperty("mgcb").GetString()!).First(l => l.StartsWith("/outputDir:", StringComparison.Ordinal)).Substring("/outputDir:".Length);
		File.Delete(Path.Combine(cache, "Textures", "Stable.xnb"));
		var missing = Run("Missing");
		Assert.That(missing.GetProperty("success").GetBoolean(), Is.True, missing.ToString());
		Assert.That(missing.GetProperty("upToDate").GetInt32(), Is.EqualTo(missing.GetProperty("compiled").GetInt32() - 1));
		var premultiply = Call("assetbuild.settings").GetProperty("premultiplyAlpha").GetBoolean();
		try
		{
			Call("assetbuild.settings", new { premultiplyAlpha = !premultiply });
			var settingsChanged = Run("SettingsChanged");
			Assert.That(settingsChanged.GetProperty("success").GetBoolean(), Is.True, settingsChanged.ToString());
			Assert.That(settingsChanged.GetProperty("upToDate").GetInt32(), Is.Zero);
		}
		finally
		{
			Call("assetbuild.settings", new { premultiplyAlpha = premultiply });
		}
		File.Delete(Path.Combine(project, "Content", "Textures", "Changed.png"));
		var removed = Run("Removed");
		Assert.That(removed.GetProperty("success").GetBoolean(), Is.True, removed.ToString());
		Assert.That(File.ReadAllText(removed.GetProperty("index").GetString()!), Does.Not.Contain("Textures/Changed"));
		Assert.That(File.Exists(Path.Combine(cache, "Textures", "Changed.xnb")), Is.False);
	}

	/// <summary>The first run downloads dotnet-mgcb, which can take minutes.</summary>
	private static GatewayConnection SlowConnection()
	{
		var info = GatewayInfo.Read(InfoPath);
		return new GatewayConnection(info.Port, info.Token, TimeSpan.FromMinutes(10));
	}
}
