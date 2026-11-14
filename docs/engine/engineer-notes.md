---
title: Engine Engineer Notes
sidebar_position: 2
description: Core lifecycle, resolution policies, the persistence layer, the identity model and adding serializable engine components.
---

# Engine Engineer Notes

Internals for people changing the engine. Game developers using the prebuilt editor do not need them.

## Core lifecycle

`Core` is the MonoGame `Game` subclass; a generated project's `<Project>Game` derives from it. It owns:

- The active `Scene` and a queued next scene. The first assignment to `Core.Scene` starts that scene at once; later assignments queue a swap at the end of `Update`: the old scene's `End()` runs, `CoreEvents.SceneChanged` fires, then the new scene's `Begin()`.
- The global managers, which survive scene changes: coroutines, tweens, timers, audio and the render target. A built game started with `--gateway` also registers the runtime gateway here. Add your own with `Core.RegisterGlobalManager`.
- `Core.Emitter`, an `Emitter<CoreEvents>` for `GraphicsDeviceReset`, `SceneChanged`, `OrientationChanged` and `Exiting`.
- The static events `OnSwitchEditMode`, `OnSwitchPauseMode`, `OnSwitchAudio` and `OnResetScene`.

`Core.IsEditMode` starts `true` in `EDITOR` builds and `false` otherwise; the editor sets it to `false` on Play. In `EDITOR` builds the window title shows FPS and managed memory once a second, and the draw-call count comes from `GraphicsDevice.Metrics`.

## Scene resolution policy

`Scene.SetDesignResolution(width, height, policy, horizontalBleed, verticalBleed)` sets the render target size and how it is scaled to the window. `Scene.SetDefaultDesignResolution` sets the same for scenes created afterwards.

| Policy | Behaviour |
|---|---|
| `None` | The render target matches the window. No scaling. |
| `ExactFit` | Stretched to fill the window. May distort. |
| `NoBorder` | Fills the window without distortion, cropping as needed. |
| `ShowAll` | Fits inside the window without distortion, with borders on two sides. |
| `FixedHeight` / `FixedWidth` | Keeps one axis of the design size and adapts the other to the window's aspect ratio. |
| `BestFit` | Fits the design size, cropping only inside the bleed area. |

`NoBorder`, `ShowAll`, `FixedHeight` and `FixedWidth` each have a `PixelPerfect` variant that limits scaling to whole numbers.

## Persistence layer

Scenes, prefabs, components and assets are serialized with `Voltage.Persistence` (`Voltage.Persistence.Json`), a custom encoder and decoder, not `System.Text.Json` or Newtonsoft. `JsonSettings` controls `PrettyPrint`, `TypeNameHandling` and `PreserveReferencesHandling`.

Scene and prefab files are read through `AotDeserializers.DeserializeSceneData` and `DeserializePrefabData` in every build. Component data inside them goes through `ComponentDataAotDeserializer`, whose readers the generated module initializers register (see [Serialization and NativeAOT](serialization-and-aot.md)).

`ComponentIdRegistry` maps each stable `[ComponentId]` alias to the type that carries it, and back. The generated `ComponentDataAotBootstrap.AutoRegister` fills it, both in a published build and for the editor's in-memory script assemblies, so loading resolves a stored id to the current type and ignores a stale type name. That is why class renames and namespace moves need no fix-up. `ComponentIdStamper` writes the attribute into script source during editor compilation. Scripts therefore need no `.meta` sidecar: code is the one asset that carries its identity in its own source.

`TypeRenameRegistry` maps old type names from the obsolete `[FormerlyKnownAs]` attribute. It is only a fallback for scenes saved before a component had a `[ComponentId]`.

## Identity model

Every reference resolves by a stable id; names are only hints.

- **Entities and transforms** are referenced by `EntityReference.EntityPersistentId`, the entity's `Guid`.
- **Components** in another entity are referenced by `ComponentReference`: the entity's persistent id plus the component's `[ComponentId]`, with `ComponentTypeName` as the fallback. Renaming a component class keeps these references valid.
- **Assets** (prefabs, scenes, textures, data assets) are referenced by their `.meta` GUID. The editor resolves it through `AssetDatabase`; a published build reads the GUID to path map in `Data/assets.manifest` through `Voltage.Assets.AssetManifest`. The editor rewrites that manifest on every asset refresh. Renaming or moving an asset in the editor never changes its GUID.

## Entity instance types

| `InstanceType` | Meaning |
|---|---|
| `NonSerialized` | Created from code. Never saved and not selectable in the editor. |
| `Serialized` | The default for editor entities. Saved in the `.vscene`. |
| `SerializedPrefab` | An instance of a `.vprefab`. Saved as a per-component delta against the prefab. |
| `SceneRequired` | Always present and cannot be deleted. Used for the main camera entity, `Camera (Main)`. |

## Adding a serializable engine component

1. Declare the component `partial`. The generator handles everything else for ordinary fields.
2. When the saved shape needs custom logic (for example `SpriteRenderer`, whose data covers PNG and Aseprite sources), write a `ComponentData` subclass and override `Data` yourself. The generator then skips the class.
3. A hand-written `Data` override is registered automatically by `ComponentDataAotBootstrap`, but its deserializer uses the reflection decoder. Engine data classes survive trimming because `TrimmerRoots.xml` preserves `Voltage.dll` wholesale; a data class outside the engine needs `[DynamicallyAccessedMembers]` or its own trimmer root.
4. Give the class a `[ComponentId]` so scenes keep resolving it after a rename.
