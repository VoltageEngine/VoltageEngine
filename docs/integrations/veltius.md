---
title: Veltius
sidebar_position: 3
description: See your Veltius production board inside the Voltage Editor, report bugs, and sync dialogue boards and tuned values into the project.
---

# Veltius

Veltius is a production planning app for game teams: boards, tasks and milestones linked to the game's own files. The Veltius plugin (`com.veltius.voltage`) brings that board into the Voltage Editor, so you can see what is planned for the scene you have open without leaving it. It is an editor-only plugin and adds nothing to published games.

## Setup

1. Install the plugin in **Plugins > Plugin Manager** and reload the editor when asked.
2. Open the Veltius desktop app with the project linked to your game folder.
3. In Voltage, open **Veltius > Open** and press **Connect** to pair the editor with Veltius. The editor remembers the pairing, reconnects on later starts while Veltius is running, and **Unpair** forgets it.

The window updates live while Veltius is open. **Refresh** re-reads everything.

## The window

| Tab | Shows |
|---|---|
| **Tasks** | The project's tasks and their status. |
| **Due** | Tasks that are due soon. |
| **This scene** | Everything in Veltius that points at the open scene. |
| **Asset** | Drop an asset from the Asset Browser to see what points at it. |
| **Search** | Find a task, scene, board, character or milestone. |

When Veltius allows it, the plugin can also move tasks between columns and file a bug report with a screenshot.

## Syncing content

- **Dialogue boards to DialogueMaker.** Dialogue written on a Veltius board can be turned into [DialogueMaker](dialoguemaker.md) graphs and string tables, by hand with a preview first, or automatically as the board changes.
- **System maps to data assets.** Values tuned on a Veltius system map can be written into the project's [data assets](../editor/data-assets.md), again with a preview before applying.

The same actions are available to scripts and agents through the plugin's `veltius.*` gateway commands (task moves, bug reports, and dialogue and data previews and applies), next to the rest of the [Editor Gateway](../gateway/README.md).
