---
title: The Asset Browser and .meta GUIDs
sidebar_position: 5
---

# The Asset Browser and .meta GUIDs

Open the Asset Browser from **View > Window > Asset Browser**.

## What it indexes

The tree shows the project's `Content`, `Data`, `Scripts` and `Effects` folders, plus every folder that holds scenes or prefabs, because `.vscene` and `.vprefab` files are found anywhere in the project. Other files outside those four folders are not indexed.

The browser skips `.meta`, `.xnb`, `.mgcb` and `.manifest` files, hidden dotfiles, OS files such as `Thumbs.db` and `desktop.ini`, and temporary or backup files (`.tmp`, `.bak`, `.swp`, `.log`, editor autosaves and similar). It never descends into `bin`, `obj`, `Build`, `EngineLibs`, `PluginLibs`, `node_modules`, `temp`, `tmp` or any folder whose name starts with a dot.

The toolbar has a search field, **Refresh** (rescans every folder and rebuilds the index), and **Zoom Level**, from 0% (a compact list) to 200% (large thumbnails); `Ctrl` or `Cmd` with the scroll wheel changes it too. **Create Shortcut** on a file or folder pins it to a **Shortcuts** section at the top. Selecting a script or text file shows a preview unless **File > Settings > Interface > Preview scripts and text on selection** is off.

## .meta files and GUIDs

The first time the editor indexes a file it writes a `<file>.meta` sidecar holding a stable GUID. Scenes, prefabs and inspector fields reference assets by that GUID plus a project-relative path, so a rename or move inside the editor does not break them. Scripts get no `.meta` file: their identity lives in source through `[ComponentId]`.

Every refresh writes `Data/assets.manifest`, a map from each GUID to its project-relative path. It ships with the game, and published builds resolve `AssetReference` and `PrefabReference` fields GUID-first through `Voltage.Assets.AssetManifest`, falling back to the stored path when the GUID is unknown. Files changed outside the editor reach the manifest at the next index: when the project opens, on **Refresh**, or when the browser's file watcher sees the change.

Rules for keeping references intact:

- Move and rename assets in the Asset Browser. It moves the `.meta` file with the asset. If you move files in your file manager, move the `.meta` file alongside.
- Commit `.meta` files with their assets.
- Do not delete a `.meta` file. The editor creates a new one with a new GUID, and references to the old GUID fall back to their stored path. An unreadable `.meta` file is regenerated the same way.
- A copied `.meta` file (two files claiming one GUID) is detected: the file the manifest last pointed to keeps the GUID, the copy gets a fresh one, and a warning names both.

## Working with files

| Action | How |
|---|---|
| Select | Click; `Ctrl`/`Cmd`-click adds or removes; `Shift`-click selects a range. |
| Copy, paste, duplicate, delete | Right-click menu, or `Ctrl+C`, `Ctrl+V`, `Ctrl+D` and `Delete` (rebindable in File > Settings > Custom Hotkeys). Pasted and duplicated files get new GUIDs. |
| Rename | Right-click > **Rename**, or click a selected file again after a short pause. |
| Move | Drag files or folders onto a folder. Drop on **Project root**, or in the gap between top-level folders, to move a folder to the top level. Name clashes get a ` (1)` suffix instead of overwriting. |
| Wrap in a folder | Right-click > **Create Parent Folder** puts the selected files of one folder into a new subfolder. |
| New folder or asset | Right-click a folder > **Add Folder** or **Create**. **Create** offers **Tileset**, **Timeline**, your data asset types, and inside `Scripts` a **New Script** submenu (Component, Scene Component, Data Asset, Empty Class). |
| Import | Drop files from your file manager onto the browser. They are copied into the folder under the cursor, or into `Content` otherwise. |
| Reveal | Right-click > **Open In File Explorer**. |

**Delete is permanent.** The confirmation says so: the file and its `.meta` are removed from disk, not moved to the recycle bin. Deleting a folder removes everything inside it. The project root and the `Content`, `Data`, `Scripts` and `Effects` folders cannot be renamed or deleted from the browser.

Double-clicking a file opens it: scenes load, prefabs open in an isolated edit scene, `.vtileset` files open the Tileset Editor, `.vasset` files open the Data Asset window and `.vtimeline` files open the Timeline window. File types added by plugins open in the tool the plugin registers.

Dragging a file into the Game view creates content at the drop point:

| File | Result |
|---|---|
| `.png` | An entity with a sprite renderer. |
| `.aseprite`, `.ase` | An animated sprite (`SpriteAnimator`) with every tag loaded. |
| `.vprefab` | A prefab instance. |
| `.wav`, `.ogg`, `.mp3` | An entity with an `AudioSourceComponent`. |
| `.vtimeline` | An entity with a `TimelineDirector` bound to the timeline. |
| `.vtileset` | A tilemap layer bound to the tileset, with the tile brush armed. |
| `.vscene` | Loads the scene. |

Dragging a file onto a matching field in the inspector assigns it.

## Asset type icons

Textures, shader effects, Tiled maps, tilesets, timelines and data assets have monochrome icons that stay readable in every theme; unknown types use a plain document icon. Scene, prefab, script, Aseprite and audio files keep their own artwork. A plugin file type can supply an `IconPath`; if that icon cannot be loaded, the browser falls back to the icon for the asset kind.

The icon sources are SVG files in `Voltage.Editor/DefaultContent/UI/AssetTypes`, next to the 96 x 96 PNG exports the editor actually loads. After editing an SVG, regenerate its PNG.
