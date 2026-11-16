using NUnit.Framework;

namespace Voltage.Effects.Tests;

[TestFixture, NonParallelizable]
public class GraphicsBackendSelectorTests
{
	string _root, _previousPreference;

	[SetUp]
	public void SetUp()
	{
		_root = Path.Combine(Path.GetTempPath(), "voltage-backend-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_root);
		_previousPreference = GraphicsBackendSelector.PreferenceFile;
		GraphicsBackendSelector.PreferenceFile = Path.Combine(_root, "user", "GraphicsBackend.txt");
		Environment.SetEnvironmentVariable(GraphicsBackendSelector.EnvironmentVariable, null);
	}

	[TearDown]
	public void TearDown()
	{
		GraphicsBackendSelector.PreferenceFile = _previousPreference;
		Directory.Delete(_root, true);
	}

	[Test]
	public void A_request_comes_from_the_last_argument_then_the_environment_and_unknown_names_mean_none()
	{
		Assert.That(GraphicsBackendSelector.Requested(new[] { "game.voltage", "--graphics-backend", "vulkan" }, "OpenGL4"), Is.EqualTo(GraphicsApi.Vulkan));
		Assert.That(GraphicsBackendSelector.Requested(new[] { "--graphics-backend=directx12" }, null), Is.EqualTo(GraphicsApi.DirectX12));
		Assert.That(GraphicsBackendSelector.Requested(Array.Empty<string>(), "opengl 4"), Is.EqualTo(GraphicsApi.OpenGL4));
		Assert.That(GraphicsBackendSelector.Requested(new[] { "--graphics-backend" }, null), Is.Null);
		Assert.That(GraphicsBackendSelector.Requested(null, "Metal"), Is.Null);
		Assert.That(GraphicsBackendSelector.Parse("DirectX 12"), Is.EqualTo(GraphicsApi.DirectX12));
		Assert.That(GraphicsBackendSelector.Parse("3"), Is.Null);

		Assert.That(GraphicsBackendSelector.Order(null), Is.EqualTo(GraphicsBackendSelector.AutoOrder));
		Assert.That(GraphicsBackendSelector.Order(GraphicsApi.OpenGL4).First(), Is.EqualTo(GraphicsApi.OpenGL4));
		Assert.That(GraphicsBackendSelector.Order(GraphicsApi.OpenGL4), Is.Unique);
		Assert.That(GraphicsBackendSelector.Order(GraphicsApi.OpenGL4), Is.EquivalentTo(GraphicsBackendSelector.AutoOrder));
	}

	[Test]
	public void The_player_choice_round_trips_and_anything_unreadable_means_automatic()
	{
		Assert.That(GraphicsBackendSelector.Preference, Is.EqualTo(GraphicsApi.Auto));
		GraphicsBackendSelector.Preference = GraphicsApi.Vulkan;
		Assert.That(GraphicsBackendSelector.Preference, Is.EqualTo(GraphicsApi.Vulkan));
		File.WriteAllText(GraphicsBackendSelector.PreferenceFile, "Metal");
		Assert.That(GraphicsBackendSelector.Preference, Is.EqualTo(GraphicsApi.Auto));
	}

	[Test]
	public void The_OpenGL_build_offers_only_automatic_and_compatibility_and_says_why()
	{
		Assert.That(GraphicsBackendSelector.NativeBuild, Is.False);
		Assert.That(GraphicsBackendSelector.Running, Is.EqualTo(GraphicsApi.OpenGL));
		Assert.That(GraphicsBackendSelector.Unavailable(GraphicsApi.Auto), Is.Null);
		Assert.That(GraphicsBackendSelector.Unavailable(GraphicsApi.OpenGL), Is.Null);
		foreach (var native in new[] { GraphicsApi.Vulkan, GraphicsApi.OpenGL4 })
		{
			Assert.That(GraphicsBackendSelector.Unavailable(native), Does.StartWith("Not included in this build"));
			Assert.That(GraphicsBackendSelector.Unavailable(native, gameBuild: true), Does.StartWith("Build Game"));
		}
		Assert.That(GraphicsBackendSelector.Unavailable(GraphicsApi.DirectX12),
			Is.EqualTo(OperatingSystem.IsWindows() ? GraphicsBackendSelector.Unavailable(GraphicsApi.Vulkan) : "DirectX 12 needs Windows."));
	}

	[Test]
	public void A_backend_left_pending_is_skipped_even_when_preferred_and_unloadable_runtimes_fall_through()
	{
		var root = Path.Combine(_root, "backends");
		foreach (var backend in new[] { "directx12", "vulkan", "opengl4" })
		{
			Directory.CreateDirectory(Path.Combine(root, backend));
			foreach (var library in new[] { "mgruntime.dll", "libmgruntime.so", "libmgruntime.dylib" })
				File.WriteAllText(Path.Combine(root, backend, library), "not a library");
		}
		File.WriteAllText(Path.Combine(root, "pending.txt"), "Vulkan");
		GraphicsBackendSelector.Preference = GraphicsApi.Vulkan;

		var failure = Assert.Throws<PlatformNotSupportedException>(() => GraphicsBackendSelector.Select(Array.Empty<string>(), root));

		Assert.That(failure.Message, Does.Contain("Vulkan: crashed on an earlier start"));
		Assert.That(failure.Message, Does.Contain("OpenGL4: its runtime library failed to load"));
		Assert.That(File.ReadAllLines(Path.Combine(root, "failed.txt")), Is.EqualTo(new[] { "Vulkan" }));
		Assert.That(GraphicsBackendSelector.Selected, Is.Null);
	}
}
