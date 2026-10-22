---
title: Scenes and Prefabs
sidebar_position: 4
---

# Scenes and Prefabs

**Scenes** are `.vscene` files that can live anywhere inside the project. New projects use `Scenes/` as a default creation folder. Each scene is a JSON document containing entity transforms, component data, and a list of `SceneComponent` entries. The scene's display name comes from `SceneData.Name`; its loading key is the project-relative path without extension (`Scene.LevelName`). **Project → Load Scene** opens a file explorer filtered to Voltage scenes. Scenes with the same file name in different folders remain distinct.

**Project → New Scene** immediately opens the creation prompt. Enter a name and choose its destination folder with **Browse** or the folder field. The configured default folder is a starting location; scenes do not need to live there.

In the Asset Browser, drop a folder on **Project root** or in a gap between top-level folders to move it out of its parent. Top-level folders sort alphabetically. Moving a default Scenes or Prefabs folder updates the project descriptor, the open scene save/reload path and affected scene build keys. The folder contents and `.meta` identities travel together.

Load a scene from code:

```csharp
Scene.LoadLevel("Level2");
Scene.LoadLevel("Worlds/Act 1/Room2");
```

Existing short names and paths relative to the configured default scene folder still work. For duplicate names, use the project-relative path to select the intended file.

Reload the current scene (e.g. for a "restart" button):

```csharp
Scene.ReloadCurrentLevel();
```

The **Scenes in Build** list in **Build → Build Game** sets which scenes ship. Click a row to select it, Ctrl/Cmd-click to toggle individual rows, or Shift-click to select a range. Ctrl/Cmd+A selects all rows. A Debug or Release checkbox applies its new value to every selected row. Use Ctrl/Cmd+1 through Ctrl/Cmd+4 for include both, exclude both, Debug only and Release only. See [Build Game](game-build.md) for the complete shortcuts and resizable scene list. Settings save to `ProjectSettings.json` under `BuildScenes`; scenes without an explicit rule are available in both configurations by default.

The selected startup scene must be included in the target configuration. Export preserves the project-relative locations of scenes and prefabs, removes excluded scenes and their manifest entries, and leaves the source project intact. The list controls packaged scene files; author scene transitions using the same relative keys. Runtime debug scene menus can use `Voltage.Project.SceneCatalog.Read(Scene.ProjectRoot)` to browse the same tree of packaged scenes.

**Prefabs** are `.vprefab` files that can also live anywhere inside the project. New projects use `Prefabs/` as the default creation folder. Prefab pickers and GUID lookup use the project-wide asset index. A prefab captures one entity (with its components and child entities) as a reusable template. To create a prefab, right-click an entity in the Scene Graph and choose *Save as Prefab*. To instantiate a prefab from code at runtime:

```csharp
Entity enemy = Core.Scene.LoadPrefab("Actors/Enemies/Slime.vprefab", spawnPosition);
```

**Prefab overrides** work at the component level. When a scene entity was created from a prefab, loading the scene re-instantiates the prefab and then overwrites individual component data entries from the scene's delta. There is no field-level override system.

Double-click a prefab in the Asset Browser to open its isolated edit scene. The initial view centers and fits the combined visual bounds of its children, including transformed sprites and painted tilemap cells, with padding on both axes. Empty layers, runtime helper entities and light volumes do not enlarge this view. A prefab with no visual content uses collider bounds, then leaf positions as a fallback. Tiny and empty prefabs use a limited zoom rather than unlimited magnification.

Use **Go Back** in the Scene Graph to return to the previous scene. Its editor camera position and zoom are restored, including view adjustments made after the last scene save. Opening a prefab changes the temporary editor view; it does not save a new camera setting into the original scene file.

**Important caveats:**

- `Entity.Transform` (position/rotation/scale) is **not** part of the prefab delta. Position is supplied at instantiation via the `position` parameter of `LoadPrefab`.
- A prefab instance in a scene stores both its source prefab's stable GUID (`OriginalPrefabGuid`) and name (`OriginalPrefabName`). At runtime the source prefab is resolved **GUID-first** via the baked **asset manifest** (`Data/assets.manifest`) - so renaming or moving a `.vprefab` does not break the scenes that use it - falling back to name only when the GUID is absent. (`Entity.OriginalPrefabGuid` is still `Guid.Empty` for prefabs instantiated by *path* via `Scene.LoadPrefab`, which carry no GUID context.)
