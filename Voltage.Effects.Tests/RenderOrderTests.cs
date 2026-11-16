using System;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using Voltage.Editor.ProjectFile;
using Voltage.Project;
using Voltage.Sprites;

namespace Voltage.Effects.Tests;

[TestFixture]
public class RenderOrderTests
{
	[Test]
	public void A_higher_render_layer_draws_after_a_lower_one()
	{
		var back = new SpriteRenderer { RenderLayer = 0 };
		var front = new SpriteRenderer { RenderLayer = 5 };
		Assert.That(new RenderableComparer().Compare(back, front), Is.LessThan(0));
		Assert.That(back.CompareTo(front), Is.LessThan(0));
	}

	[Test]
	public void Stored_layers_are_negated_in_plain_and_embedded_json_only()
	{
		var text = "{ \"RenderLayer\": 5, \"Json\": \"{\\n\\t\\\"RenderLayer\\\": -2,\\n\\t\\\"LayerDepth\\\": 0.5 }\", " +
		           "\"SortRenderLayer\": 3, \"Property\": \"RenderLayer\", \"PhysicsLayer\": 4 }";
		var flipped = RenderOrderMigration.FlipStoredLayers(text, out var count);
		Assert.That(count, Is.EqualTo(2));
		Assert.That(flipped, Is.EqualTo(text.Replace("\"RenderLayer\": 5", "\"RenderLayer\": -5").Replace("\\\"RenderLayer\\\": -2", "\\\"RenderLayer\\\": 2")));
	}

	[Test]
	public void An_old_project_is_migrated_once_with_a_backup()
	{
		var root = Path.Combine(Path.GetTempPath(), "voltage-render-order-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(root, "Scenes"));
		Directory.CreateDirectory(Path.Combine(root, "bin"));
		try
		{
			File.WriteAllText(Path.Combine(root, "ProjectSettings.json"), "{ \"Rendering\": { \"RenderingLayers\": { \"Background\": 0, \"Entities\": -1, \"Lighting\": 100 } } }");
			var scene = Path.Combine(root, "Scenes", "Room.vscene");
			File.WriteAllText(scene, "{ \"Json\": \"{\\\"RenderLayer\\\": -1}\" }");
			var buildCopy = Path.Combine(root, "bin", "Room.vscene");
			File.WriteAllText(buildCopy, "{ \"RenderLayer\": -1 }");
			var settings = new ProjectSettings();
			settings.Rendering.RenderingLayers = new() { { "Background", 0 }, { "Entities", -1 }, { "Lighting", 100 } };

			Assert.That(RenderOrderMigration.Run(root, settings), Is.True);
			Assert.That(File.ReadAllText(scene), Does.Contain("\\\"RenderLayer\\\": 1"));
			Assert.That(File.ReadAllText(buildCopy), Does.Contain("\"RenderLayer\": -1"));
			Assert.That(settings.Rendering.RenderingLayers["Entities"], Is.EqualTo(1));
			Assert.That(settings.Rendering.RenderingLayers["Lighting"], Is.EqualTo(-100));
			Assert.That(File.ReadAllText(Path.Combine(root, "ProjectSettings.json")), Does.Contain("\"RenderOrderVersion\""));
			var backups = Directory.GetFiles(Path.Combine(root, "Backups"), "*.zip");
			Assert.That(backups, Has.Length.EqualTo(1));
			using (var zip = ZipFile.OpenRead(backups[0]))
				Assert.That(zip.GetEntry("Scenes/Room.vscene"), Is.Not.Null);

			Assert.That(RenderOrderMigration.Run(root, settings), Is.False);
			Assert.That(File.ReadAllText(scene), Does.Contain("\\\"RenderLayer\\\": 1"));
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}
}
