---
title: Live Aseprite Integration
sidebar_position: 9
---

# Live Aseprite Integration

Voltage Editor includes Voltage Bridge, its live Aseprite integration. The editor
hosts an authenticated WebSocket connection; its Lua extension edits documents inside running Aseprite,
including unsaved documents. The existing `voltage` CLI and MCP server expose 123 Aseprite commands
under `aseprite.*`, together with six connection, installation, guidance and project commands. No Node.js
or Python installation is required. This feature is editor-only and adds nothing to published games.

## Setup

1. Open **View > Window > Aseprite** in Voltage.
2. Press **Install or update extension**. The editor installs **Voltage Bridge** in Aseprite's user
   extensions folder and starts its loopback listener.
3. Restart Aseprite, or enable the extension in **Edit > Preferences > Extensions**.
4. Allow the extension's WebSocket and file access when Aseprite asks.
5. Press **Refresh open sprites** in Voltage to check the connection.

The equivalent CLI commands are `voltage aseprite.install`, `voltage aseprite.status`,
`voltage aseprite.connect` and `voltage aseprite.disconnect`. An installed extension causes the bridge
to start with the editor, unless `--gateway-safe` is enabled. The extension reconnects after editor
restarts. If its saved port is occupied, Voltage reports the failure rather than connecting to a
different server. Stop the conflicting listener and reconnect.

Voltage uses its own extension name, command IDs, port and authentication token. Other installed
Aseprite extensions are left intact. Using multiple bridges to edit the same document simultaneously can
change the active document or frame between commands; prefer one owner during an editing sequence.

`VOLTAGE_ASEPRITE_EXTENSION` can override the installation directory for custom Aseprite installations
and isolated tests. Normal paths are `%APPDATA%/Aseprite/extensions/voltage-aseprite` on Windows,
`~/Library/Application Support/Aseprite/extensions/voltage-aseprite` on macOS, and
`$XDG_CONFIG_HOME/Aseprite/extensions/voltage-aseprite` (default `~/.config`) on Linux. The user-level
`AsepriteBridge.json` next to Voltage's settings stores the local port and credential; do not share it.

## Working with sprites

```text
voltage aseprite.list_open_sprites
voltage aseprite.get_sprite_info voltage_sprite_id=12
voltage aseprite.put_pixel voltage_sprite_id=12 x=2 y=3 color=#FF8800
voltage aseprite.get_sprite_screenshot voltage_sprite_id=12 frame=1
voltage aseprite.undo voltage_sprite_id=12
voltage aseprite.save_to_project voltage_sprite_id=12 path=Characters/NewCharacter.aseprite entity=true x=100 y=80
```

All Aseprite tools accept the optional `voltage_sprite_id` obtained from `list_open_sprites`. It targets
that document for the operation and restores the previously active document afterwards if it remains
open. Without it the command uses Aseprite's current active document. IDs last only for the current
Aseprite session. For sequences that change selection or active frames, use stable IDs consistently.

MCP tool names replace dots with underscores, for example `aseprite_put_pixels`. Full nested schemas
preserve pixel arrays, animation templates, required fields and enums. Sprite screenshots and animation
strips arrive as native MCP image content. Other inspection data arrives as JSON. `aseprite.guide`
provides the bundled pixel-art workflow guidance.

`save_to_project` saves a copy under the current project's `Content` folder, refreshes the asset
database and preserves an existing `.meta` GUID. `entity=true` additionally creates an animated sprite
using the normal Aseprite drop handler; this requires Edit mode and is undoable in Voltage. Set
`overwrite=true` explicitly to replace an existing file. The original Aseprite document keeps its
filename. The operation checks that the project has not changed while saving before importing it,
and checks the scene again before creating an entity.
Scene saving remains a separate action in Edit mode.

## Behavior and limits

- Aseprite owns art-edit undo; Voltage owns scene-edit undo. They are separate histories.
- Requests complete asynchronously without blocking Voltage's render loop. The bridge accepts up to
  32 pending requests, limits each message to 3 MiB, and uses a 60-second deadline.
- A timeout or disconnect does not prove an edit was cancelled. Inspect the document before retrying;
  requests are never replayed automatically.
- The listener binds to IPv4 loopback, requires the installation credential and rejects browser Origin
  headers. Aseprite's exact `ws://127.0.0.1:<bridge port>` Origin is accepted.
  `--gateway-safe` refuses mutation, installation and arbitrary Lua commands.
- `execute_script` has the extension's full Lua authority. It is an explicitly unsafe gateway command.
- Frame interpolation blends colors; it does not infer motion. Animation preview returns a frame strip.
  Screenshot returns rendered sprite content, not Aseprite's entire window.
- Some inherited operations iterate top-level layers only, and cel linking has a content-copy fallback.
  See the [package audit](../engine/aseprite-package-audit.md) for inherited limitations and local fixes.

## Verification and maintenance

```text
dotnet run --project tools/aseprite/BridgeChecks/BridgeChecks.csproj
powershell -File tools/aseprite/run-smoke.ps1 -AsepritePath <aseprite executable>
dotnet test Voltage.Editor.Tests -c Editor-Debug --filter FullyQualifiedName~AsepriteGatewayTests
```

Bridge checks exercise authentication, schemas, response matching, remote errors and timeouts. The Lua
smoke checks use temporary sprites in Aseprite batch mode, with pixel and export readback. Editor tests
use a throwaway project and extension installation and emulate the Aseprite WebSocket peer. They do not
replace testing the real GUI extension's initial permissions and reconnection.

Re-extract schemas from an updated upstream package with
`node tools/aseprite/extract-tools.cjs <server/build/tools> Voltage.Editor/Aseprite/Resources/tools.json`.
The extractor reads registration modules without starting their server. Updating schemas does not update
Lua implementations: review and update both together, preserving the upstream license.
