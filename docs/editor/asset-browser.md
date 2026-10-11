---
title: The Asset Browser and .meta GUIDs
sidebar_position: 5
---

# The Asset Browser and .meta GUIDs

Every file in a watched asset folder gets a `.meta` sidecar file the first time it is seen by the editor. The sidecar contains a stable `Guid` that survives renames and moves within the project. The editor uses this GUID to resolve prefab references when a `.vprefab` file is renamed or moved.

**The `.meta`/GUID system is editor-time only.** Published games do not read `.meta` files. Asset references in scenes and prefabs are stored as project-relative file paths, and `VoltageContentManager` resolves them against `VoltageContentManager.ContentRoot` at runtime.

Do not delete `.meta` files while the project is open. If a `.meta` file is missing the editor regenerates it with a new GUID, which will break any prefab references that pointed to the old GUID.

## Asset type icons

The browser uses monochrome icons so file types stay readable in every editor theme. Textures, shader effects, Tiled maps, tilesets, timelines, and data assets have distinct icons; unknown file types use a plain document icon. Scene, prefab, script, Aseprite, and audio icons retain their existing artwork. Plugin descriptors can supply an `IconPath`; if it cannot be loaded, the browser falls back to the asset kind icon.

The original vector sources and their 96 x 96 transparent PNG exports are in `Voltage.Editor/DefaultContent/UI/AssetTypes`. Edit the SVG source and regenerate its PNG before committing artwork changes. The editor loads the PNGs directly; no SVG renderer or new runtime dependency is required. For example, a development environment with CairoSVG can export `Shader.svg` using `cairosvg Shader.svg -o Shader.png`.

[Three editor design concepts and a GUI framework assessment](design-concepts/index.html) compare presentation improvements on ImGui with a possible Avalonia or Qt port. The concepts are design sketches, not implemented editor themes. Further visual validation should check small icons at multiple display scales on Linux, Windows, and macOS, and dynamic non-ASCII names when broader font coverage is introduced.
