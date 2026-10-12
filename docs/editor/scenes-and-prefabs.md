---
title: Scenes and Prefabs
sidebar_position: 4
---

# Scenes and Prefabs

**Scenes** are `.vscene` files stored in `Data/Scenes/`. Each scene is a JSON document containing entity transforms, component data, and a list of `SceneComponent` entries. The scene's display name comes from `SceneData.Name`; the key used for loading is the path relative to the Scenes folder without extension (`Scene.LevelName`). **Project → Load Scene** mirrors the folder hierarchy, so scenes with the same file name in different folders remain distinct.

Load a scene from code:

```csharp
Scene.LoadLevel("Level2");
Scene.LoadLevel("Act 1/Room2");
```

Reload the current scene (e.g. for a "restart" button):

```csharp
Scene.ReloadCurrentLevel();
```

**Build → Scenes in Build** opens the build scene list. Click a row to select it, Ctrl/Cmd-click to toggle individual rows, or Shift-click to select a range. **Select All** selects every row. **Include selected**, **Exclude selected**, **Debug only**, and **Release only** apply to the selected rows; each row also has independent Debug and Release checkboxes. These settings save to `ProjectSettings.json` under `BuildScenes`. Missing rules and newly created scenes are available in both configurations by default.

The selected startup scene must be included in the target configuration. Export preserves scene subfolders under `Data/Scenes`, removes excluded scenes and their manifest entries, and leaves the source project intact. The list controls packaged scene files; author scene transitions using the same relative keys. Runtime debug scene menus can use `Voltage.Project.SceneCatalog.Read(Scene.ScenesDirectory)` (or the published `Data/Scenes` folder) to browse the same tree of packaged scenes.

**Prefabs** are `.vprefab` files stored in `Data/Prefabs/`. A prefab captures one entity (with its components and child entities) as a reusable template. To create a prefab, right-click an entity in the Scene Graph and choose *Save as Prefab*. To instantiate a prefab from code at runtime:

```csharp
Entity enemy = Core.Scene.LoadPrefab("Enemies/Slime.vprefab", spawnPosition);
```

**Prefab overrides** work at the component level. When a scene entity was created from a prefab, loading the scene re-instantiates the prefab and then overwrites individual component data entries from the scene's delta. There is no field-level override system.

**Important caveats:**
- `Entity.Transform` (position/rotation/scale) is **not** part of the prefab delta. Position is supplied at instantiation via the `position` parameter of `LoadPrefab`.
- A prefab instance in a scene stores both its source prefab's stable GUID (`OriginalPrefabGuid`) and name (`OriginalPrefabName`). At runtime the source prefab is resolved **GUID-first** via the baked **asset manifest** (`Data/assets.manifest`) — so renaming or moving a `.vprefab` does not break the scenes that use it — falling back to name only when the GUID is absent. (`Entity.OriginalPrefabGuid` is still `Guid.Empty` for prefabs instantiated by *path* via `Scene.LoadPrefab`, which carry no GUID context.)
