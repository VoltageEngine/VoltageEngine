---
title: Room Light
description: Bounded, art-directed indirect fill for a room polygon, with an optional driver lamp, two-tone gradient, edge bounce and collider shadows.
---

# Room light

`Voltage.DeferredLighting.RoomLight` fills a polygon with soft indirect light, the glow a lamp spreads through a room. It is added to the deferred light buffer like any other light and does not change point or spot lights.

## Requirements

- Turn on **Deferred lighting** in **Project > Settings > Rendering Layers**, then reload the scene. With it on, every scene that does not set up its own renderers gets a `DeferredLightingRenderer`, which draws the lights on the **Lighting** render layer.
- Set the component's `RenderLayer` to **Lighting**. Only lights belong on that layer.

## Shape

The default polygon is a 320 by 160 rectangle centred on the entity. `Vertices` holds 3 to 128 points in local coordinates, in either winding. Concave outlines work; holes and self-intersecting outlines do not. Entity position, rotation and scale, plus `LocalOffset`, place the polygon in the world. A shape with no area, or wider or taller than 16384 local pixels, emits nothing.

To edit it in the game view, select the entity in Edit mode and switch to **Resize Bounds** (4 or T). Drag a vertex handle to move that point, or a bounds handle to scale the whole outline. Both edits are undoable. Debug rendering outlines the polygon in yellow.

## Settings

| Setting | Default | Meaning |
| --- | --- | --- |
| `Color` | white | Primary fill tint |
| `Intensity` | 0.35 | Overall brightness, 0 to 8 |
| `SecondaryColor` | 255, 207, 158 | Second fill hue for a two-tone room |
| `SecondaryColorStrength` | 0 | How far the far side blends to `SecondaryColor`, 0 to 1 |
| `ColorBlendDirection` | (1, 0) | Local gradient direction: (1, 0) left to right, (0, 1) top to bottom; negate to reverse |
| `AmbientFill` | 0.3 | Minimum fill away from the virtual source, 0 to 1 |
| `LightOrigin` | (0, -60) | Local position of the virtual source |
| `Range` / `Falloff` | 240 / 2 | Reach of the virtual source and the exponent of its fade |
| `Scattering` | 0.7 | How evenly the source light spreads through the room, 0 to 1 |
| `EdgeSoftness` | 12 | Inward fade at the polygon edge, in local pixels |
| `BounceColor` / `BounceStrength` | 255, 207, 158 / 0.25 | Extra tint near the walls and its strength |
| `SourceLight` | none | Point or spot light that drives the fill |
| `SourceReferenceIntensity` | 1 | Driver intensity that counts as normal brightness |
| `SourceInfluence` | 1 | Blend between a fixed fill (0) and following the driver (1) |
| `UseSourceColor` | off | Multiply the fill by the driver's colour |
| `ShadowStrength` / `ShadowLayerMask` | 0 / all | Shadows from solid colliders inside the room; 0 turns them off |

The fill reads no normal maps: it is flat, diffuse light. Overlapping room lights add together.

## Driving it from a lamp

Assign the room's lamp to `SourceLight` and set `SourceReferenceIntensity` to the lamp's usual intensity. The fill then scales with the lamp's current intensity divided by that reference (up to 8 times), so flicker, dimming and switching off carry into the room. A disabled lamp, or one in another scene, counts as off. `LightOrigin` stays where you placed it, even if the lamp swings.

For a dim warm room, start with `Intensity` 0.15 to 0.35, `AmbientFill` 0.2, `Scattering` 0.6, `EdgeSoftness` 12 and `BounceStrength` 0.15.

For a two-tone room, set `Color`, set `SecondaryColor`, and raise `SecondaryColorStrength` toward 1. The gradient spans the polygon's local bounds and rotates with the entity.

## Shadows

With `ShadowStrength` above 0, each point of the room is dimmed when a solid collider on `ShadowLayerMask` lies between it and `LightOrigin`. Triggers are ignored, as is a collider that contains the origin. Point the mask at static walls and furniture: moving those colliders, or moving the room light itself, rebuilds the cached field.

## Performance

The fill is baked into a cached texture of up to 256 by 256 texels (about one texel per 2 local pixels on smaller rooms) and drawn as one quad. Changing the shape, colours or profile rebuilds it. `Intensity`, the driver and, with shadows off, the transform do not. Filtering softens the edge by about a texel, and features thinner than a texel can disappear, so split very large rooms into several lights.
