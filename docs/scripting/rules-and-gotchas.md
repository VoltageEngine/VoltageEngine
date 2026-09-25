---
title: Scripting Rules and Gotchas
sidebar_position: 5
---

# Scripting Rules and Gotchas

## Do

- **Always declare serialized components `partial`.**
  Any `Component` or `SceneComponent` subclass that you want saved/loaded must be `partial`. If you forget, the class still works in code but its public fields reset to defaults on every scene reload.

- **Use `SpriteRenderer.LoadPngFile` / `LoadAsepriteFile` / `LoadTmxFile` from code, not raw content loading.**
  These methods update the component's internal `_data` so the file path is saved with the scene. Direct calls to `Scene.Content.LoadTexture` and `SetSprite(new Sprite(tex))` work visually but the path is not serialized.

- **Register audio components with `AudioComponentRegistry`.**
  Implement `IAudioComponent`, call `AudioComponentRegistry.Register(this)` in `OnAddedToEntity`, and `Unregister` in `OnRemovedFromEntity`. Guard your own `Play` calls with `Core.IsAudioOn`.

- **Never edit or delete a generated `[ComponentId("...")]`; it is what survives class renames and namespace moves.**
  The editor stamps `[ComponentId]` onto every concrete component and scene component on its first compile without one, using the class name at that moment, and then leaves it alone. Scenes and prefabs store that id, so you can rename the class or move it to another namespace freely. If you delete the attribute the next compile stamps a fresh one from the current class name, which is a new identity, and every scene that referenced the old id loses the component. Changing the string by hand does the same. Two exceptions: abstract classes are never stamped, and sources of installed (non-dev) plugins are not touched, so plugin authors write the attribute themselves.

- **Use `[DecodeAlias("oldName")]` when renaming a field.**
  `[ComponentId]` identifies the type, not its fields; field values are saved under the field name. Without the alias, old scenes and save files silently drop the renamed field's value.

- **Use `IUpdatableInPauseMode` for UI and menus.**
  Without it, your component stops updating the moment the user presses Pause.

- **Initialize `IComponentGroup` fields at the declaration site.**
  ```csharp
  public MyGroup Settings = new MyGroup();  // correct
  public MyGroup Settings;                  // null at snapshot time → logged error
  ```

- **Prefer `Scene.Content` for scene-lifetime assets, `Core.Content` for cross-scene assets.**
  `Scene.Content` is disposed when the scene ends. Using `Core.Content` for a texture that is only needed in one scene leaks memory.

## Do Not

- **Do not use structs with explicit parameterless constructors as serializable fields.**
  The source generator emits `new T()` in the AOT reader, which ignores any field defaults set inside an explicit constructor. The editor works (reflection is available) but the published build silently drops those defaults. The generator emits a compile-time error (`VLT003`) when it detects this pattern. Use a class implementing `IComponentGroup` instead.

- **Do not save scene data in Play or Pause mode.**
  `Ctrl+S` is blocked. Changes made during Play mode are intended to be discarded when you return to Edit mode (or when `Core.ResetSceneAutomatically` reloads the scene).

- **Asset references resolve by GUID at runtime via the baked manifest.**
  The editor writes `Data/assets.manifest` (a `GUID → project-relative path` map) on every asset-database refresh; it ships with the build and `Voltage.Assets.AssetManifest` loads it at runtime. This is what lets a renamed/moved prefab still resolve in a published game. If the manifest is missing (e.g. a build that never opened in the editor), resolution falls back to name/path.

- **Do not cache `Core.Scene.Content` across scene transitions.**
  The content manager is disposed when the scene ends. Cache the asset reference instead, not the manager.

- **Do not delete `.meta` files while the project is open.**
  The editor regenerates them with a new GUID, breaking all editor-side prefab references that pointed to the old one.
