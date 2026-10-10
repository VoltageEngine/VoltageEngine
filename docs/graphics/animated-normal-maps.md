# Animated sprite normal maps

Normal maps give sprite surfaces a directional response to deferred point,
spot and directional lights. They shape highlights and the shaded sides of a
sprite; they do not add cast shadows or geometry. Room fill remains diffuse.

Add `AnimatedNormalMap` next to `SpriteAnimator`. Leave `Source` empty to use
the animator's GUID-backed Aseprite asset, or assign another Aseprite file.
List the normal layer names in `Layers`, then press `Reload` to preview changes.
The component also reloads on scene start and re-enable. Save in Edit mode.

Keep normal layers hidden in Aseprite so they do not enter the albedo. Explicit
normal selections include hidden layers. For each visible art layer, paint a
matching normal layer on the same animation frames and canvas. Preserve tags,
frame order and poses; include neutral normals for visible effects or props
that should stay flat. The normal and diffuse atlases can be packed differently.
Missing animation bindings use neutral normals.

Encode tangent-space XYZ normals in RGB: (128, 128, 255) faces the screen;
red points right and green points up. Keep the RGB vector normalized and use
opaque pixels wherever the matching artwork is visible. Flipping, negative
scale and sprite rotation also transform the normal direction.

`DeferredAnimatedMaterial` accepts either one normal animation or a dictionary
keyed by the animator's animation names. Content-owned Aseprite atlases are
shared by path, layer selection and visibility setting. Identical full frames
reuse atlas regions while keeping frame order, durations, tags and origins.
Consumers must not dispose them. The component owns and disposes only its
material, restoring the
previous material when disabled or removed.

For pixel art, start with restrained volumes for limbs, clothing and faces.
Judge the result with a moving side light and through the whole animation.
Avoid deriving height directly from painted brightness: dark clothing and
baked shadows do not necessarily indicate a recessed surface. The
[Slice Back developers](https://forums.unrealengine.com/t/slice-back-action-2d-pixel-art-with-normal-maps-demo/129904)
describe character normal maps, limited shading tones and Aseprite workflows.
The [SpriteIlluminator animation tutorial](https://www.codeandweb.com/spriteilluminator/tutorials/lighting-demo-cocos2d-x)
explains matched normal atlases and light-direction shading.
