---
title: Core and Global Systems
sidebar_position: 0
description: The Core class, the game entry point, global managers, engine events and the edit/play state flags.
---

# Core and Global Systems

`Voltage.Core` derives from MonoGame's `Game` and is the root of every Voltage game. It owns the active scene and exposes the engine's subsystems through static members, so component code never needs a reference to the game object.

## The game entry point

**File > New Project** generates `Program.cs` with a `<Project>Game : Core` class. You rarely edit it. At startup it:

1. Reads `ProjectSettings.json` from the output folder into `ProjectSettings.Instance`.
2. Loads the default bitmap font and creates `Graphics.Instance`.
3. Calls `Scene.LoadLevel(settings.InitialScene, applyDisplaySettings: true)`, which applies the display and design-resolution settings and makes that scene active.

Inside the editor, the editor plays this role. Your scripts run against the same `Core` API in both cases.

## Static members

| Member | Purpose |
|---|---|
| `Core.Scene` | The active scene. Assigning a new scene takes effect at the end of the current frame. |
| `Core.Content` | Global content manager for assets that live for the whole session. See [Content Loading](content-loading.md). |
| `Core.GraphicsDevice` | The MonoGame graphics device. |
| `Core.Audio` | The global audio hub. See [Audio](audio.md). |
| `Core.Emitter` | Engine-level events (below). |
| `Core.DebugRenderEnabled` | Draws collider, renderable and light debug overlays. On in the editor, off in game builds. |
| `Core.DefaultSamplerState` | Sampler used by new materials. Defaults to point filtering for pixel art; set it before any material is created. |
| `Core.ExitOnEscapeKeypress` | Quits on Escape or gamepad Back. Defaults to `true`; most games turn it off. |
| `Core.PauseOnFocusLost` | Stops updating and drawing while the window is unfocused. Editor Play mode keeps running. |
| `Core.Instance` | The running `Core` instance. |

`Core.Scene = nextScene` is the low-level switch. Most games call `Scene.LoadLevel("Path/To/Scene")` or start a [scene transition](../rendering/scene-transitions.md). Either way the current scene ends, `CoreEvents.SceneChanged` fires, and the new scene begins on the next frame.

## Edit, Play and Pause state

The same scene runs in the editor and in a published game, so the engine tracks which mode it is in.

| Member | Meaning |
|---|---|
| `Core.IsEditorMode` | `true` when running inside the Voltage Editor. |
| `Core.IsEditMode` | `true` while the editor is in Edit mode. Component `Update` methods do not run. Always `false` in a game build. |
| `Core.IsPauseMode` | `true` while Play is paused. Only components that implement `IUpdatableInPauseMode` keep updating. |
| `Core.IsAudioOn` | Global mute switch used by the toolbar's audio button. |
| `Core.ResetSceneAutomatically` | Reloads the scene from disk when the editor returns from Play to Edit. |

Subscribe to `Core.OnSwitchEditMode`, `Core.OnSwitchPauseMode`, `Core.OnResetScene` or `Core.OnSwitchAudio` to react to these changes. Unsubscribe in `OnRemovedFromEntity`: a handler that outlives its scene keeps the scene alive after every Play/Stop cycle.

## Engine events

`Core.Emitter` is an `Emitter<CoreEvents>` that raises engine-wide events:

| Event | Raised when |
|---|---|
| `GraphicsDeviceReset` | The window or back buffer changes size. Render targets must be recreated. Resize events are coalesced. |
| `SceneChanged` | After one scene ends and before the next begins. |
| `OrientationChanged` | The device orientation changes. |
| `Exiting` | The game is closing. |

```csharp
using Voltage;

public partial class ResizeWatcher : Component
{
    public override void OnAddedToEntity() =>
        Core.Emitter.AddObserver(CoreEvents.GraphicsDeviceReset, OnResize);

    public override void OnRemovedFromEntity() =>
        Core.Emitter.RemoveObserver(CoreEvents.GraphicsDeviceReset, OnResize);

    void OnResize() => Debug.Log($"New size: {Screen.Width}x{Screen.Height}");
}
```

Adding the same handler twice fails an assertion, and removing a handler from an event that never had one throws. Observers run in reverse order of registration.

`Emitter<T>` and `Emitter<T, TData>` in `Voltage.Utils` work for your own events too, with any `int` or enum key. Pass an `IEqualityComparer<T>` (see `CoreEventsComparer`) to avoid boxing enum keys.

## Global managers

A `GlobalManager` is an object that updates every frame regardless of which scene is active, and survives scene changes. Use one for systems such as save management, achievements or analytics.

```csharp
using Voltage;
using Voltage.Utils;

public class PlayTimeTracker : GlobalManager
{
    public float Seconds;

    public override void Update() => Seconds += Time.UnscaledDeltaTime;
}

Core.RegisterGlobalManager(new PlayTimeTracker());
var tracker = Core.GetGlobalManager<PlayTimeTracker>();
```

`Enabled` toggles a manager and calls `OnEnabled`/`OnDisabled`. Managers update before the scene and in reverse order of registration. The engine registers its own coroutine, tween, timer, audio and render-target managers this way; a built game started with `--gateway` also registers the runtime [gateway](../gateway/README.md).

Global managers keep running while the editor is in Edit mode and while Play is paused. Check `Core.IsEditMode` and `Core.IsPauseMode` if your manager should hold still.
