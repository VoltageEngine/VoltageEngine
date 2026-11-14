---
title: Time, Timers and Coroutines
sidebar_position: 2.5
description: Frame timing, time scale, delayed and repeating timers, and coroutines.
---

# Time, Timers and Coroutines

## Time

`Voltage.Utils.Time` holds the frame timing, updated once per frame before anything else.

| Member | Meaning |
|---|---|
| `DeltaTime` | Seconds since the last frame, multiplied by `TimeScale`. Use it for gameplay. |
| `UnscaledDeltaTime` | The same, ignoring `TimeScale`. Use it for UI and pause menus. |
| `TimeScale` | Global speed of `DeltaTime`. `0.5f` is slow motion, `0` freezes gameplay. |
| `AltDeltaTime`, `AltTimeScale` | A second, independently scaled delta, for when two parts of the game need different speeds. |
| `MaxDeltaTime` | Caps a single frame's delta, so a hitch or a dragged window doesn't push physics too far. |
| `TotalTime`, `TimeSinceSceneLoad`, `FrameCount` | Running totals. `TimeSinceSceneLoad` resets on every scene change. |
| `CheckEvery(interval)` | `true` on the frame each interval elapses, for periodic work without a timer. |

The `timescale` console command changes `TimeScale` while testing.

## Timers

`Core.Schedule` runs an action after a delay, once or repeatedly. Timers are global: they keep running across scene changes until they finish or are stopped.

```csharp
using Voltage;
using Voltage.Utils.Timers;

Core.Schedule(2f, timer => Debug.Log("Two seconds later"));

ITimer pulse = Core.Schedule(0.5f, repeats: true, context: this, timer =>
{
    var self = timer.GetContext<Beacon>();
    self.Blink();
});

pulse.Stop();             // no further calls
pulse.Reset();            // restart the countdown
float left = pulse.RemainingTime();
```

Timers advance with `Time.DeltaTime`, so they slow down with `TimeScale` and pause when it is 0. Pass a context object instead of capturing `this` in a lambda to avoid an allocation per timer. Stop timers that refer to an entity in its `OnRemovedFromEntity`.

## Coroutines

A coroutine is an `IEnumerator` that runs a little each frame. Start one with `Core.StartCoroutine`:

```csharp
using System.Collections;
using Voltage;
using Voltage.Utils.Coroutines;

IEnumerator FlashThenFade()
{
    _sprite.Color = Color.Red;
    yield return Coroutine.WaitForSeconds(0.1f);
    _sprite.Color = Color.White;

    yield return Core.StartCoroutine(Fade());   // wait for another coroutine
    Debug.Log("done");
}

ICoroutine handle = Core.StartCoroutine(FlashThenFade());
handle.Stop();
```

| Yield | Effect |
|---|---|
| `yield return null` | Continue next frame. |
| `yield return Coroutine.WaitForSeconds(s)` | Wait `s` seconds of `DeltaTime`. |
| `yield return Core.StartCoroutine(other)` or `yield return other` (an `IEnumerator`) | Wait until the other coroutine finishes. |
| `yield break` | End the coroutine. |

`SetUseUnscaledDeltaTime(true)` on the handle makes its waits ignore `TimeScale`. Don't yield a plain number to wait. The editor honours it but logs an error, and a published build waits only one frame.

Coroutines are global like timers, so a coroutine that touches an entity should check that the entity still exists, or be stopped in `OnRemovedFromEntity`. `Core.GetGlobalManager<CoroutineManager>().StopAllCoroutines()` stops every running coroutine.

Waiting on a [Timeline](../editor/timelines.md) cutscene works the same way: `yield return director.PlayAndWait();`.
