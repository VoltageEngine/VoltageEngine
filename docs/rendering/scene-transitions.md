---
title: Scene Transitions
sidebar_position: 3
description: Animated changes between scenes, in-scene transitions and writing your own.
---

# Scene Transitions

A transition hides a scene change behind an effect. Start one with `Core.StartSceneTransition`, giving it a function that loads the next scene:

```csharp
Core.StartSceneTransition(new FadeTransition(() => Scene.LoadLevel("Menus/Title"))
{
    FadeToColor = Color.Black,
    FadeOutDuration = 0.4f,
    FadeInDuration = 0.6f,
});
```

Only one transition runs at a time. Without a load function the transition plays inside the current scene: use its `OnScreenObscured` callback to move the camera, respawn the player or rebuild the level while the screen is covered.

```csharp
var wipe = new SquaresTransition { SquareColor = Color.Black };
wipe.OnScreenObscured = () => player.Position = checkpoint;
wipe.OnTransitionCompleted = () => Debug.Log("back in control");
Core.StartSceneTransition(wipe);
```

| Transition | Effect | Main settings |
|---|---|---|
| `FadeTransition` | Fades to a colour, then into the new scene. | `FadeToColor`, `FadeOutDuration`, `DelayBeforeFadeInDuration`, `FadeInDuration`, `FadeEaseType` |
| `CrossFadeTransition` | Blends the old scene into the new one. | `FadeDuration`, `FadeEaseType` |
| `SquaresTransition` | A grid of squares covers and reveals the screen. | `SquareColor`, `Size`, `Smoothness`, `SquaresInDuration`, `SquaresOutDuration` |
| `TextureWipeTransition` | Wipes using a greyscale texture as the pattern. | `TransitionTexture`, `Duration`, `Opacity`, `UseRedGreenChannelsForDistortion` |
| `ImageMaskTransition` | Scales and rotates a mask image to reveal the new scene. | `MinScale`, `MaxScale`, `MinRotation`, `MaxRotation`, `Duration` |
| `TransformTransition` | Zooms, spins or slides the old scene away. | `TransformTransitionType`, `Duration`, `TransitionEaseType` |
| `WindTransition` | Streaks the scene away like wind. | `WindSegments`, `Size`, `Duration` |

While a transition that loads a scene is running, the old scene stops updating until the new one is in place. `HideLoadingScreen` keeps the transition visible instead of the loading display, and `LoadSceneOnBackgroundThread` loads the next scene off the main thread (only for compiled content; raw PNG files must load on the main thread).

## Writing a transition

Derive from `SceneTransition` and override `OnBeginTransition`, a coroutine. A two-part transition covers the screen, loads the next scene, then uncovers it:

```csharp
using System.Collections;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Voltage;
using Voltage.Utils.Tweens.Easing;

public class IrisTransition : SceneTransition
{
    readonly Effect _effect;

    public IrisTransition(System.Func<Scene> load) : base(load, wantsPreviousSceneRender: true)
    {
        _effect = Core.Content.LoadEffect("Content/Effects/Iris.mgfxo");
    }

    public override IEnumerator OnBeginTransition()
    {
        yield return null;                                         // let the previous scene render once
        yield return Core.StartCoroutine(TickEffectProgressProperty(_effect, 0.5f, EaseType.QuadIn));
        yield return Core.StartCoroutine(LoadNextScene());          // calls OnScreenObscured
        yield return Core.StartCoroutine(TickEffectProgressProperty(_effect, 0.5f, EaseType.QuadOut, reverseDirection: true));
        TransitionComplete();
    }

    public override void Render(Batcher batcher)
    {
        Core.GraphicsDevice.SetRenderTarget(null);
        batcher.Begin(BlendState.NonPremultiplied, Core.DefaultSamplerState, DepthStencilState.None, null, _effect);
        batcher.Draw(PreviousSceneRender, Vector2.Zero, Color.White);
        batcher.End();
    }
}
```

`PreviousSceneRender` holds the last frame of the old scene when `WantsPreviousSceneRender` is true. `TickEffectProgressProperty` animates an effect's `_progress` parameter from 0 to 1 (or back) with an ease. `_isNewSceneLoaded` tells `Render` which half of the transition is playing. `TransitionComplete()` must run at the end; override it to dispose your own resources, and call the base method.
