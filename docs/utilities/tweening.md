---
title: Tweening
sidebar_position: 1
description: Animate positions, scale, rotation, colours and any numeric member over time with easing, loops and callbacks.
---

# Tweening

A tween moves a value from its current state to a target over a duration. The global `TweenManager` updates every running tween, so you create one, configure it and call `Start()`.

```csharp
using Voltage.Utils.Tweens;
using Voltage.Utils.Tweens.Easing;

Entity.TweenPositionTo(new Vector2(200, 80), 0.5f)
      .SetEaseType(EaseType.BackOut)
      .SetCompletionHandler(t => Debug.Log("arrived"))
      .Start();
```

## Built-in tweens

| Extension | Animates |
|---|---|
| `TweenPositionTo`, `TweenLocalPositionTo` | `Entity` or `Transform` position. |
| `TweenScaleTo`, `TweenLocalScaleTo` | Scale, from a `float` or a `Vector2`. |
| `TweenRotationDegreesTo`, `TweenLocalRotationDegreesTo` | Rotation in degrees. |
| `TweenColorTo` | A renderable's `Color`. |

`PropertyTweens` animates any public field or property by name: `PropertyTweens.FloatPropertyTo(light, "Intensity", 0f, 1.2f).Start();`. There are versions for `int`, `float`, `Vector2`, `Vector3`, `Vector4`, `Quaternion` and `Color`. They use reflection, so prefer a typed tween (`FloatTween`, `Vector2Tween`, `ColorTween` and others) with your own `ITweenTarget<T>` in code that runs often or must be trim-safe.

## Configuring a tween

| Method | Effect |
|---|---|
| `SetEaseType(EaseType)` | Easing curve. Defaults to `TweenManager.DefaultEaseType` (`QuartIn`). |
| `SetDuration(s)`, `SetDelay(s)` | Length and start delay. |
| `SetLoops(LoopType, loops, delayBetweenLoops)` | `RestartFromBeginning` or `PingPong`; `-1` loops forever. |
| `SetFrom(value)` | Start value instead of the current one. |
| `SetIsRelative()` | Treat the target as an offset from the start. |
| `SetTimeScale(scale)`, `SetIsTimeScaleIndependent()` | Speed, or ignore `Time.TimeScale` (for paused menus). |
| `SetCompletionHandler`, `SetLoopCompletionHandler` | Callbacks at the end and after each loop. |
| `SetContext(object)` | Data for callbacks and for `StopAllTweensWithContext`. |
| `SetNextTween(tween)` | Starts another tween when this one ends. |
| `SetRecycleTween(false)` | Keep the tween object after it finishes so you can restart it. |

Ease types cover `Linear`, `Sine`, `Quad`, `Cubic`, `Quart`, `Quint`, `Expo`, `Circ`, `Elastic`, `Back` and `Bounce` (each with `In`, `Out` and `InOut`) and `Punch`. `Lerps.Ease(easeType, from, to, elapsed, duration)` applies a curve yourself.

## Controlling tweens

A started tween supports `Pause()`, `Resume()`, `Stop(bringToCompletion)`, `IsRunning()` and `JumpToElapsedTime(t)`. A coroutine can wait for one with `yield return tween.WaitForCompletion();`.

```csharp
TweenManager.StopAllTweensWithTarget(Entity.Transform);
TweenManager.StopAllTweensWithContext(this, bringToCompletion: true);
TweenManager.StopAllTweens();
```

Tweens are global and keep running across scene changes unless `TweenManager.RemoveAllTweensOnLevelLoad` is set. Stop tweens that target an entity when it is destroyed. Finished tweens of common types are recycled to avoid allocations, so don't keep a reference to one after it completes unless you called `SetRecycleTween(false)`.

## Springs

`TransformSpringTween` follows a moving target with a damped spring instead of a fixed duration, for camera follow, wobble and squash. Set `DampingRatio` and `AngularFrequency`, start it like a tween and update its goal with `SetTargetValue`.

For keyframed cutscene animation, use the [Timeline](../editor/timelines.md).
