using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Voltage.Utils;

namespace Voltage.Editor.Utils;

public static class EntityFocusBounds
{
	/// <summary>Fits authored prefab content without including light volumes, runtime helpers or oversized collision envelopes.</summary>
	public static RectangleF PrefabBounds(IEnumerable<Entity> entities)
	{
		var pending = new Stack<Entity>(entities);
		var visited = new HashSet<Entity>();
		var components = new List<Component>();
		RectangleF? visuals = null, collisions = null, positions = null;
		while (pending.Count > 0)
		{
			var entity = pending.Pop();
			if (entity == null || entity.IsDestroyed || !visited.Add(entity)) continue;
			if (!entity.Enabled) continue;
			foreach (var child in entity.Transform.Children) pending.Push(child.Entity);
			if (entity.Type == Entity.InstanceType.NonSerialized) continue;
			if (entity.Transform.ChildCount == 0) Include(ref positions, new RectangleF(entity.Position, Vector2.Zero));
			components.Clear();
			entity.GetComponents(components);
			foreach (var component in components)
			{
				if (!component.Enabled || component is Voltage.DeferredLighting.DeferredLight or StencilLight or Voltage.Shadows.PolyLight)
					continue;
				if (component is TilemapRenderer map && map.TileCount == 0 || component is TiledMapRenderer tiled && tiled.TiledMap == null)
					continue;
				var bounds = component switch { RenderableComponent r => r.Bounds, Collider c => c.Bounds, _ => default };
				if (bounds.Width < 0) { bounds.X += bounds.Width; bounds.Width = -bounds.Width; }
				if (bounds.Height < 0) { bounds.Y += bounds.Height; bounds.Height = -bounds.Height; }
				if (bounds.Width <= 0 || bounds.Height <= 0) continue;
				if (component is RenderableComponent) Include(ref visuals, bounds);
				else Include(ref collisions, bounds);
			}
		}
		return visuals ?? collisions ?? positions ?? new RectangleF(Vector2.Zero, Vector2.Zero);
	}

	/// <summary>Fits both axes with padding and limits magnification of tiny or empty prefabs.</summary>
	public static float FitZoom(RectangleF bounds, Vector2 visibleWorldSize, float currentZoom)
	{
		var width = System.Math.Max(32f, bounds.Width) * 1.2f + 16f;
		var height = System.Math.Max(32f, bounds.Height) * 1.2f + 16f;
		var zoom = currentZoom * System.Math.Min(System.Math.Abs(visibleWorldSize.X) / width, System.Math.Abs(visibleWorldSize.Y) / height);
		return float.IsFinite(zoom) && zoom > 0 ? System.Math.Min(8f, zoom) : 1f;
	}

	/// <summary>Frames a prefab in the current render target and returns the corrected editor camera target.</summary>
	public static Vector2 FramePrefab(Camera camera, IEnumerable<Entity> entities)
	{
		var bounds = PrefabBounds(entities);
		var size = camera.Entity.Scene.SceneRenderTargetSize;
		var visible = camera.ScreenToWorldPoint(size.ToVector2()) - camera.ScreenToWorldPoint(Vector2.Zero);
		camera.RawZoom = FitZoom(bounds, visible, camera.RawZoom);
		camera.Position = CameraPosition(camera, bounds.Center);
		return camera.Position;
	}
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
