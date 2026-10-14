---
title: Editor Layouts
sidebar_position: 9
---

The editor automatically remembers your current layout when you close it. Dock windows together, reorder their tabs, choose the visible tab, or move and resize a floating window; reopening the editor restores that working layout. Window visibility is remembered separately, so a closed Timeline or Core Settings window stays closed. Reopening a window restores its saved position. Switching scenes does not reset the layout.

Use **View → Layout** to load the built-in Default layout or a saved layout. Loading a layout changes the current workspace. To keep a reusable arrangement, save it under a name. Subsequent automatic saves preserve your working arrangement without overwriting that named snapshot; explicitly save it again to update the snapshot.

Settings and layouts live in the editor's user profile, outside game projects. The automatic layout is `Layouts/imgui_layout.ini`; an exit save keeps the previous differing version in `imgui_layout.ini.bak`. Named `.ini` layouts remain separate. Changing or resetting layouts does not modify scene assets.
