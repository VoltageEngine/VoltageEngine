using System;
using Voltage.DeferredLighting;
using Voltage.Shadows;
using Voltage.Sprites;

namespace Voltage;

[Flags]
public enum DebugRenderCategory
{
	None = 0,
	Colliders = 1,
	Sprites = 2,
	Tilemaps = 4,
	Lights = 8,
	UI = 16,
	Other = 32,
	Gizmos = 64,
	All = Colliders | Sprites | Tilemaps | Lights | UI | Other | Gizmos
}

/// <summary>Global overlay filters independent of saved component and entity settings.</summary>
public static class DebugRenderFilter
{
	public static DebugRenderCategory EnabledCategories { get; set; } = DebugRenderCategory.All;
	public static bool IsEnabled(DebugRenderCategory category) => (EnabledCategories & category) != 0;
	public static DebugRenderCategory CategoryOf(object component) => component switch
	{
		Collider => DebugRenderCategory.Colliders,
		DeferredLight or PolyLight or StencilLight => DebugRenderCategory.Lights,
		SpriteRenderer => DebugRenderCategory.Sprites,
		TilemapRenderer or TiledMapRenderer => DebugRenderCategory.Tilemaps,
		UICanvas => DebugRenderCategory.UI,
		_ => DebugRenderCategory.Other
	};

	public static bool ShouldRender(object component)
	{
		if (component == null || (component as Component)?.Entity?.DebugRenderEnabledInHierarchy == false) return false;
		var categories = CategoryOf(component);
		// Tilemap-generated colliders have no component-list entry of their own.
		if (component is TilemapRenderer or TiledMapRenderer) categories |= DebugRenderCategory.Colliders;
		return IsEnabled(categories);
	}
}
