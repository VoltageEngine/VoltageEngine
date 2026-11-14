---
title: Sprites and Animation
sidebar_position: 1
description: Sprite renderers, Aseprite-driven animation, animation events, sprite atlases, text, particles and other renderable components.
---

# Sprites and Animation

Anything visible is a `RenderableComponent`. Every renderable has `Color`, `RenderLayer`, `LayerDepth`, `LocalOffset`, `Material` and `IsVisible`, and is culled when its `Bounds` leave the camera.

## Sprite renderer

`SpriteRenderer` (namespace `Voltage.Sprites`) draws one `Sprite`: a texture region with an origin. In the editor, drop a `.png` or pick a file in its Inspector. From code:

```csharp
var sr = Entity.AddComponent(new SpriteRenderer());
sr.LoadPngFile("Content/Props/Crate.png");
sr.LoadAsepriteFile("Content/Props/Crate.aseprite", layerName: "Body", frameNumber: 0);
sr.SetOriginNormalized(new Vector2(0.5f, 1f));   // bottom centre
sr.FlipX = true;
sr.Color = Color.LightGray;
```

The `Load...` methods record the file in the component's data so it is saved with the scene. An Aseprite source can use one layer, several layers (`string[]`), a frame number, and whether hidden layers are included. A sprite with a normal map lights through the deferred renderer; see [Lighting](../graphics/lighting.md).

`Sprite` itself wraps a texture region: `new Sprite(texture, x, y, width, height)`. `Sprite.SpritesFromAtlas(texture, cellWidth, cellHeight)` slices a uniform sheet into a list.

## Sprite animator

`SpriteAnimator` extends `SpriteRenderer` with frame animation. Dropping an `.aseprite` file into the game view creates an entity with an animator: every Aseprite tag becomes a named animation (a file without tags gets one `Default` animation), frame durations come from Aseprite, and the **Default Animation** picker in the Inspector chooses what plays when the scene starts. In Edit mode animations hold their first frame.

```csharp
var anim = GetComponent<SpriteAnimator>();
anim.Play("Run");                                       // loops by default
anim.Play("Attack", SpriteAnimator.LoopMode.Once);
anim.OnAnimationCompletedEvent += name => { if (name == "Attack") anim.Play("Idle"); };

if (anim.IsAnimationActive("Run")) { }
anim.Speed = 1.5f;
anim.Pause(); anim.UnPause(); anim.Stop();
anim.SetFrame(3);
float? progress = anim.GetNormalizedTime();
```

| `LoopMode` | Plays |
|---|---|
| `Loop` | A B C A B C, forever. |
| `Once` | A B C, then stops and returns to the first frame. |
| `ClampForever` | A B C, then holds the last frame. |
| `PingPong` | A B C B A B C, forever. |
| `PingPongOnce` | A B C B A, then stops. |

Animations can also come from code: `AddAnimation(name, sprites, fps)`, `AddAnimationsFromAtlas(atlas)` or `LoadAllAnimations()` after setting the file. Animators that share a file and layer selection share one atlas texture, while playback, frame, tint and origin stay per component.

### Animation events

Events mark frames of an animation. Author them in the [Animation Event Inspector](../editor/animation-events.md): an `AnimationEvent` fires on one frame, a `LongAnimationEvent` covers a frame range. Gameplay code subscribes by animation and event name:

```csharp
anim.SubscribeToEvent("Attack", "Hit", DealDamage);
```

Subscribing to an event that doesn't exist on that animation throws, which catches typos early.

## Sprite atlases

A `SpriteAtlas` holds named sprites and named animations from one texture.

```csharp
SpriteAtlas hero = Entity.Scene.Content.LoadAsepriteAtlas("Content/Hero.aseprite", new[] { "Body", "Cape" });
Sprite first = hero.GetSprite(hero.Names[0]);
SpriteAnimation run = hero.GetAnimation("Run");
animator.AddAnimationsFromAtlas(hero);
```

Aseprite files are the main source: tags become animations and the chosen layers are flattened. For folders of loose PNG files, **File > Open Sprite Atlas Editor** packs a folder into an `.atlas` and `.png` pair (subfolders become animations), lets you slice sheets and set origins, and `Content.LoadSpriteAtlas("Content/Atlases/Items.atlas")` loads the result.

## Other sprite components

| Component | Use |
|---|---|
| `TiledSpriteRenderer` | Repeats a texture across a larger area. `ScrollX`/`ScrollY` offset it and `TextureScale` scales it. |
| `ScrollingSpriteRenderer` | A tiled sprite that scrolls by itself (`ScrollSpeedX`, `ScrollSpeedY`), for parallax layers, water and clouds. |
| `NinePatchSpriteRenderer` | Stretches a sprite while keeping its borders, for panels and frames. |
| `PrototypeSpriteRenderer` | A coloured rectangle of any size, with optional skew. Useful for blocking out levels. |
| `SpriteOutlineRenderer` | Draws an outline (`OutlineWidth`, `OutlineColor`) around another sprite renderer. |
| `SpriteTrail` | Leaves fading copies of a sprite behind a moving entity, for dashes and after-images. |
| `SpriteMime` | Draws another sprite renderer's current frame with its own offset and colour, for shadows and silhouettes. |

These components are built in code; unlike `SpriteRenderer` and `SpriteAnimator`, their settings are not saved with the scene.

## Text

| Component | Use |
|---|---|
| `TextComponent` | A line or block of text with a bitmap font, alignment and colour. |
| `TextRunComponent` | Cached text for labels that rarely change or move; very cheap to draw. |
| `MarkupText` | XML-styled text that mixes fonts, colours, inline images and conditional sections. |
| `FramesPerSecondCounter` | A frame-rate readout. |

Bitmap fonts in BMFont `.fnt` format load with `Content.LoadBitmapFont`. `Graphics.Instance.BitmapFont` is the default font. For menus and HUDs, see [UI](ui.md).

## Particles

`ParticleEmitter` (namespace `Voltage.Particles`) simulates and draws particles from a `ParticleEmitterConfig`. Load a config from a `.pex` file with `Content.LoadParticleEmitterConfig`, or fill one in code.

```csharp
var config = Entity.Scene.Content.LoadParticleEmitterConfig("Content/Particles/Sparks.pex");
var sparks = Entity.AddComponent(new ParticleEmitter(config, playOnAwake: false));
sparks.Emit(20);
sparks.EmitAt(hitPoint);
sparks.OnAllParticlesExpired += e => e.Entity.Destroy();
```

`Play`, `Pause`, `Stop`, `PauseEmission` and `ResumeEmission` control it; `SimulateInWorldSpace` decides whether particles follow the emitter; `CollisionConfig` lets particles collide with physics colliders.

## More renderables

| Component | Use |
|---|---|
| `LineRenderer` | A polyline with optional joint styles, for ropes, lasers and paths. |
| `TrailRibbon` | A ribbon that follows the entity, for swipes and motion trails. |
| `SpringGrid` | A grid of springs that warps under forces, for neon-style backgrounds. |
| `WaterReflectionPlane` | A reflective water surface; see [Rendering](rendering.md#reflections). |
| `PolygonMesh`, `Mesh` | Filled convex polygons and custom vertex meshes. |
| `ColorCycler` | Cycles a renderable's colour with a wave form, for flicker and pulsing. |

For lit sprites, see [Lighting](../graphics/lighting.md); for tilemaps, see [Tilemaps](../scripting/tilemaps.md).
