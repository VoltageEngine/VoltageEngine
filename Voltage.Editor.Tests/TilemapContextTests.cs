using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class TilemapContextTests
{
	[Test]
	public void Context_reports_ordered_overlapping_layers_atlas_and_collision()
	{
		var source = Path.Combine(Path.GetTempPath(), $"context-tile-{Guid.NewGuid():N}.png");
		File.WriteAllBytes(source, Convert.FromBase64String(
			"iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAG0lEQVR4nGMI2v31PyWYYdSAUQNGDfg/TAwAAEcHAS40R0jqAAAAAElFTkSuQmCC"));
		Call("asset.import", new { source, name = "ContextTile.png" });
		Call("tileset.create", new { texture = "ContextTile.png", name = "ContextTiles" });
		Call("scene.create", new { name = "ContextScene", template = "empty" });
		Call("tilemap.create", new { tileset = "ContextTiles.vtileset", name = "Rear wall" });
		Call("tilemap.create", new { tileset = "ContextTiles.vtileset", name = "Front detail" });
		Call("tilemap.paint", new { entity = "Rear wall", x = 0, y = 0, tile = 0 });
		Call("tilemap.paint", new { entity = "Front detail", x = 0, y = 0, tile = 0 });
		Call("tilemap.collision", new { entity = "Rear wall", x = 0, y = 0, solid = true });
		Call("component.set", new { entity = "Rear wall", type = "TilemapRenderer", member = "LayerDepth", value = 0.8 });
		Call("component.set", new { entity = "Front detail", type = "TilemapRenderer", member = "LayerDepth", value = 0.2 });

		var context = Call("tilemap.context", new { x = 0, y = 0, width = 16, height = 16, includeViewportImage = false });
		var layers = context.GetProperty("layers").EnumerateArray().ToArray();
		Assert.That(layers.Select(layer => layer.GetProperty("name").GetString()), Is.EqualTo(new[] { "Rear wall", "Front detail" }));
		Assert.That(context.GetProperty("availableCells").GetInt32(), Is.EqualTo(2));
		Assert.That(context.GetProperty("truncatedCells").GetInt32(), Is.Zero);
		Assert.That(layers[0].GetProperty("cells")[0][4].GetBoolean(), Is.True);
		Assert.That(layers[1].GetProperty("cells")[0][4].GetBoolean(), Is.False);
		Assert.That(layers[0].GetProperty("sourceTexture").GetString(), Does.Contain("ContextTile.png"));
		Assert.That(layers[0].GetProperty("sourceTiles")[0].GetProperty("rect")[2].GetInt32(), Is.EqualTo(16));
		Assert.That(layers[0].GetProperty("hierarchy").GetString(), Does.EndWith("Rear wall"));
	}
}
