---
title: Logging, Debug Drawing and the Console
sidebar_position: 2.7
description: Debug log levels, timed debug shapes, assertions, debug overlays and console commands.
---

# Logging, Debug Drawing and the Console

## Logging

```csharp
Debug.Log($"Player spawned at {position}");
Debug.Info("Checkpoint reached");
Debug.Warn("Missing animation 'Hurt'");
Debug.Error($"Save failed: {ex.Message}");
Debug.Success("Level complete");
Debug.WarnIf(health < 0, "Negative health");
```

Pass interpolated strings: the logging methods capture the caller's file and line through optional parameters, so extra format arguments would land in the wrong place. Every entry records its level, time, source file and line. In the editor, entries appear in the **Debug Log** window; a game built in Debug writes them to standard output. The log keeps the latest 500 entries. A message repeated from the same line more than 100 times is collapsed into one warning, so a log call in `Update` cannot flood it. Subscribe to `Debug.OnLogEntry` to forward entries elsewhere, and read `Debug.GetLogEntries()` for a snapshot. The gateway's `log.tail` reads the same log.

`Debug.TimeAction(action, iterations)` measures how long code takes.

## Debug drawing

Debug shapes draw in world space on top of the scene while debug rendering is on. Pass a duration to keep a shape for several seconds, which helps visualise one-off events such as a raycast.

```csharp
Debug.DrawLine(start, hit.Point, Color.Red, 1.5f);
Debug.DrawColliderDelayed(collider, Color.Yellow, 1f);
Debug.DrawCircle(explosionCenter, radius, Color.Orange, 0.5f);
Debug.DrawArrow(origin, origin + velocity, color: Color.Cyan);
Debug.DrawText($"state: {_state}");                 // screen-space text
```

`DrawPixel`, `DrawRect`, `DrawHollowRect`, `DrawHollowBox` and `DrawPolygon` cover other shapes. `Debug.Colors` holds the colours used for collider, renderable and Verlet overlays.

Components draw their own gizmos by overriding `DebugRender(Batcher)`. It runs only while `Core.DebugRenderEnabled` is true, which the editor turns on in Edit mode. The eye button in the Editor Tools bar filters overlays by category (colliders, sprites, tilemaps, lights, UI, other, gizmos), and an entity's **Debug Render Enabled** flag hides overlays for it and its children.

## Assertions

`Insist` states assumptions:

```csharp
Insist.IsNotNull(_target, "Turret {0} has no target", Entity.Name);
Insist.IsTrue(count > 0);
Insist.AreEqual(a, b, "Mismatch");
```

A failing check raises a debug assertion and breaks into an attached debugger. Use it for programmer errors, not for conditions a player can cause.

## The debug console

In the editor, press the tilde key to open the in-game console over the game view. It completes commands with Tab and keeps a history. Built-in commands include:

| Command | Does |
|---|---|
| `help [command]` | Lists commands, or shows one command's usage. |
| `entity-list`, `entity-count` | Lists or counts the scene's entities. |
| `renderable-count`, `renderable-log` | Counts or lists renderables per render layer. |
| `physics` | Reports the number of colliders in the spatial hash. |
| `debug-render` | Toggles debug rendering. |
| `assets [s or g]` | Lists loaded assets in the scene or global content manager. |
| `timescale <value>` | Changes `Time.TimeScale`. |
| `vsync`, `fixed`, `framerate` | Toggle vertical sync and fixed time step, set the target frame rate. |
| `log-drawcalls`, `clear`, `console-scale`, `exit` | Diagnostics and console housekeeping. |

Commands are static methods marked `[Command("name", "help")]` (namespace `Voltage.Console`) with `string`, `int`, `float` or `bool` parameters; default values become optional arguments. The console collects the engine's commands when it starts, and the commands in game scripts after every successful compile, so a renamed or deleted script command disappears with the next compile. A command method that is not static, or has a parameter of another type, is skipped with an error in the Debug Log.

`DebugConsole.BindCommandToFunctionKey(2, "debug-render")` runs a command from a function key. The console exists only in the editor; game builds do not include it. Scripts and agents can run the same commands through the gateway's `console.exec` and list them with `console.commands`.
