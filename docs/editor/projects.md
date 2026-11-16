---
title: Projects and Folder Layout
sidebar_position: 1
---

# Projects and Folder Layout

A Voltage project is a folder holding a `.voltage` metadata file, the game's C# project, and the asset folders the editor indexes. The editor works on one project at a time.

## Creating and opening

**File > New Project** asks for a project name and a parent folder (default `Documents/VoltageProjects`), plus the starting screen resolution, fullscreen and VSync, master, music and SFX volume, and a version number. **Create Project** writes the project, creates `Scenes/MainScene.vscene`, and opens it. The name must be a valid folder name, and a folder with that name must not already exist. When **Open 'Visual Studio' solution file upon Project Creation** is on in **File > Settings** (the default), the generated `.sln` opens as well.

**File > Load Project** opens a `.voltage` file. **File > Recent Projects** lists the last ten projects and has **Clear Recent Projects**. On startup the editor reopens the last project, then loads any `.voltage` path given on the command line, which is how opening a `.voltage` file from the file explorer works. **Project > Close Project** unloads the project and asks to save unsaved changes first.

The `.voltage` file records the engine version the project targets. On open the editor compares it with its own version, counting a hotfix number as well, so a 0.2.2.1 project is newer than a 0.2.2 editor:

- **Newer project than editor**: a warning is logged. Scenes may contain settings this build does not understand, and saving can drop them. Update the editor before editing.
- **Older or unrecorded**: opening is safe. **Project > Settings > Project Info** offers **Set project to Voltage &lt;version&gt;**, which writes the editor's version into the `.voltage` file. Commit that change so the team moves together. The editor never rewrites the version on its own.

## Folder layout

```
MyGame/
  MyGame.voltage          project metadata (JSON): name, folder paths, engine version
  MyGame.csproj           game project; holds the project version
  MyGame.sln
  Program.cs              entry point: reads ProjectSettings.json, loads the initial scene
  ProjectSettings.json    display, audio, layers, tags, scene build list, asset build
  Properties/             AssemblyInfo.cs
  Content/                textures, Aseprite files, audio, fonts, Tiled maps
    Voltage/              engine effects and the default font, copied by the editor
  Scripts/                C# scripts compiled by the editor (ExampleComponent.cs to start)
  Effects/                shader sources
  Data/                   data assets, and assets.manifest written by the editor
  Scenes/                 default folder for new .vscene files
  Prefabs/                default folder for new .vprefab files
  EngineLibs/             engine assemblies synced from the editor on every open (git-ignored)
  .gitignore
```

The `.voltage` file stores the six folder paths (`ContentsFolder`, `ScriptsFolder`, `EffectsFolder`, `DataFolder`, `ScenesFolder`, `PrefabsFolder`) relative to the project root. Game builds go to `bin/<Debug|Release>/<platform>`; see [Build Game](game-build.md).

`Scenes/` and `Prefabs/` are only the folders new files are created in. Scenes and prefabs can live anywhere in the project, and the Asset Browser finds them wherever they are. Moving either default folder in the Asset Browser updates the `.voltage` file. Move files with the editor rather than the file system so their `.meta` GUIDs travel with them; see [The Asset Browser and .meta GUIDs](asset-browser.md). Older projects that keep scenes and prefabs under `Data/` work without migration.

## Project settings

**Project > Settings** edits `ProjectSettings.json`. **Save** writes the file, **Apply** applies the values to the current session without saving, and **Close** discards unsaved edits.

| Tab | Contents |
|---|---|
| **Project Info** | Name and path (read-only), the project version (written to the `.csproj` and embedded in the built executable), and the engine version check. |
| **Display & Audio** | Screen resolution, fullscreen, VSync, background clear colour, design resolution and resolution policy (including `SmoothPixelPerfect`), **Smooth pixel art** and **Fixed camera frame**, master, music and SFX volume, and the content directory. |
| **Physics Layers** | Named collision layers. New projects start with Default 0, Ground 1, Player 2, Enemy 3 and Projectile 4. |
| **Rendering Layers** | Render-order layers, the **Deferred lighting** checkbox (the default renderer for every scene; reload the scene after changing it) and, when deferred lighting is on, the **Ambient light** colour. Defaults: Lighting -100, BehindAll -99, HideObject -30, Background 0, Entities 1, Foreground 2, InFrontOfAll 30, UIElement 99. |
| **Entity Tags** | Named tags. New projects start with Default 0, Player 1, Enemy 2, Collectible 3 and Environment 4. |
| **Loading Screen** | The small loading label a standalone game shows in the lower right: on or off, label text, font, margin, spark count and speed, and text and spark colours. |

A `ProjectSettings.json` without physics layers or entity tags falls back to Default 0 and Ground 1, and Default 0 and Player 1.

The scene the game loads on startup (`InitialScene`) is chosen in **Build > Build Game** under **Scene to load on startup**.
