using NUnit.Framework;

namespace Voltage.Effects.Tests;

[TestFixture]
public class GraphicsBackendSelectorTests
{
	[Test]
	public void A_request_comes_from_the_last_argument_then_the_environment_and_unknown_names_mean_automatic()
	{
		Assert.That(GraphicsBackendSelector.Requested(new[] { "game.voltage", "--graphics-backend", "vulkan" }, "OpenGL4"), Is.EqualTo("Vulkan"));
		Assert.That(GraphicsBackendSelector.Requested(new[] { "--graphics-backend=directx12" }, null), Is.EqualTo("DirectX12"));
		Assert.That(GraphicsBackendSelector.Requested(Array.Empty<string>(), "opengl4"), Is.EqualTo("OpenGL4"));
		Assert.That(GraphicsBackendSelector.Requested(new[] { "--graphics-backend" }, null), Is.Null);
		Assert.That(GraphicsBackendSelector.Requested(null, "Metal"), Is.Null);

		Assert.That(GraphicsBackendSelector.Order(null), Is.EqualTo(GraphicsBackendSelector.AutoOrder));
		Assert.That(GraphicsBackendSelector.Order("OpenGL4").First(), Is.EqualTo("OpenGL4"));
		Assert.That(GraphicsBackendSelector.Order("OpenGL4"), Is.Unique);
		Assert.That(GraphicsBackendSelector.Order("OpenGL4"), Is.EquivalentTo(GraphicsBackendSelector.AutoOrder));
	}

	[Test]
	public void A_backend_left_pending_is_remembered_as_crashed_and_unloadable_runtimes_fall_through()
	{
		var root = Path.Combine(Path.GetTempPath(), "voltage-backend-tests", Guid.NewGuid().ToString("N"));
		try
		{
			foreach (var backend in new[] { "directx12", "vulkan", "opengl4" })
			{
				Directory.CreateDirectory(Path.Combine(root, backend));
				foreach (var library in new[] { "mgruntime.dll", "libmgruntime.so", "libmgruntime.dylib" })
					File.WriteAllText(Path.Combine(root, backend, library), "not a library");
			}
			File.WriteAllText(Path.Combine(root, "pending.txt"), "Vulkan");
			Environment.SetEnvironmentVariable(GraphicsBackendSelector.EnvironmentVariable, null);

			var failure = Assert.Throws<PlatformNotSupportedException>(() => GraphicsBackendSelector.Select(Array.Empty<string>(), root));

			Assert.That(failure.Message, Does.Contain("Vulkan: crashed on an earlier start"));
			Assert.That(failure.Message, Does.Contain("OpenGL4: its runtime library failed to load"));
			Assert.That(File.ReadAllLines(Path.Combine(root, "failed.txt")), Is.EqualTo(new[] { "Vulkan" }));
			Assert.That(GraphicsBackendSelector.Selected, Is.Null);
		}
		finally { Directory.Delete(root, true); }
	}
}
