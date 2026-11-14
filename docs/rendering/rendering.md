---
title: Rendering
sidebar_position: 2
description: Renderers, render layers and sorting, render textures, materials and blend modes, stencil effects, post-processing, immediate drawing and 3D.
---

# Rendering

Each frame a scene runs its renderers in `RenderOrder`, then its post-processors, then the renderers marked to draw after post-processing, and finally scales the result to the window using the [resolution policy](../scripting/scenes-and-entities.md#resolution-policies).

## Renderers

A scene starts with renderers created from the project's Rendering settings (see [Default renderers](../scripting/scenes-and-entities.md#default-renderers)). Add your own when a scene needs more control:

```csharp
var ui = RenderLayer.GetRenderLayer("UIElement");
Core.Scene.AddRenderer(new ScreenSpaceRenderer(100, ui) { WantsToRenderAfterPostProcessors = true });
```

| Renderer | Draws |
|---|---|
| `DefaultRenderer` | Every renderable, with the scene camera. |
| `RenderLayerRenderer` | Only the listed render layers. |
| `RenderLayerExcludeRenderer` | Every layer except the listed ones. |
| `ScreenSpaceRenderer` | The listed layers with a fixed camera, for HUDs in screen coordinates. |
| `DeferredLightingRenderer` | Normal-mapped, lit sprites. See [Lighting](../graphics/lighting.md). |
| `StencilLightRenderer` | 2D lights with hard shadows into a light map. See [Lighting](../graphics/lighting.md#2d-shadow-lights). |
| `ReflectionRenderer` | Reflected layers for mirror and water materials. See [Reflections](#reflections). |

Shared settings: `Camera` (null uses the scene camera), `Material` (default for its renderables), `RenderTexture` with `RenderTargetClearColor` (draw into a texture instead of the screen), `WantsToRenderAfterPostProcessors` and `ShouldDebugRender`. `scene.GetRenderer<T>()` and `RemoveRenderer` manage them. To write your own, derive from `Renderer` and override `Render(Scene)`.

A `RenderTexture` resizes with the scene render target by default; set its `ResizeBehavior` for a fixed size. A renderer with a render texture draws there, and its texture can feed a material, a post-processor or a sprite.

## Render order

Renderables are drawn by render layer from the highest value to the lowest, so lower layers appear on top, then by `LayerDepth` (higher first) within a layer. Layers are named in **Project > Settings > Rendering Layers**; use the names in code through `RenderLayer.GetRenderLayer("Foreground")` and mark `int` fields `[RenderLayer]` for a dropdown.

Changing a renderable's `LayerDepth` re-sorts its layer. To impose your own order, assign `RenderableComponentList.CompareUpdatableOrder` an `IComparer<IRenderable>`, and call `scene.RenderableComponents.SetRenderLayerNeedsComponentSort(layer)` when you need a layer sorted again.

## Materials and blend modes

A `Material` bundles blend state, depth-stencil state, sampler state and an optional `Effect`. Assign one with `renderable.SetMaterial(material)`.

```csharp
sprite.SetMaterial(Material.BlendAdditive());               // glows and sparks
sprite.SetMaterial(new Material<SpriteBlinkEffect>(new SpriteBlinkEffect()));
```

Presets: `BlendAdditive`, `BlendScreen`, `BlendMultiply`, `BlendMultiply2x`, `BlendLighten`, `BlendDarken`, `BlendLinearDodge`, `BlendLinearBurn`, `BlendDifference`, `BlendSubtractive`, plus `Material.DefaultOpaqueMaterial`. Override `OnPreRender(Camera)` in a subclass to update shader parameters each frame.

Built-in sprite effects include `SpriteBlinkEffect`, `SpriteAlphaTestEffect`, `SpriteLinesEffect`, `DissolveEffect`, `GrayscaleEffect`, `SepiaEffect`, `InvertEffect`, `CrosshatchEffect`, `DotsEffect`, `NoiseEffect`, `TwistEffect`, `ScanlinesEffect` and `PaletteCyclerEffect`. Load them with `Content.LoadVoltageEffect<T>()`; project shaders are covered in [Effects](../effects.md).

`Core.DefaultSamplerState` uses point filtering, which keeps pixel art sharp. Set it before materials are created to change the default.

### Stencil effects

`Material.StencilWrite(ref)` marks pixels in the stencil buffer and `Material.StencilRead(ref)` draws only where the buffer holds that value. Together they produce silhouettes behind walls, masks and x-ray reveals:

```csharp
wall.SetMaterial(Material.StencilWrite(1));
playerSilhouette.SetMaterial(Material.StencilRead(1));     // e.g. a SpriteMime of the player, tinted
```

The silhouette renderable must draw after the wall, so give it a lower render layer or a lower `LayerDepth`.

## Post-processing

Post-processors run full-screen passes on the rendered scene in `ExecutionOrder`. They are added in code, usually from a component or scene component, and are not saved with the scene. While editing you can try them in the Scene Graph's **Post Processors** section.

```csharp
var glitch = Entity.Scene.AddPostProcessor(new PixelGlitchPostProcessor(10));
glitch.Enabled = false;                       // switch on for a hit, then off again
Entity.Scene.RemovePostProcessor(glitch);
```

| Post-processor | Effect |
|---|---|
| `BloomPostProcessor` | Soft bloom from bright areas. |
| `PixelBloomPostProcessor` | Bloom from one layer's render texture, sized for pixel art. |
| `GaussianBlurPostProcessor` | Full-screen blur. |
| `VignettePostProcessor` | Darkened edges. |
| `ScanlinesPostProcessor` | CRT scanlines. |
| `PixelGlitchPostProcessor` | Rows shifted sideways, for damage and glitch moments. |
| `HeatDistortionPostProcessor` | Wavy heat haze. |
| `CinematicLetterboxPostProcessor` | Animated letterbox bars for cutscenes. |
| `SpriteLightPostProcessor`, `PolyLightPostProcessor` | Combine a light map with the scene. See [Lighting](../graphics/lighting.md). |

`scene.EnablePostProcessing` turns them all off at once. For a custom pass, derive from `PostProcessor<TEffect>` and set effect parameters before `Process` runs. UI drawn on the UIElement layer is never post-processed.

`scene.FinalRenderDelegate` replaces the last step that scales the scene to the window. `PixelMosaicRenderDelegate` uses it to overlay a pixel grid on pixel-perfect output.

## Reflections

`ReflectionRenderer.CreateAndSetupScene(scene, renderOrder, layers)` renders the listed layers into a texture before the main renderer. A `ReflectionMaterial` on a surface samples it for mirrors; `WaterReflectionMaterial` and the `WaterReflectionPlane` component produce animated water at the bottom of the view.

## Immediate drawing

`Graphics.Instance.Batcher` draws sprites, text and primitives directly, for example from a custom renderable's `Render` or a component's `DebugRender`:

```csharp
public override void Render(Batcher batcher, Camera camera)
{
    batcher.Draw(_texture, Entity.Position, Color);
    batcher.DrawRect(new Rectangle(0, 0, 32, 4), Color.Red);
    batcher.DrawHollowRect(Bounds, Color.Yellow);
    batcher.DrawLine(a, b, Color.White, thickness: 2);
    batcher.DrawCircle(Entity.Position, 24, Color.Cyan);
    batcher.DrawString(Graphics.Instance.BitmapFont, "Ready", Entity.Position, Color.White);
}
```

`DrawPixel`, `DrawPoints`, `DrawPolygon` and `DrawLineAngle` complete the set. Primitives use `Graphics.Instance.PixelTexture`; replace it with a white pixel from your own atlas to avoid texture switches. `Graphics.CreateSingleColorTexture` makes solid textures.

## 3D content

2D scenes can include simple 3D objects. `Cube3D`, `Sphere3D`, `Torus3D` and `Model3D` derive from `Renderable3D`, which follows the entity's `Transform` and exposes it as `Vector3` `Position`, `Rotation` (or `RotationDegrees`) and `Scale`, plus a `WorldMatrix`. The camera's `PositionZ3D`, `NearClipPlane3D` and `FarClipPlane3D` control the 3D projection, which lines up with the 2D view.
