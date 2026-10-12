---
title: Main Windows
sidebar_position: 2
---

# Main Windows

| Window | Purpose |
|---|---|
| **Scene Graph** | Entity hierarchy for the active scene. Create, rename, reparent, duplicate, and delete entities. Select an entity to open its inspector. |
| **Inspector** | Edits the selected entity's `Transform` and component fields. Component data is read and written through each component's `Data` property. |
| **Asset Browser** | File tree rooted at `Content/`, `Scripts/`, `Effects/`, `Data/Scenes/`, and `Data/Prefabs/`. Drag a texture onto a `SpriteRenderer` field in the inspector to assign it. Right-click for Copy / Paste / Duplicate / Delete. Keyboard: `Ctrl+C`, `Ctrl+V`, `Ctrl+D`, `Delete`. |
| **Game Viewport** | The live MonoGame render target displayed as an ImGui image. The editor UI is rendered on top; the game content renders into the same window as a passthrough. |
| **Editor Tools bar** | Cursor/zoom mode toggle, audio mute toggle, and the Play / Stop / Pause / Reset button cluster. |

Layouts and theme can be changed from the **View** menu.

The eye button in **Editor Tools** shows or hides debug overlays. Its dropdown has **All** and independent
categories for **Colliders**, **Sprite renderers**, **Tilemaps**, **Lights**, **UI**, **Other components**,
and **Transform gizmos**. Checked categories are visible; **All** shows or hides everything. The eye button
temporarily hides your selected categories and restores them on the next click. Category choices persist
as editor preferences and do not modify scenes. The collider category includes tilemap-generated collision
shapes independently of tilemap bounds. Parent and component settings still apply.

With the Scene Graph focused in Edit mode, **Up/Down** selects the previous or next entity through the
whole hierarchy, including collapsed children. Holding a key repeats. Navigation follows the selected
entity even when the inspector is locked, expands its ancestors, and scrolls the selected row into view.
Text fields keep their own arrow-key input.

In Edit mode, selecting an entity in the Scene Graph centers the camera on the combined bounds of its
renderers, colliders, and descendants. Tilemap layers use their painted cells rather than their shared
origin. Empty layers do not pull a populated group toward the origin; groups without content use their
leaf positions. **Move Camera Here** and gateway `view.focus` use the same content bounds.

The Inspector's **Debug Render Enabled** flag also gates every descendant. Turning a parent back on
restores each child's own debug setting; it does not overwrite those settings or disable gameplay rendering.
