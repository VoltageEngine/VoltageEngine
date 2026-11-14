---
title: Scripting Rules and Gotchas
sidebar_position: 20
description: The rules that keep components saving, loading and running correctly in the editor and in NativeAOT builds.
---

# Scripting Rules and Gotchas

## Serialization

- **Declare saved components `partial`.** This applies to every `Component` and `SceneComponent` whose fields should persist. Without it the component still loads and runs, but its fields reset to their defaults on every load. Data assets must be `partial`; the generator reports `VLT012` otherwise.

- **Leave the generated `[ComponentId("...")]` alone.** On its first compile without one, the editor stamps `[ComponentId]` onto each concrete component and scene component, using the class name at that moment. Scenes store that id, so you can later rename the class or move its namespace freely. Deleting or editing the attribute creates a new identity, and scenes that referenced the old id lose the component. Abstract classes are never stamped, and sources of installed (non-dev) plugins are left untouched, so plugin authors write the attribute themselves.

- **Renaming a serialized field drops its saved value.** `[ComponentId]` identifies the type, not its fields; values are stored under the field name, and the generated reader matches names exactly. Keep the old name, or re-enter the value after renaming. `[DecodeAlias]` from `Voltage.Persistence` only affects hand-written `ComponentData` classes, which are read by reflection.

- **Don't give serialized structs an explicit parameterless constructor.** The generated reader creates structs with `new T()` in published builds, which skips defaults set in that constructor. The generator reports `VLT003` for this pattern. Use a class that implements `IComponentGroup` instead.

- **Initialize `IComponentGroup` fields where you declare them.**

  ```csharp
  public MoveSettings Settings = new MoveSettings();  // correct
  public MoveSettings Settings;                       // null when the component is saved
  ```

- **Non-public fields need `[Serialize]` to be saved and shown.** Public fields are included by default; `[HideInInspector]` hides a field from the Inspector. See [Inspector Attributes](inspector-attributes.md).

## Edit and Play

- **`OnStart` runs in Edit mode too.** The editor starts every component when it loads a scene, and Play only begins calling `Update`. Guard gameplay with `Core.IsEditMode` and start it from `Core.OnSwitchEditMode`. See [Edit and Play](components.md#edit-and-play).

- **`SceneComponent.Update` and global managers run in Edit mode.** Return early on `Core.IsEditMode` for gameplay logic.

- **Unsubscribe from static events.** `Core.OnSwitchEditMode`, `Core.OnSwitchPauseMode`, `Core.OnSwitchAudio` and `Core.Emitter` outlive scenes. A handler left subscribed keeps a dead scene alive and can act on the wrong scene after the next Play.

- **Implement `IUpdatableInPauseMode` for menus and HUD.** Other components stop updating while Play is paused.

- **Don't save during Play or Pause.** `Ctrl+S` is blocked in Play. Changes made in Play mode are discarded when you stop, because the scene reloads from disk while `Core.ResetSceneAutomatically` is on.

- **Script edits during Play wait.** The editor queues the compile and runs it, then reloads the scene, after Play stops.

## Content

- **Prefer `Scene.Content` for scene-only assets and `Core.Content` for assets used everywhere.** `Scene.Content` is disposed when the scene ends; loading a one-scene texture into `Core.Content` keeps it in memory for the whole session.

- **Don't keep a scene's content manager, or its assets, across a scene change.** They are disposed with the scene.

- **Use the `SpriteRenderer` loaders when the sprite should be saved.** `LoadPngFile`, `LoadAsepriteFile` and `LoadTmxFile` record the file in the component's data. A sprite assigned with `SetSprite` is runtime-only.

- **Prefer asset references for anything assigned in the editor.** `AssetReference` and `PrefabReference` resolve by GUID, through the baked `Data/assets.manifest` in published builds, so moving or renaming the file is safe. See [Content Loading](content-loading.md).

- **Don't delete `.meta` files while the project is open.** The editor regenerates a missing `.meta` with a new GUID, which breaks every reference to the old one. Move and rename assets in the Asset Browser so their `.meta` files travel with them.

## Audio

- **Engine audio components need no extra wiring for mute.** The mixer silences every bus while `Core.IsAudioOn` is `false`. Only custom code that plays `SoundEffectInstance`s directly should implement `IAudioComponent` and register with `AudioComponentRegistry`. See [Audio](audio.md).
