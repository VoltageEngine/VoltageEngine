---
title: Hot-Reload Script Workflow
sidebar_position: 6
---

# Hot-Reload Script Workflow

The editor compiles the project's C# scripts itself, with no IDE build step. Save a `.cs` file and the editor recompiles, swaps in the new assembly, and reloads the scene so the change takes effect.

## What is watched and compiled

- **Watched**: every `*.cs` file in the project's Scripts folder, subfolders included, plus the source folders of plugins added as live-edit (local source checkout) plugins. Created, changed, deleted and renamed files all count.
- **Compiled**: all script files in the Scripts folder and the source of every restored plugin, as one in-memory assembly. Files under `bin` and `obj` folders are left out.
- **Debounce**: changes are collected until 500 ms pass without another save, then compiled once.

Scripts also compile once whenever a project opens, unless **Compile Scripts on Startup** is off in File > Settings > Scripts. With it off, script components show as missing until you choose **Compile & Reload Scene**.

## What happens after a compile

After a successful compile, new component types appear in **Add Component**. In Edit mode the scene may then reload, depending on what started the compile:

- **Saving a script** reloads the scene when **Auto Reload Scene** is on.
- **Compile Scripts** and the compile at project open reload it when **Reload The Scene On Script Compilation End** is on in File > Settings > Scripts.
- **Compile & Reload Scene** always reloads it.

The reload reads the scene from its file. Save scene edits before saving a script, or turn **Auto Reload Scene** off, otherwise unsaved scene changes are lost.

When a compile fails, each error is written to the [Debug Log](windows.md) and to the compile log in the Scripting window. The previously compiled scripts stay loaded.

## Changes during Play

A script saved while the game is in Play or Pause mode is not compiled immediately. The Debug Log shows "Script changes queued. Compilation will run after Play stops." After you stop, and once the scene has finished reloading, the queued compile runs and, if it succeeds, the scene reloads again. A **Compile Scripts** request made during Play is queued the same way.

## Menu and window

The **Scripting** menu shows the active project and contains:

| Item | What it does |
|---|---|
| **Scripting Window** | Opens the Scripting window: the hot reload switches, the compile actions as buttons, the number of loaded component and entity types, and the compile log. |
| **Compile Scripts** | Compiles now, then reloads the scene on success when **Reload The Scene On Script Compilation End** is on. |
| **Compile & Reload Scene** | Compiles, then reloads the scene on success, whatever the reload settings say. |
| **Reload Scene** (`F6`) | Reloads the scene from its file without compiling. |
| **Open Scripts Folder** | Opens the Scripts folder in your file manager. |
| **Enable Hot Reload** | Turns compile-on-save on or off. With it off, changes are logged and nothing compiles until you choose Compile Scripts. |

## Settings

All of these are per-user editor settings, not project settings.

| Setting | Where | Default | Effect |
|---|---|---|---|
| **Enable Hot Reload** | Scripting menu, Scripting window | On | Compile automatically when a watched file changes. |
| **Auto Reload Scene** | Scripting window | On | Reload the scene after a compile started by saving a script (Edit mode only). |
| **Compile Scripts on Startup** | File > Settings > Scripts | On | Compile the scripts when a project opens. |
| **Reload The Scene On Script Compilation End** | File > Settings > Scripts | On | Reload the scene after Compile Scripts and after the compile at project open. |
| **Close Progress Bar When Finished** | File > Settings > Scripts | On | Close the compile progress window 2 seconds after it finishes. |

## Published builds

The in-editor assembly is for editing only. A game build compiles the same script files as part of the game project with `dotnet publish`, where the source generators emit reflection-free serialization code. See [Build Game](game-build.md) and [Serialization and NativeAOT](../engine/serialization-and-aot.md).
