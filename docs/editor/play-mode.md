---
title: Play, Pause, Reset, and Audio
sidebar_position: 3
---

# Play, Pause, Reset, and Audio

The playback buttons sit on the Game view's top strip. When **View > Window > Separate Game Window** is off they move into the Editor Tools window instead.

| Button | Default key | What it does |
|---|---|---|
| **Play / Stop** | `F1` | Switches between Edit mode and Play mode (`Core.IsEditMode`). In Play mode, components update. Stopping returns to Edit mode and, while `Core.ResetSceneAutomatically` is on (the default), reloads the scene from its file so changes made during Play are discarded. |
| **Pause** | `F2` | Toggles `Core.IsPauseMode` while playing. Components stop updating unless they implement `IUpdatableInPauseMode`. Disabled in Edit mode. |
| **Reset** (tooltip: Reload Scene) | `F5` | Calls `Core.InvokeResetScene()`: reloads the scene from its file without changing mode. If the scene has unsaved edits, the editor asks whether to save them first. In a prefab edit scene it reloads the prefab instead. |

Other playback shortcuts:

| Action | Default key |
|---|---|
| Reload scene through the script manager (Scripting > Reload Scene); reads the file without asking about unsaved edits | `F6` |
| Save scene, or the prefab in a prefab edit scene | `Ctrl+S` |
| Build and Run | `Ctrl+F5` |
| Undo / Redo | `Ctrl+Z` / `Ctrl+Y` |
| Toggle fullscreen | `F11` on Windows and Linux, `Ctrl+Cmd+F` on macOS |

`Ctrl` shortcuts also accept `Cmd` on macOS. Every shortcut except fullscreen can be rebound in **File > Settings > Custom Hotkeys**.

Scene data cannot be saved in Play or Pause mode: `Ctrl+S` shows a notification and does nothing. Stop first, then save.

## Camera and cursor during Play

During Play the gameplay camera controls the view; the editor does not pull it toward the last editing position. Pausing with the editor's Pause button or `F2` starts from the current gameplay camera position and then allows editor camera navigation (wheel zoom, middle-drag and `W A S D` pan) while paused. Resuming returns the camera to gameplay. See [Main Windows](windows.md#navigating-the-scene).

Pressing `Esc` during Play makes the mouse cursor visible over the whole editor, even if the game hid it. Left-click the game image to give the cursor back to the game.

## Play View

The **Play View** button at the end of the Editor Tools row (also on the right of the Game view's playback strip) chooses where the game shows while playing. The choice is saved with your editor settings.

| Mode | While playing |
|---|---|
| **Custom** | The game runs in the Game window, as you laid it out. |
| **Maximized** | The game view fills the editor window below the menu bar, with the Editor Tools row and playback buttons along its top. Combine with fullscreen for a full-screen view. |
| **Floating** | The game view pops out into a window inside the editor that you can move and resize; its position is remembered. |

Edit mode always uses the docked Game window, and the dock layout underneath is never changed. Play View requires **View > Window > Separate Game Window**. The game cannot open in a separate operating-system window, because the editor runs in a single window.

## Audio

The **audio mute** button in Editor Tools sets `Core.IsAudioOn`. The change raises `Core.OnSwitchAudio` and sets `SoundEffect.MasterVolume` to 0 or 1 as a global backstop. Components that implement `IAudioComponent` and register with `AudioComponentRegistry` (in `OnAddedToEntity`, unregistering in `OnRemovedFromEntity`) receive `OnAudioStateChanged(bool)` so they can stop or resume their own sound instances. Check `Core.IsAudioOn` before starting new sounds so they respect the current state.

## Debug drawing in Play

With **File > Settings > Debug > Disable Debug Drawing (Lines, Shapes, etc.) In Play Mode** on (the default), debug overlays turn off when Play starts and return when it stops.
