---
title: Projects and Folder Layout
sidebar_position: 1
---

# Projects and Folder Layout

The editor works with **Voltage projects** — a directory containing a `.voltage` metadata file alongside the game C# project. Open a project from `File > Load Project…` or create one with `File > New Project`.

A freshly created project has this layout (relative paths are stored in `ProjectMetadata`):

```
MyGame/
  MyGame.csproj              # game script project
  MyGame.voltage             # project metadata (JSON)
  ProjectSettings.json       # display resolution, vsync, startup scene
  Content/                   # textures, audio, tiled maps, fonts
  Scripts/                   # C# source files hot-reloaded by the editor
  Effects/                   # custom GLSL/HLSL shader source files
  Data/                      # data assets
  Scenes/                    # default destination for .vscene files
  Prefabs/                   # default destination for .vprefab files
  Build/                     # output of the Build system (generated)
```

The paths `ContentsFolder`, `ScriptsFolder`, `EffectsFolder`, `DataFolder`, `ScenesFolder`, and `PrefabsFolder` are all properties on `RuntimeGameProject` and are resolved from the metadata at load time.

`Scenes/` and `Prefabs/` are creation defaults, not required locations. Put `.vscene` and `.vprefab` files anywhere inside the project, including folders organized by level or feature. The Asset Browser discovers them by asset type. Use the editor to move files so their `.meta` GUIDs and references travel with them. Older projects can keep `Data/Scenes/` and `Data/Prefabs/`; no manual migration is required. See [Scenes and Prefabs](scenes-and-prefabs.md) for creation, loading, and references.

`ProjectSettings.json` controls the startup scene name, target design resolution, and display settings applied on the first `Scene.LoadLevel` call.
