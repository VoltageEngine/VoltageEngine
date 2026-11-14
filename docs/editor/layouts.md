---
title: Editor Layouts
sidebar_position: 9
---

# Editor Layouts

The editor saves your working layout when it closes: docked windows, tab order, the active tab of each dock, and the position and size of floating windows. The next launch restores it. Window visibility is stored separately, so a closed window stays closed, and reopening it from **View > Window** puts it back where it was. Switching scenes does not change the layout.

## View > Layout

The menu shows the current layout's name at the top.

| Item | What it does |
|---|---|
| **Save Layout As** | Saves the current arrangement as a named layout. |
| **Load Layout** | Lists **Default** and every saved layout. Right-click a saved layout to rename or delete it. |
| **Rename Layout**, **Delete Layout** | The same actions as submenus. **Default** cannot be renamed, deleted or overwritten. |
| **Reset to Default** / **Switch to Default** | Loads the layout that ships with the editor. The label reads **Switch to Default** when another layout is active. |

Loading a layout replaces the working layout. A named layout is a snapshot: later changes to the workspace are kept in the working layout, but the named file only changes when you save it again under the same name. Some windows may need an editor restart to pick up a loaded layout fully.

## Where layouts are stored

Layouts and editor settings live in your user profile, never in a game project, so changing a layout does not modify scenes or project files.

| OS | Folder |
|---|---|
| Windows | `%APPDATA%\VoltageEngine\Editor` |
| macOS | `~/Library/Application Support/VoltageEngine/Editor` |
| Linux | `$XDG_CONFIG_HOME/VoltageEngine/Editor` (default `~/.config/VoltageEngine/Editor`) |

Inside that folder, `Layouts/imgui_layout.ini` is the working layout and each named layout is `Layouts/<name>.ini`. Settings, including the theme and last selected layout, are in `Settings.json`. Setting the `VOLTAGE_EDITOR_DATA` environment variable to a folder runs the editor with a separate profile under `<folder>/Data`.

## Recovery

- Each save on exit keeps the previous working layout as `imgui_layout.ini.bak` when the two differ. Copy it over `imgui_layout.ini` while the editor is closed to go back one session.
- When an editor update ships a new default layout, the old working layout is backed up as `imgui_layout.ini.v<N>.bak` and replaced with the new default once.
- If the workspace becomes unusable, choose **View > Layout > Reset to Default**.
