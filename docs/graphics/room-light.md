# Room light

`Voltage.DeferredLighting.RoomLight` adds bounded, art-directed indirect illumination
to the existing deferred light buffer. Add it to an entity on the scene's lighting
render layer. It does not replace point/spot lights or change their parameters.

The default polygon is a 320 by 160 rectangle centered on the entity. Edit
`Vertices` in the inspector using local coordinates around its perimeter. Simple
concave polygons and either winding work; holes and self-intersections are not
supported. Entity position, rotation, scale and LocalOffset place the volume.
Debug rendering outlines the polygon. Invalid/degenerate shapes emit nothing.

| Control | Meaning |
| --- | --- |
| Color / Intensity | Fill tint and overall brightness |
| AmbientFill | Minimum illumination away from the virtual source |
| LightOrigin / Range / Falloff | Local virtual source and its attenuation profile |
| Scattering | Spreads the source illumination throughout the room (0–1) |
| EdgeSoftness | Inward boundary fade in local pixels |
| BounceColor / BounceStrength | Additional wall-edge tint and strength |
| SourceLight | Optional point/spot light driving the room fill |
| SourceReferenceIntensity | Driver intensity corresponding to normal brightness |
| SourceInfluence | Blend between independent fill and driver response (0–1) |
| UseSourceColor | Multiply the room fill by the driver's color |

For a dim warm room, start at Intensity 0.15–0.35, AmbientFill 0.2, Scattering
0.6, EdgeSoftness 12 and BounceStrength 0.15. Link its lamp through SourceLight
and set SourceReferenceIntensity to that lamp's normal intensity: flicker and
switch-off then affect the room without rebuilding its cached field. The virtual
LightOrigin stays artist-controlled, independent of a swinging lamp's position.

This is diffuse fill, not ray-traced global illumination or volumetric fog. It
does not calculate occlusion from furniture, modify contained lights, or simulate
physical multiple scattering. Overlapping room lights add together. Existing
ambient regions and normal-mapped direct lights still work unchanged. A cached
field uses up to 258 by 258 texels including its black border; filtering can soften
the polygon boundary by roughly a texel. Shape/profile edits rebuild the field;
intensity, driver and transform changes do not. Thin features below a texel may
vanish; divide very large rooms into smaller lights when necessary.
