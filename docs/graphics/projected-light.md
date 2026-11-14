---
title: Projected Light
description: Light cast through the transparent pixels of a tilemap, such as sunlight through boarded windows, shaded by the receivers' normal maps.
---

# Projected light

`Voltage.DeferredLighting.ProjectedLight` casts light through the transparent pixels of a tilemap, for example sun falling through gaps in boarded windows, and lands it on a receiving area of the room. The result is added to the deferred light buffer and shaded with the receivers' normal maps.

## Requirements

- Turn on **Deferred lighting** in **Project > Settings > Rendering Layers**, then reload the scene. With it on, every scene that does not set up its own renderers gets a `DeferredLightingRenderer`, which draws the lights on the **Lighting** render layer.
- Set the component's `RenderLayer` to **Lighting**. Only lights belong on that layer.

## Setup

1. Assign `Boards`: the `TilemapRenderer` whose art acts as the mask.
2. Add rectangles to `Windows`, in the board tilemap's local pixel coordinates, around each opening. Each rectangle can be up to 512 by 512 pixels.
3. Set `RoomArea`, the world-space rectangle that receives the light. Light never leaves it, which keeps it from crossing into the next room.

`RoomArea` is in world space, so the light's own entity position does not move it. To edit it in the game view, select the entity in Edit mode, switch to **Resize Bounds** (4 or T) and drag its handles. Debug rendering outlines the receiving area.

Inside each window, opaque pixels block light and partly transparent pixels let some through. Stacked tiles, per-cell rotation and flips, animated tiles, the board entity's transform and the tilemap's colour alpha are all respected. Solid colliders on `OcclusionLayerMask` between the opening and a receiving point block it too; triggers are ignored.

Nothing is emitted while `Boards` is unassigned or disabled, `RoomArea` is empty, or `Direction` is zero.

## Settings

| Setting | Default | Purpose |
| --- | --- | --- |
| `Direction` | (0.6, 1) | Direction the light travels, in board-local coordinates |
| `ProjectionDistance` | 44 | Gap between the mask and the receiving surface, 0 to 512 |
| `Magnification` | 1.5 | How much the openings grow with distance, 1 to 4 |
| `Softness` | 2 | Penumbra width at the mask, in pixels, 0 to 16 |
| `Color` / `Intensity` | white / 0.65 | Hue and strength; intensity 0 to 8 |
| `NormalZ` | 1.5 | Height of the light above the surface for normal shading, 0.1 to 8 |
| `OcclusionLayerMask` | all | Physics layers whose solid colliders block the projection |

## Performance

The projection is baked into a cached field with one sample per world pixel of `RoomArea`, capped at 512 by 512, and drawn as a single quad. Moving or repainting the boards, an animated tile changing frame, editing the windows or projection settings, or any change to colliders on `OcclusionLayerMask` inside the room rebuilds it. `Color`, `Intensity` and `NormalZ` do not. Point `OcclusionLayerMask` at static room geometry so moving actors do not rebuild the field every frame. The field is released when the component is removed.

This is a 2D surface projection with a virtual depth gap, not 3D geometry or volumetric light. The receiving area is a rectangle; use separate lights for separate rooms.
