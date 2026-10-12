# Projected light

`Voltage.DeferredLighting.ProjectedLight` illuminates the deferred light buffer
through the transparent pixels of a board tilemap. It never draws lines into
the scene's color buffer. The light shades receiving surfaces using their normal
maps. Existing point, spot, sprite and room lights keep their behavior.

Add the component on the scene's Lighting render layer. Assign `Boards`, then
author `Windows` as rectangles in that tilemap's local pixel coordinates. Opaque
pixels block light; partially transparent pixels attenuate it. Stacked tiles,
cell orientation and the board entity's transform are respected. `RoomArea` is a
world-space receiving rectangle that prevents illumination crossing into another
room. Solid colliders on `OcclusionLayerMask` also block the projection.

| Setting | Purpose |
| --- | --- |
| Direction | Direction away from the window in board-local coordinates |
| ProjectionDistance | Separation between the mask and the receiving surface |
| Magnification | Expansion of the projected openings with distance |
| Softness | Width of the penumbra at the mask, in pixels |
| Color / Intensity | Light hue and strength |
| NormalZ | Height of the light direction above the 2D receiving surface |

The board reference and all controls are serialized and available in the
inspector. The mask and light texture are generated at runtime, cached, and
released with the scene. Moving or painting the boards and changing static
occluders invalidate the field; color and intensity edits do not rebuild it.

This is a projected 2D surface light with a virtual depth separation, rather than
a 3D geometry or volumetric simulation. Receiver clipping is rectangular; very
large receiving areas are limited to 512 by 512 field samples. Prefer separate
lights for different rooms. Set the occlusion mask to static room geometry to
avoid rebuilding the field every time an actor moves.
