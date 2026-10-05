---
title: Voltage Aseprite Integration Architecture
sidebar_position: 8
---

# Voltage Aseprite Integration Architecture

Voltage Bridge connects Voltage Editor to live Aseprite documents through the editor gateway.
Bundled third-party code is MIT-licensed. Its required copyright and permission notice is retained in
`Voltage.Editor/Aseprite/Resources/LICENSE`, shipped with the editor and installed with the extension.

## Execution path

Voltage ships a single Lua extension (`plugin.lua`), its manifest, a complete tool-schema catalogue,
pixel-art guidance and the license notice. The manifest executes the Lua file inside Aseprite.
The editor owns the C# transport and exposes commands through its existing CLI and MCP server;
there is no separate Node server to install or run.

The bridge forwards JSON-RPC requests over an authenticated loopback WebSocket and converts preview
responses into native MCP image blocks. It uses unique request IDs, 60-second deadlines, a 5-second
heartbeat and a persistent local port. The extension uses a 3-second
health timer, stale-session detection and generation guards against obsolete socket callbacks.
It dispatches handlers synchronously in Aseprite's Lua environment; expensive pixel loops can block
Aseprite while running. Most mutations use named Aseprite transactions.

Voltage exposes **123 Aseprite tools across 16 categories**, plus six bridge and project commands.
The Lua extension has 122 handler registrations plus a separately handled `get_server_info` command.
The catalogue preserves nested fields, enum values and required parameters.
The [complete catalogue](aseprite-tool-catalogue.md) lists every command.

| Module | Tools | Implementation |
|---|---:|---|
| Sprite | 12 | Live document creation, opening, copy/save-as, switching, sizing, grid and rendered PNG |
| Drawing | 13 | Cel-local images mapped to sprite coordinates, growing cels, tools, pixel read/write, fill and outlines |
| Layers | 10 | Stack/group operations, visibility, opacity, blend modes, duplication and merge |
| Frames | 8 | Creation, duplication, active frame, durations, deletion and reversal |
| Cels | 5 | Inspection, position, opacity, clear and linking/content-copy fallback |
| Selection | 7 | Mask operations, shape/color selection and combination modes |
| Palette | 12 | Palette colors, resizing, file IO, generation, sorting and image import |
| Tags | 6 | Tag ranges, naming, colors, repeat and direction |
| Slices | 5 | Named rectangles, 9-patch center and pivot |
| Tilemaps | 5 | Native tilemap layer/tileset/cel creation and grid cell access |
| Export | 6 | PNG, sprite sheets, GIF, tilesets, per-layer and per-tag output |
| Godot | 8 | Sheets, metadata, SpriteFrames, AtlasTexture, 9-patch and TileSet resource text |
| Analysis | 6 | Color usage, frame/screenshot comparison, bounds, unused colors and tag validation |
| Editor | 4 | Raw Lua execution, undo, redo and version information |
| Templates | 2 | Layer/tag character scaffold and tileset/grid scaffold |
| Advanced | 14 | Color interpolation, symmetry, ramps, strip preview, transforms, dithering, references, Godot AnimationLibrary, autotile metadata, palettes and inter-document copying |

## Semantics that need care

- `interpolate_frames` blends RGBA values at fixed pixel positions. Its top-level layer traversal skips
  nested groups. It cannot produce hand-drawn in-between poses from semantic movement.
- `get_animation_preview` renders a horizontal PNG strip with timing metadata, rather than animated
  playback. `get_sprite_screenshot` renders the sprite only.
- `link_cels` first tries Aseprite's timeline-range LinkCels command. If the range cannot be established,
  it reports `content_copied`; future edits are not necessarily shared.
- Many tools operate on the active sprite/layer/frame. Concurrent clients or a human changing tabs can
  affect the target. Voltage adds an optional stable document ID to each exposed tool.
- Tool schemas often use unrestricted numbers rather than integers. Inherited handlers vary in their
  coercion and bounds checking. Very large canvases and pixel loops remain expensive; use small pixel
  art canvases and batched pixel arrays. Raw Lua is not a sandbox.
- Godot tools generate files; Voltage does not load Godot or verify its resource parser. They remain
  available for callers that need those exports, while Voltage uses native `.aseprite` import.

## Implementation guarantees

1. An editor-owned C# service reuses Voltage's gateway.
   There are no new runtime packages. Full schemas extend the existing gateway/MCP schema path without
   changing older commands' schema behavior.
2. Added a persistent per-installation credential and fixed listener port. The installed Lua client uses
   the authenticated URL; it does not hunt other applications' ports. Authentication secrets are excluded
   from connection logs and gateway status. Browser-origin requests are refused.
3. Added asynchronous request correlation, bounded messages/pending calls, disconnect error propagation,
   explicit uncertain-outcome timeout reporting and editor shutdown cleanup. Requests are not retried.
4. Added stable sprite targeting, connection events, native MCP image responses, editor controls and a
   save-copy/import workflow that runs asset/scene operations on the editor thread.
5. `export_sprite_sheet` supports `json_path`. Voltage writes frame
   rectangles, durations and tag metadata, checks write failures, creates directories, and rejects unknown
   tags and invalid column/padding values.
6. Explicit-frame sprite screenshots validate the frame and leave the active frame unchanged.
7. Inter-document copy responses report the source and target filenames for stable-ID operations.
8. The extension installs under `voltage-aseprite`, leaving other extensions intact.
   Mutating/file-writing commands are marked unsafe for Voltage's safety profile.

## Verification boundary

Transport/schema checks cover authenticated and rejected connections, nested-array validation,
out-of-order responses, remote errors and timeouts. Editor tests cover catalogue registration,
annotations, extension generation, gateway/WebSocket forwarding and native MCP image responses. Native Aseprite smoke checks cover
pixel readback, cel growth, masks, shapes, layers, palettes, frame durations, tags, analysis, previews,
undo/redo, tilemaps, slices, templates, transforms, interpolation, dithering, saved sprites, PNG/sheet
metadata and Godot file generation. These checks exercise representative tools in every category,
not every parameter combination of all 123 tools. GUI connection permissions, all reconnect scenarios,
and Godot parser compatibility require separate live verification.
