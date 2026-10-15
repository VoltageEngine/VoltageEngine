using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Voltage.Editor.Tools.Tilemap;
using Voltage.Editor.Utils;
using Voltage.Gateway;
using Voltage.Sprites;
using static Voltage.Editor.Gateway.GatewayValues;

namespace Voltage.Editor.Gateway.Commands;

/// <summary>A small, ordered cross-section of the scene for inspecting layered tile art without a series of separate queries.</summary>
internal static class TilemapContextCommands
{
	private const int DefaultWidth = 256;
	private const int DefaultHeight = 128;
	private const int MaxWidth = 512;
	private const int MaxHeight = 256;
	private const int MaxCells = 512;
	private const int MaxSprites = 100;

	public static void Register(GatewayCommandTable table)
	{
		table.Add("tilemap.context", "A bounded scene slice with ordered tile layers, cell stacks, source atlas coordinates, collision, nearby sprites and a viewport image in MCP.", (args, _) => Build(args),
			P.Str("entity", "Center on this entity or hierarchy; otherwise use the current camera"),
			P.Float("x", "World-space left edge; with y overrides the center"),
			P.Float("y", "World-space top edge; with x overrides the center"),
			P.Int("width", "World-space width, maximum 512 pixels", DefaultWidth),
			P.Int("height", "World-space height, maximum 256 pixels", DefaultHeight),
			P.Bool("includeCells", "Include sparse [x,y,stack,orientation,solid] records", true),
			P.Bool("includeSprites", "Include overlapping sprite renderers and animators", true),
			P.Bool("includeViewportImage", "MCP attaches the current game view when the region is visible", true)).ReadOnly();
	}

	private static object Build(GatewayArgs args)
	{
		var scene = Core.Scene ?? throw new GatewayException("no scene loaded");
		var width = args.Int("width", DefaultWidth);
		var height = args.Int("height", DefaultHeight);
		if (width <= 0 || width > MaxWidth || height <= 0 || height > MaxHeight)
			throw new GatewayException($"region must be within 1..{MaxWidth} by 1..{MaxHeight} world pixels");
		if (args.Has("x") != args.Has("y"))
			throw new GatewayException("pass both x and y, or neither");

		var viewport = scene.Camera.Bounds;
		var center = viewport.Center;
		if (args.Has("entity"))
			center = EntityFocusBounds.Center(new[] { ResolveEntity(args.Require("entity")) });
		var x = args.Has("x") ? args.Float("x") : center.X - width / 2f;
		var y = args.Has("y") ? args.Float("y") : center.Y - height / 2f;
		if (!float.IsFinite(x) || !float.IsFinite(y))
			throw new GatewayException("region coordinates must be finite");
		var region = new RectangleF(new Vector2(x, y), new Vector2(width, height));
		var maps = TilemapSceneUtils.FindTilemaps()
			.Where(map => map.Entity?.Scene == scene && map.Enabled && map.Entity.Enabled)
			.OrderByDescending(map => map.RenderLayer).ThenByDescending(map => map.LayerDepth).ToList();
		var layers = new List<object>();
		var availableCells = 0;
		var returnedCells = 0;
		foreach (var map in maps)
		{
			var corners = new[]
			{
				map.WorldToTile(new Vector2(x, y)), map.WorldToTile(new Vector2(x + width, y)),
				map.WorldToTile(new Vector2(x, y + height)), map.WorldToTile(new Vector2(x + width, y + height))
			};
			var minX = corners.Min(p => p.X) - 1;
			var maxX = corners.Max(p => p.X) + 1;
			var minY = corners.Min(p => p.Y) - 1;
			var maxY = corners.Max(p => p.Y) + 1;
			var cells = new List<object>();
			var indices = new HashSet<int>();
			var layerCells = 0;
			for (var cy = minY; cy <= maxY; cy++)
				for (var cx = minX; cx <= maxX; cx++)
				{
					var stack = map.GetCellStack(cx, cy);
					var solid = map.GetCollision(cx, cy);
					if (stack.Length == 0 && !solid) continue;
					var tileWorld = map.TileToWorld(cx, cy);
					if (tileWorld.X + map.TileWidth < x || tileWorld.X > x + width ||
					    tileWorld.Y + map.TileHeight < y || tileWorld.Y > y + height) continue;
					layerCells++;
					if (!args.Bool("includeCells", true) || returnedCells >= MaxCells) continue;
					cells.Add(new object[] { cx, cy, stack, map.GetOrientation(cx, cy), solid });
					foreach (var index in stack) indices.Add(index);
					returnedCells++;
				}
			if (layerCells == 0) continue;
			availableCells += layerCells;
			var atlas = map.ResolvedTileset;
			var source = atlas?.Asset;
			var sourceTiles = indices.OrderBy(index => index).Select(index =>
			{
				if (atlas == null || index < 0 || index >= atlas.TileCount) return (object)new { index, invalid = true };
				var rect = atlas.SourceRects[index];
				return new { index, rect = new[] { rect.X, rect.Y, rect.Width, rect.Height },
					blank = atlas.IsBlank(index), opaque = atlas.IsTileOpaque[index] };
			}).ToList();
			var origin = map.TileToWorld(0, 0);
			layers.Add(new
			{
				name = map.Entity.Name, guid = map.Entity.PersistentId, hierarchy = Hierarchy(map.Entity),
				drawOrder = layers.Count, renderLayer = map.RenderLayer, layerDepth = map.LayerDepth,
				tileSize = new[] { map.TileWidth, map.TileHeight },
				worldOrigin = new[] { origin.X, origin.Y },
				rotationDegrees = MathHelper.ToDegrees(map.Entity.Transform.Rotation),
				scale = new[] { map.Entity.Transform.Scale.X, map.Entity.Transform.Scale.Y },
				tileset = map.Tileset.AssetPath,
				sourceTexture = source?.Texture.AssetPath,
				sourceLayers = source?.TextureLayers,
				unusedSourceLayers = atlas?.UnusedSourceLayers,
				atlasColumns = atlas?.Columns,
				cellCount = layerCells, cells, sourceTiles
			});
		}

		var sprites = new List<object>();
		var spriteCount = 0;
		if (args.Bool("includeSprites", true))
			foreach (var entity in scene.Entities)
			{
				if (!entity.Enabled) continue;
				foreach (var sprite in entity.GetComponents<SpriteRenderer>())
				{
					if (!sprite.Enabled) continue;
					var bounds = sprite.Bounds;
					if (!Intersects(bounds, region) && !region.Contains(entity.Transform.Position)) continue;
					spriteCount++;
					if (sprites.Count >= MaxSprites) continue;
					var animator = sprite as SpriteAnimator;
					sprites.Add(new
					{
						name = entity.Name, guid = entity.PersistentId, hierarchy = Hierarchy(entity),
						type = sprite.GetType().Name, renderLayer = sprite.RenderLayer, layerDepth = sprite.LayerDepth,
						bounds = new[] { bounds.X, bounds.Y, bounds.Width, bounds.Height },
						texture = animator?.TextureFilePath ?? sprite.Sprite?.Texture2D?.Name,
						asepriteLayers = animator?.LoadedLayers, animation = animator?.CurrentAnimationName,
						frame = animator?.CurrentFrame
					});
				}
			}

		return new
		{
			scene = scene.Name,
			region = new { x, y, width, height },
			viewport = new { x = viewport.X, y = viewport.Y, width = viewport.Width, height = viewport.Height },
			viewportImageAvailable = Intersects(region, viewport),
			order = "Layers and sprites draw back to front: higher renderLayer, then higher layerDepth first",
			cellFormat = "[tileColumn,tileRow,bottomToTopTileIndices,orientation,solid]",
			availableCells, returnedCells, truncatedCells = Math.Max(0, availableCells - returnedCells),
			layers, spriteCount, truncatedSprites = Math.Max(0, spriteCount - sprites.Count), sprites
		};
	}

	private static string Hierarchy(Entity entity)
	{
		var names = new List<string>();
		for (var current = entity.Transform; current != null && names.Count < 32; current = current.Parent)
			names.Add(current.Entity.Name);
		names.Reverse();
		return string.Join("/", names);
	}

	private static bool Intersects(RectangleF a, RectangleF b) =>
		a.Right > b.Left && a.Left < b.Right && a.Bottom > b.Top && a.Top < b.Bottom;
}
