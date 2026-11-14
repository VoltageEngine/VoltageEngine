---
title: Scenes and Entities
sidebar_position: 1.5
description: Scene loading, renderers set up from project settings, resolution policies, entity lookup, tags, layers and the camera.
---

# Scenes and Entities

A `Scene` holds entities, scene components, renderers and post-processors. Scenes are authored in the editor as `.vscene` files; at runtime you load them by their project-relative key.

## Loading scenes

```csharp
Scene.LoadLevel("Levels/Act1/Rooftop");   // project-relative path without extension
Scene.ReloadCurrentLevel();               // fresh copy of the active scene from disk
```

`LoadLevel` finds the scene anywhere in the project, makes it the active `Core.Scene` and returns it. The switch happens at the end of the current frame, so calling it from `Update` is safe. `scene.LevelName` is the key of a loaded scene and `scene.Name` its display name. Wrap either call in a [scene transition](../rendering/scene-transitions.md) to animate the change.

Only scenes included in the build configuration ship with a game; see [Build Game](../editor/game-build.md). `Voltage.Project.SceneCatalog.Read(Scene.ProjectRoot)` lists the packaged scenes, which is useful for debug level menus.

Prefabs instantiate into the active scene:

```csharp
Entity slime = Core.Scene.LoadPrefab("Actors/Enemies/Slime.vprefab", spawnPosition);
Entity bolt  = BoltPrefab.Instantiate(muzzle);   // BoltPrefab is a PrefabReference
```

## Creating entities in code

```csharp
var pickup = Core.Scene.AddEntity(new Entity("Coin"));
pickup.Position = new Vector2(120, 40);
pickup.AddComponent(new SpriteRenderer()).LoadPngFile("Content/Items/Coin.png");
pickup.AddComponent(new CircleCollider(6) { IsTrigger = true });
```

Entities created this way are `NonSerialized`: they exist only at runtime and are never written to the scene file.

## Finding things

| Method | Returns |
|---|---|
| `FindEntity(name)` | The first entity with that name. |
| `FindEntityWithTag(tag)`, `FindEntitiesWithTag(tag)` | By tag value or tag name. |
| `FindComponentOfType<T>()`, `FindComponentsOfType<T>()` | Components anywhere in the scene. |
| `FindComponentWithName<T>(name)` | A component by type and component name. |
| `FindEntityByPersistentId(guid)` | An entity by its saved identity. |

Lookups scan the scene, so cache the result instead of searching every frame. To react when something appears, use `OnEntityAddedByName`, `OnEntityAddedByTag` or `OnComponentAddedToScene<T>` (each has a `...Once` variant).

## Tags, physics layers and render layers

Project-wide names live in **Project > Settings** and are stored in `ProjectSettings.json`.

| Setting | Defaults | Read in code |
|---|---|---|
| Entity Tags | Default 0, Player 1 | `EntityTags.GetEntityTag("Player")` |
| Physics Layers | Default 0, Ground 1 | `PhysicsLayer.GetPhysicsLayerBit("Ground")`, `PhysicsLayer.GetPhysicsLayerMask("Default", "Ground")` |
| Rendering Layers | Lighting 100, BehindAll 99, HideObject 30, Background 0, Entities 1, Foreground -2, InFrontOfAll -30, UIElement -99 | `RenderLayer.GetRenderLayer("Foreground")` |

These helpers are in `Voltage.Project`. A physics layer is stored as a bit (`1 << index`), so masks combine layers. Renderables are sorted by render layer from the highest value to the lowest, so a lower value appears in front; within one layer, a higher `LayerDepth` draws first.

## Default renderers

When a scene begins without renderers of its own, it sets them up from the Rendering settings:

- Without deferred lighting, one renderer draws every layer except UIElement.
- With **Deferred lighting** checked (Project Settings, Rendering Layers tab), a `DeferredLightingRenderer` draws every layer except Lighting, InFrontOfAll and UIElement, using the Lighting layer for lights and the project's ambient colour. A second renderer then draws InFrontOfAll unlit.
- The UIElement layer always draws last, after post-processing, so screen effects never distort menus and HUD.

Add your own renderers with `scene.AddRenderer(...)`; see [Rendering](../rendering/rendering.md).

## Resolution policies

Each scene renders into a render target at the design resolution and scales it to the window. The project's Display settings set the design size and policy; `scene.SetDesignResolution(width, height, policy)` changes them at runtime.

| Policy | Result |
|---|---|
| `None` | The render target matches the window. |
| `ExactFit` | Stretches to fill the window, distorting the aspect ratio. |
| `NoBorder` | Fills the window without distortion, cropping overflow. |
| `ShowAll` | Shows everything without distortion, with letterbox or pillarbox bars. |
| `FixedHeight`, `FixedWidth` | Keeps one dimension and widens the other to the window's aspect ratio. |
| `BestFit` (default) | Like `ShowAll`, with an optional bleed area that may be cropped. |
| `...PixelPerfect` variants | Restrict scaling to whole numbers for crisp pixel art. |
| `SmoothPixelPerfect` | Draws art pixels at a whole-number scale but moves the camera at display-pixel precision, for smooth scrolling. |

`LetterboxColor` colours the bars and `ClearColor` the scene background. `scene.RequestScreenshot(tex => ...)` captures the next rendered frame.

## The camera

Every scene has a main camera entity, `Camera (Main)`, which cannot be deleted. `scene.Camera` exposes it:

```csharp
var cam = Core.Scene.Camera;
cam.Position = player.Position;
cam.SetZoom(0.5f);                          // -1 maps to MinimumZoom, 0 to 1x, 1 to MaximumZoom
Vector2 world = cam.ScreenToWorldPoint(Input.MousePosition);
Vector2 mouse = cam.MouseToWorldPoint();
RectangleF view = cam.Bounds;               // visible world area
```

`WorldToScreenPoint`, `ScreenToWorldPoint` and `TouchToWorldPoint` convert coordinates, and `SetInset` shrinks the culling bounds.
