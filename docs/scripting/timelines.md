---
title: Timeline Scripting
sidebar_position: 3.8
description: Play cutscenes from code, react to timeline signals, expose methods and properties to tracks, and add preview support.
---

# Timeline Scripting

A `.vtimeline` asset is a reusable sequence of tracks: transforms, camera, tint, sprite animation, activation, audio, component properties, events, nested timelines, prefab spawns and markers. A `TimelineDirector` component plays one in a scene and binds the asset's roles to scene entities. Authoring happens in the [Timeline Editor](../editor/timelines.md); this page covers the code side (namespace `Voltage.Cinematics`).

## Playing a timeline

```csharp
using Voltage.Cinematics;

var director = GetComponent<TimelineDirector>();
director.OnFinished += () => _player.EnableControl();
director.OnCancelled += () => Debug.Log("cutscene aborted");
director.Play();

// or from a coroutine
yield return director.PlayAndWait();
```

| Member | Purpose |
|---|---|
| `Timeline` | The `.vtimeline` asset reference. |
| `Bindings` | Role name to scene entity. Spawned prefabs bind themselves. |
| `PlayOnStart`, `Wrap` (`Hold`, `Loop`, `PingPong`, `Stop`), `Speed` | Playback setup. |
| `UseUnscaledTime` | Keep playing while `Time.TimeScale` is changed or 0. |
| `Play`, `Pause`, `Resume` | Transport. |
| `Seek(time)`, `SeekToMarker(name)` | Jump without firing events, for checkpoints and rewinds. |
| `Skip()` | The player skips: events marked to fire on skip run in order, everything snaps to the end, `OnFinished` fires. |
| `Stop()` | End normally at the final frame. |
| `Cancel()` | Abort and restore the scene to its state before the cutscene, despawning anything it spawned. |
| `State`, `PlayheadTime`, `Duration` | Inspection. |
| `Validate()` | Lists references that won't resolve with the current bindings. Problems are also logged once when playback starts. |

Tracks come in two kinds. Parameter tracks (transform, camera, tint, sprite, activation, properties) are pure functions of time, so seeking and scrubbing are exact. Events fire once when the playhead crosses them going forward.

## Signals

An event without a target is broadcast. Listen with `OnSignal`, which keeps game code decoupled from the asset:

```csharp
director.OnSignal += (name, args) =>
{
    if (name == "ShakeCamera") _shake.Start(args.Length > 0 ? args[0].AsFloat() : 1f);
};
```

## Calling component methods

Mark a public component method with `[TimelineEvent]` and it appears in the event track's method list for any entity bound to a role:

```csharp
public partial class Door : Component
{
    [TimelineEvent(DisplayName = "Open door")]
    public void Open(float speed) { }

    [TimelineEvent]
    public void Lock() { }
}
```

Parameters may be `float`, `int`, `bool`, `string`, `EntityReference`, `ComponentReference`, `AssetReference` or `PrefabReference`. The source generator builds a dispatch table for these methods, so they are called without reflection and work under NativeAOT.

## Animating component properties

Mark a `float`, `Vector2` or `Color` field or property with `[TimelineProperty]` to make it available to property tracks:

```csharp
public partial class Lantern : Component
{
    [TimelineProperty] public float Brightness = 1f;
    [TimelineProperty(DisplayName = "Glow colour")] public Color Glow = Color.Orange;
}
```

Property tracks sample keyframes with the timeline's eases every frame, so the value follows scrubbing in the editor and seeking at runtime.

## Editor preview

The Timeline Editor previews parameter tracks in Edit mode without running gameplay. A component that draws presentation the preview should show, such as cinematic text or an overlay, implements `ITimelinePreviewParticipant`: capture its state into the provided `StateSnapshot`, then evaluate itself at a given time. The editor restores the captured state when preview stops.

```csharp
public partial class Subtitle : Component, ITimelinePreviewParticipant
{
    public void CaptureTimelinePreview(StateSnapshot snapshot) { /* record what you change */ }
    public void EvaluateTimelinePreview(float time, TimelineAsset asset, TimelineDirector director) { /* show the line for 'time' */ }
}
```

## Custom tracks

Derive from `TimelineParameterTrack`, give it a stable `[TrackTypeId("my.track")]`, and register it with `TimelineTrackRegistry.Register(typeof(MyTrack))`, typically from a `[ModuleInitializer]`. The id, not the class name, is saved in the asset, so the class can be renamed later. Plugins use this route: [DialogueMaker](../integrations/dialoguemaker.md) adds a dialogue track that starts a conversation when the playhead reaches its clip.

The gateway's `timeline.*` commands create, edit, bind, validate and play timelines from scripts and agents.
