using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Voltage.Utils;

namespace Voltage.Editor.Utils;

public static class EntityFocusBounds
{
	/// <summary>Positions the camera so the given world point lands at the render target's center.</summary>
	public static Vector2 CameraPosition(Camera camera, Vector2 center)
	{
		var size = camera.Entity.Scene.SceneRenderTargetSize;
		var viewportCenter = camera.ScreenToWorldPoint(new Vector2(size.X * .5f, size.Y * .5f));
		return center - (viewportCenter - camera.Position);
	}

	/// <summary>Centers on authored content across the selected hierarchies, falling back to leaf positions.</summary>
	public static Vector2 Center(IEnumerable<Entity> entities)
	{
		var pending = new Stack<Entity>(entities);
		var visited = new HashSet<Entity>();
		var components = new List<Component>();
		RectangleF? content = null, fallback = null;
		while (pending.Count > 0)
		{
			var entity = pending.Pop();
			if (entity == null || entity.IsDestroyed || !visited.Add(entity))
				continue;
			foreach (var child in entity.Transform.Children)
				pending.Push(child.Entity);
			if (entity.Transform.ChildCount == 0)
				Include(ref fallback, new RectangleF(entity.Position, Vector2.Zero));

			components.Clear();
			entity.GetComponents(components);
			foreach (var component in components)
			{
				// Empty layers have a pickable box at the origin, but no painted content to frame.
				if (component is TilemapRenderer map && map.TileCount == 0 ||
					component is TiledMapRenderer tiled && tiled.TiledMap == null)
					continue;
				var bounds = component switch
				{
					RenderableComponent renderable => renderable.Bounds,
					Collider collider => collider.Bounds,
					_ => default
				};
				if (bounds.Width < 0) { bounds.X += bounds.Width; bounds.Width = -bounds.Width; }
				if (bounds.Height < 0) { bounds.Y += bounds.Height; bounds.Height = -bounds.Height; }
				if (bounds.Width > 0 && bounds.Height > 0)
					Include(ref content, bounds);
			}
		}
		return (content ?? fallback)?.Center ?? Vector2.Zero;
	}

	private static void Include(ref RectangleF? combined, RectangleF bounds)
	{
		if (MathUtils.IsVectorNaNOrInfinite(bounds.Location) ||
			MathUtils.IsVectorNaNOrInfinite(new Vector2(bounds.Right, bounds.Bottom)))
			return;
		combined = combined.HasValue ? RectangleF.Union(combined.Value, bounds) : bounds;
	}
}
