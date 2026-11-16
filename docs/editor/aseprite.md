---
title: Live Aseprite Integration
sidebar_position: 9
---

# Live Aseprite Integration

Voltage Bridge connects the editor to a running copy of Aseprite. The editor listens for an authenticated WebSocket connection on the local machine, and a Lua extension inside Aseprite carries out the commands, including on documents that have never been saved. The `voltage` CLI and the MCP server expose 132 Aseprite commands under `aseprite.*`, plus six of Voltage's own for connecting, installing, guidance and saving into the project. Nothing else needs installing, and the bridge is editor-only: a published game contains none of it.

## Setup

1. Open **View > Window > Aseprite**.
2. Press **Install or update extension**. Voltage writes the **Voltage Bridge** extension into Aseprite's user extensions folder and starts listening.
3. Restart Aseprite, or enable the extension in **Edit > Preferences > Extensions**.
4. Allow the WebSocket and file access requests that Aseprite shows.
5. Press **Refresh open sprites**. The window lists the documents open in Aseprite.

The window shows whether the bridge is connected, waiting for Aseprite or stopped. **Connect** starts listening without reinstalling and **Disconnect** stops it. Install and Connect turn green briefly when they succeed.

Voltage needs Aseprite 1.3 or newer. It reads the version from Aseprite's default install folder when you install, and from Aseprite itself once connected, and shows a red error for anything older. **Open Voltage Normals in Aseprite** opens the [Voltage Normals](voltage-normals.md) panel in the connected Aseprite; inside Aseprite it is **Edit > Voltage Normals**.

Once the extension is installed, the bridge starts with the editor and the extension reconnects on its own after an editor restart. Aseprite scripts can only open outgoing connections, so the extension is the side that dials in. While the editor is closed it keeps trying quietly every few seconds and never opens Aseprite's console, so Aseprite works as usual; **Help > Voltage Bridge: Status** shows the last connection error.

You only press **Install or update extension** once. Each time the bridge starts, the editor compares the installed extension with the copy bundled with it and rewrites any file that differs, so updating Voltage updates the extension too. Restart Aseprite afterwards to load the new version; the editor log says when it updated anything. It only touches an extension paired with its own connection key, so a test editor with separate data never overwrites yours.

The extension always uses the port saved at install time. If another program holds that port, Voltage reports the error instead of switching ports: free the port and press **Connect**. Under `--gateway-safe` the bridge does not start, the install and connection buttons are disabled, and editing, installation and Lua commands are refused.

| Window | CLI |
|---|---|
| Install or update extension | `voltage aseprite.install` |
| Connect | `voltage aseprite.connect` |
| Disconnect | `voltage aseprite.disconnect` |
| Connection state | `voltage aseprite.status` |

Voltage uses its own extension name, command IDs, port and token, so other Aseprite extensions are left alone. Avoid driving one document from two bridges at once: each can change the active document or frame between the other's commands.

### Install locations

| Platform | Extension folder |
|---|---|
| Windows | `%APPDATA%/Aseprite/extensions/voltage-aseprite` |
| macOS | `~/Library/Application Support/Aseprite/extensions/voltage-aseprite` |
| Linux | `$XDG_CONFIG_HOME/Aseprite/extensions/voltage-aseprite` (default `~/.config`) |

Set `VOLTAGE_ASEPRITE_EXTENSION` to use another folder, for a custom Aseprite install or an isolated test profile. `AsepriteBridge.json` in Voltage's per-user settings folder holds the port and token. Treat it as a credential and do not share it.

## Working with sprites

```text
voltage aseprite.list_open_sprites
voltage aseprite.get_sprite_info voltage_sprite_id=12
voltage aseprite.put_pixel voltage_sprite_id=12 x=2 y=3 color=#FF8800
voltage aseprite.get_sprite_screenshot voltage_sprite_id=12 frame=1
voltage aseprite.undo voltage_sprite_id=12
voltage aseprite.save_to_project voltage_sprite_id=12 path=Characters/NewCharacter.aseprite entity=true x=100 y=80
```

Every Aseprite command accepts an optional `voltage_sprite_id` from `list_open_sprites`. The command runs on that document and then switches back to the previously active one if it is still open. Without an ID, the command uses whatever document is active in Aseprite. IDs last for the current Aseprite session only. Use them throughout any sequence that changes the selection or the active frame.

MCP tool names replace the dot with an underscore, for example `aseprite_put_pixels`. Their schemas keep nested pixel arrays, animation templates, required fields and enums. Sprite screenshots and animation strips come back as MCP images; other results come back as JSON. `aseprite.guide` returns bundled pixel-art workflow guidance.

### Saving into the project

`aseprite.save_to_project` saves a copy of the document under the project's `Content` folder at `path`, refreshes the asset database and keeps any existing `.meta` GUID. The Aseprite document keeps its own filename. An existing file is only replaced with `overwrite=true`.

`entity=true` also creates an animated sprite entity at `x`, `y`, the same way dropping an Aseprite file into the scene does. It needs Edit mode and is undoable. If the project or scene changes while Aseprite is saving, the file is kept but not imported, or not placed. Save the scene yourself afterwards.

## Updating art already in use

| Where the art is used | How it picks up a saved change |
|---|---|
| [Light Preview](light-preview.md) | Reloads on its own when the file changes on disk |
| Tilesets | Press **Sync Changes** in the [Tile Palette](tile-palette.md) or [Tileset Editor](tilesets.md). Saving a `.vtileset` reloads the scene layers that use it |
| Sprites in the open scene | Updated when the scene reloads, for example with **Scripting > Reload Scene** (F6) |

## Behavior and limits

- Aseprite owns the undo history for art edits and Voltage owns the one for scene edits. They are separate.
- Requests run asynchronously and never block Voltage's render loop. Up to 32 requests can be pending, each message is limited to 3 MiB, and each request has a 60-second deadline.
- A timeout or disconnect does not mean an edit was cancelled. Inspect the document before retrying; requests are never replayed automatically.
- The listener binds to IPv4 loopback only, requires the installation token and rejects browser `Origin` headers. Only Aseprite's own `ws://127.0.0.1:<port>` origin is accepted.
- `execute_script` runs arbitrary Lua with the extension's full permissions and is marked unsafe.
- Frame interpolation blends colours; it does not infer motion. Animation preview returns a strip of frames, and a screenshot shows the rendered sprite, not Aseprite's window.
- Some inherited commands only visit top-level layers, and cel linking falls back to copying content. The [package audit](../engine/aseprite-package-audit.md) lists the inherited limitations and local fixes.

## Verification and maintenance

```text
dotnet run --project tools/aseprite/BridgeChecks/BridgeChecks.csproj
powershell -File tools/aseprite/run-smoke.ps1 -AsepritePath <aseprite executable>
dotnet test Voltage.Editor.Tests -c Editor-Debug --filter FullyQualifiedName~AsepriteGatewayTests
```

The bridge checks cover authentication, schemas, response matching, remote errors and timeouts. The Lua smoke run edits temporary sprites in Aseprite's batch mode and reads back pixels and exports. The editor tests use a throwaway project and extension folder and emulate the Aseprite side of the WebSocket. None of them replace a manual check of the real extension's first permission prompt and reconnection.

To take schemas from an updated upstream package, run `node tools/aseprite/extract-tools.cjs <server/build/tools> Voltage.Editor/Aseprite/Resources/tools.json`. The extractor reads the registration modules without starting their server. It updates schemas only: review and update the Lua implementations alongside them, and keep the upstream license.
