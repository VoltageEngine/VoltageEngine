---
title: Play, Pause, Reset, and Audio
sidebar_position: 3
---

# Play, Pause, Reset, and Audio

The Editor Tools bar contains a four-button cluster that controls the game loop:

| Button | Keyboard | What it does |
|---|---|---|
| **Play** | `F1` | Enters Play mode (`Core.IsEditMode = false`). Component `Update` methods start running. |
| **Stop** | `F1` (again) | Returns to Edit mode. If `Core.ResetSceneAutomatically` is true the scene is reloaded from disk, discarding Play-mode changes. |
| **Pause** | `F2` | Toggles `Core.IsPauseMode`. Components that do not implement `IUpdatableInPauseMode` stop updating. Only meaningful while in Play mode. |
| **Reset** | `F5` | Calls `Core.InvokeResetScene()`. Reloads the scene from disk without leaving Play mode. |

Additional shortcuts:

During Play, the gameplay camera controls the view; the editor does not pull it toward an old editing position. Pausing with the editor's Pause button or F2 begins at the current gameplay camera position, then allows editor camera navigation while paused. Resume returns camera control to gameplay. See [Main Windows](windows.md) for viewport and overlay controls.

| Action | Keyboard |
|---|---|
| Reload scene (recompile scripts + reload) | `F6` |
| Save scene | `Ctrl+S` |
| Toggle fullscreen | `F11` |

## Play View

The **Play View** button at the end of the Editor Tools bar (also on the right of the Game view's playback strip) picks where the game shows while playing. The choice is saved with your editor settings.

| Mode | While playing |
|---|---|
| **Custom** | The game runs in the Game panel, as you laid it out. |
| **Maximized** | The game view fills the editor window below the menu bar, with the full Editor Tools row and the playback buttons along its top. Combine with `F11` for a full-screen view. |
| **Floating** | The game view pops out into a window inside the editor that you can move and resize; its position is remembered. |

Stop always returns to the docked Game panel, and the dock layout underneath is never changed. Play View needs **View > Separate Game Window** turned on. The game cannot open in a separate operating-system window: the editor is a single MonoGame window.

The **audio mute** button in the toolbar calls `Core.InvokeSwitchAudio(bool)`, which sets `Core.IsAudioOn`, fires `Core.OnSwitchAudio`, and applies `SoundEffect.MasterVolume` as a global backstop. Components that implement `IAudioComponent` receive an `OnAudioStateChanged` callback.

> Scene data **cannot be saved** while in Play or Pause mode. `Ctrl+S` is ignored and a notification is shown.
