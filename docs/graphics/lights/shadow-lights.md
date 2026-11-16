---
title: 2D Shadow Lights
sidebar_position: 9
description: Lights that cast hard shadows from colliders into a light map, and sprite-based light maps, as an alternative to normal-mapped lighting.
---

# 2D shadow lights

The deferred lights shade sprites through their normal maps but cast no shadows. Voltage also has a separate family of 2D lights that do the opposite: they cast hard shadows from colliders and produce a **light map**, a texture of lit and dark areas that is multiplied over the scene. Use them for top-down visibility, stealth games and stylised silhouette lighting. They don't need deferred lighting or normal maps.

All three approaches follow the same pattern: a renderer draws the lights into a `RenderTexture` whose clear colour is the ambient darkness, then a post-processor or sprite combines that texture with the scene.

Put the lights on a render layer of their own, and keep that layer out of the scene's main renderer. The default renderer that a scene creates draws every layer except UIElement, so a scene using these lights replaces it, for example with `scene.RemoveRenderer(scene.GetRenderer<RenderLayerExcludeRenderer>())` followed by `scene.AddRenderer(new RenderLayerExcludeRenderer(0, uiLayer, lightLayer))`.

## Stencil lights

`StencilLight` with `StencilLightRenderer` casts sharp shadows using the stencil buffer. For each light, the renderer projects the shadows of nearby colliders, then draws the light's falloff circle everywhere the shadows are not.

```csharp
int lightLayer = RenderLayer.GetRenderLayer("Lights2D");           // a layer you add in Project Settings
var lightMap = new RenderTexture();
var lights = Core.Scene.AddRenderer(new StencilLightRenderer(-1, lightLayer, lightMap)
{
    RenderTargetClearColor = new Color(20, 20, 30),               // ambient darkness
    CollidesWithLayers = PhysicsLayer.GetPhysicsLayerMask("Ground"),
});
Core.Scene.AddPostProcessor(new SpriteLightPostProcessor(0, lightMap));

var lamp = Entity.AddComponent(new StencilLight(radius: 200, Color.Orange, power: 1f));
lamp.SetRenderLayer(lightLayer);
```

`StencilLight` settings: `Radius`, `Power` (brightness, 0 to 1) and `Color`. The renderer's `CollidesWithLayers` chooses which colliders cast shadows; give it a render order before your other renderers so the light map is ready.

## Polygon lights

`PolyLight` and `PolySpotLight` (namespace `Voltage.Shadows`) compute the area each light can see around nearby colliders and draw it as a polygon. Render them into a light map with a `RenderLayerRenderer` and combine it with `PolyLightPostProcessor`:

```csharp
var lightMap = new RenderTexture();
Core.Scene.AddRenderer(new RenderLayerRenderer(-1, lightLayer)
{
    RenderTexture = lightMap,
    RenderTargetClearColor = new Color(15, 15, 25),
});
var combine = Core.Scene.AddPostProcessor(new PolyLightPostProcessor(0, lightMap));
combine.EnableBlur = true;            // soften the shadow edges
combine.BlurAmount = 2f;

Entity.AddComponent(new PolyLight(radius: 240, Color.LightYellow, power: 1f)).SetRenderLayer(lightLayer);
```

`PolyLight` settings: `Radius`, `Power` (brightness), `Color` and `CollidesWithLayers`. `PolySpotLight` adds `SpotAngle` and aims along the entity's rotation. The post-processor's `MultiplicativeFactor` brightens or darkens the combined result, and `BlurRenderTargetScale` trades blur quality for speed.

## Sprite light maps

The simplest option needs no colliders: draw soft light sprites (circles, cones, gradients) on a light layer with a `RenderLayerRenderer` into a render texture, then multiply it over the scene with `SpriteLightPostProcessor`, as in the stencil example. Every light is just a sprite, so it can be animated, tinted and shaped freely, and the cost is one extra layer draw.

## Choosing

| Need | Use |
| --- | --- |
| Pixel art that reacts to light direction, highlights, glow | Deferred lights with normal maps ([overview](../lighting.md)) |
| Hard shadows from walls, line of sight | Stencil or polygon lights |
| Soft, art-directed light pools at minimal cost | Sprite light maps |

Light maps darken everything they cover, including UI on world layers; keep menus and HUD on the UIElement layer, which draws after post-processing.
