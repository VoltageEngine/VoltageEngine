---
title: Scenes and Prefabs
sidebar_position: 4
---

# Scenes and Prefabs

## Scenes

A scene is a `.vscene` JSON file holding entities (transform, components, hierarchy) and the scene's `SceneComponent` entries. Scenes can live anywhere in the project; new projects use `Scenes/` as the default folder for new ones. The display name comes from `SceneData.Name`. The loading key is the project-relative path without the extension (`Scene.LevelName`), so scenes with the same file name in different folders stay distinct.

| Action | Where |
|---|---|
| Create | **Project > New Scene**. Enter a name and a folder (type it or use **Browse**). **Create and load scene** opens it straight away. |
| Open | **Project > Load Scene > Select File**, or **Project > Load Scene > Recents**, or double-click a `.vscene` in the Asset Browser. Only scenes inside the current project can be loaded. |
| Save | **Project > Save Scene**, the **Save Scene** button at the bottom of the Scene Graph, or `Ctrl+S`. Saving is refused during Play and Pause. |

New Scene and Load Scene are disabled without a project and during Play. If the current scene has unsaved changes, switching scenes asks whether to save first.

In the Asset Browser, drop a folder on **Project root**, or in a gap between top-level folders, to move it out of its parent. Moving the default Scenes or Prefabs folder updates the `.voltage` file, the open scene's save path, the startup scene and the scene build list. Folder contents and their `.meta` identities move together.

### Loading scenes from code

```csharp
Scene.LoadLevel("Level2");
Scene.LoadLevel("Worlds/Act 1/Room2");
Scene.ReloadCurrentLevel(); // for a restart button
```

`LoadLevel` accepts a project-relative path, a path relative to the default scenes folder, or a short name that matches the end of one scene's path. When a short name matches more than one scene, `LoadLevel` throws and the message lists the project-relative paths to use instead. A name that matches nothing throws `FileNotFoundException`.

### Scenes in a build

**Build > Build Game** chooses which scenes ship in Debug and Release builds and which one loads on startup. Rules are saved in `ProjectSettings.json` under `BuildScenes`; a scene without a rule ships in both configurations. The startup scene must be included in the target configuration. Builds keep the project-relative locations of scenes and prefabs, so load scenes with the same keys you use in the editor. A runtime level-select menu can list the packaged scenes with `Voltage.Project.SceneCatalog.Read(Scene.ProjectRoot)`. See [Build Game](game-build.md) for the scene list controls.

## Prefabs

A prefab is a `.vprefab` file capturing one entity with its components and children as a reusable template. Prefabs can live anywhere in the project; `Prefabs/` is the default folder for new ones.

To create one, select the entity and press **Create SerializedPrefab** in the Main Entity Inspector, then name it (the default is `<entity>_Prefab`; **Create** is disabled while a prefab with that name exists). The editor writes the `.vprefab` to the default prefabs folder and adds a linked instance next to the original entity, which is left unchanged. Drag a `.vprefab` from the Asset Browser into the Game view to place more instances.

### Instances and overrides

A prefab instance stores its source prefab's GUID (`OriginalPrefabGuid`) and name (`OriginalPrefabName`). When the scene loads, the prefab is instantiated again and the instance's overrides are applied on top.

Overrides work per component, not per field. When you change a component on an instance, the scene stores that component's full data for the instance; components you remove are recorded as removed. The inspector lists the overridden and removed components of the selected instance:

- **Revert** (or **Restore** for a removed component) returns one component to the prefab's version.
- **Revert All Overrides to Prefab** clears every override on the instance.
- **Apply to Original SerializedPrefab** overwrites the `.vprefab` with the instance, after a confirmation.
- **Apply to SerializedPrefab Copies** pushes the instance's values to the other instances of the same prefab in the scene, after listing the entities it will change.

Transforms are not overrides: each instance saves its own position, rotation and scale in the scene.

The source prefab is found by GUID first, so renaming or moving a `.vprefab` in the editor does not break the scenes that use it. Published builds do the same through the asset manifest (`Data/assets.manifest`) the editor writes. The name is used only when the GUID is missing or cannot be resolved.

### Editing a prefab

Double-click a prefab in the Asset Browser to open it in an isolated edit scene. The Scene Graph shows **Editing Prefab: &lt;name&gt;** with:

- **Save Prefab** (also `Ctrl+S`), which writes the `.vprefab`. The temporary scene is never saved as a scene file.
- **Apply to SerializedPrefab Copies**.
- **&lt; Go Back**, which returns to the previous scene and restores its camera position and zoom, including view changes made after its last save.

The initial view fits the combined visual bounds of the prefab's children, including transformed sprites and painted tilemap cells, with padding. Empty layers, runtime helper entities and light volumes do not enlarge it. A prefab with no visual content falls back to collider bounds, then leaf positions. Very small or empty prefabs use a limited zoom.

### Instantiating from code

```csharp
// From an inspector field: survives renames and moves.
public PrefabReference Enemy;
Entity enemy = Core.Scene.LoadPrefab(Enemy, spawnPosition);

// From a path: project-relative, or an unambiguous short name.
Entity slime = Core.Scene.LoadPrefab("Prefabs/Enemies/Slime.vprefab", spawnPosition);
```

`LoadPrefab` returns `null` and logs the reason when the prefab cannot be found or read. Each instance gets fresh entity IDs, and references inside the prefab are remapped to the new subtree. Entities created by `LoadPrefab` have `Entity.OriginalPrefabGuid == Guid.Empty`; identify them by `OriginalPrefabName`.
