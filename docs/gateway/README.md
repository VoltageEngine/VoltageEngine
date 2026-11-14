---
title: Editor Gateway
sidebar_position: 1
description: Drive a running editor or built game over loopback JSON with the voltage CLI, MCP, synthetic input and screenshots.
---
# Editor Gateway

The gateway lets external tools, scripts and AI agents drive a running Voltage Editor, and a built game too. It is a loopback TCP server inside the process. Every request runs on the game thread between frames, so handlers see the same state the UI does and their edits land in the undo history.

The transport and the host-neutral commands (`ping`, `status`, `commands`, `log.*`, `console.*`, `screenshot`, `input.*`, `ui.info`, `events.*`, `wait.event`, `batch`, `loading.*`, `app.exit`) live in the engine (`Voltage.Engine/Gateway`, namespace `Voltage.Gateway`). The editor adds its own commands and events in `Voltage.Editor/Gateway`. A built game gets the engine set only.

## Connecting

The editor starts the gateway on `127.0.0.1:47800` and writes `gateway.json` next to `Settings.json` in the editor data folder (`%APPDATA%\VoltageEngine\Editor` on Windows):

```json
{ "port": 47800, "token": "…", "pid": 12345, "host": "editor", "started": "…", "exe": "…\\Voltage.Editor.exe", "args": [], "logs": "…\\Logs" }
```

The file stays behind when the editor exits: the pid tells clients it is gone, and `exe` is what `voltage start` relaunches.

Editor command-line flags:

- `--gateway-port N` picks another port (`0` means any free port).
- `--no-gateway` turns the gateway off.
- `--gateway-info <path>` writes the discovery file elsewhere; pair it with the CLI's `--config`.
- `--gateway-safe` starts the safety profile described under [Security](#security).

The protocol is newline-delimited UTF-8 JSON, and a request over 4 MB closes the connection. A client's first line must be `{"auth":"<token>"}`. The server answers with a `hello` event carrying `editor`, `host`, `pid` and `clientId`, or with `{"event":"error","data":"authentication failed"}` and closes. After that:

```
→ {"id": 1, "method": "entity.create", "params": {"name": "Player", "x": 100}}
← {"id": 1, "ok": true, "result": {…}}
← {"id": 2, "ok": false, "error": "entity not found: Foo"}
← {"event": "log", "data": {"type": "Warn", "message": "…", "time": "…", "caller": "Scene", "line": 42}}
```

`id` is echoed back verbatim and may be any JSON value. A request without an id still runs but gets no reply. Events go only to clients that asked for them (`log.subscribe`, `events.subscribe`).

While at least one client is connected, the editor keeps updating and drawing when it loses focus, so an agent can work while the user is in another window. A minimized window still updates but skips drawing.

## CLI

`Voltage.Cli` builds `voltage`, a thin client that reads `gateway.json` for you:

```
voltage status
voltage doctor [--json]           # gateway.json, process, port, build age, SDK, .mcp.json, as OK/WARN/FAIL lines
voltage help                      # every method with its flags; voltage help entity.set lists its parameters
voltage entity.list --table       # lists of objects as an aligned table
voltage status --field scene.name # one value out of the result
voltage entity.set entity=Player x=0 y=0 rotation=90
voltage scripts.compile reloadScene=true
voltage logs --follow --level Error --grep "Gateway|Exception" --since 60 --count 200
voltage watch --logs --filter scene.*   # lifecycle events (and log lines) as they happen; --json prints raw lines
voltage run steps.json            # replay a recorded {"steps": [...]} script, or a list of {method, params} calls
voltage json '{"method":"status"}' # send one raw request
voltage pipe                      # one JSON request per stdin line, one response per stdout line
voltage start [project.voltage] --headless --no-prompts   # relaunch the last editor and wait for its gateway
voltage stop                      # ask it to quit and wait until the process is gone
voltage restart --rebuild         # stop, dotnet build the editor project next to its exe, start again
voltage --version                 # CLI version plus the running host's engine version
```

Global options:

- `--timeout <sec>` applies to every request (default 30).
- `--config <path>` reads another discovery file, such as one an editor started with `--gateway-info` wrote.
- `--port <n> --token <t>` connects without a discovery file.
- `--compact` prints one line; `--table` formats lists of flat objects or objects of scalars; `--field a.b.0` prints one value (strings raw, everything else as JSON).

Values are parsed as JSON when they look like it (`true`, `12.5`, `[1,2]`, `{"x":1}`) and as strings otherwise.

`start` and `restart` accept `--headless`, `--safe`, `--no-prompts` and `--gateway-port N` and pass them to the editor (`--safe` becomes `--gateway-safe`), replacing any recorded copy of the same flag. With `--config` they also pass `--gateway-info`, so the new instance writes its discovery file where this CLI reads it. `--exe <path>` tells `start` where the editor is the first time, and `--wait` sets how long to wait for the gateway (default 120 seconds). `stop` sends `editor.exit force=true` (or `app.exit` with `--game`) and fails if the process is still alive after `--wait` seconds (default 15). An instance started with `--gateway-safe` refuses exits, so stop that one from its window. `restart --rebuild` finds `Voltage.Editor.csproj` from the recorded `bin/<Configuration>/<rid>/Voltage.Editor.exe` path, builds that configuration after the old instance is gone, and only then relaunches: the edit, rebuild, relaunch loop for work on the engine itself.

Headless `start` runs a private copy of the editor binaries, so Visual Studio and build scripts can rebuild the normal output while the hidden editor runs. Its discovery file records the running copy in `exe` and the original build in `sourceExe`; `restart` uses the original, and `stop` removes the private copy after exit. For unattended work alongside your own editor, use a separate `--config` discovery file and `VOLTAGE_EDITOR_DATA` profile. Launching the executable directly with `--headless` makes no private copy.

`voltage record steps.json` records the mouse, keyboard and typed text until you press Enter (`--no-mouse`, `--no-keyboard` and `--no-text` leave one out) and writes an `input.script` file: `move`, `down`, `up`, `scroll`, `keyDown`, `keyUp`, `type` and the `wait` frames between them. It samples the input the editor actually saw, physical or synthetic. The underlying command is `input.record action=start|stop|status`. A recording outlives the connection that started it, so you can start it, work by hand, and stop it from a later call. It is capped at 100,000 steps and ends with the editor.

`voltage run` takes either an `input.script` payload (`{"steps": [...]}`) or a JSON array of `{"method": ..., "params": {...}}` calls, which run one by one and stop at the first failure (`--continue` keeps going, `--batch` sends them as one `batch` request).

`voltage completion bash|zsh|pwsh` prints a completion script; each script's header says where to install it. Subcommands and options complete offline. Method names and `key=` parameter names come from the running editor's `commands` and are cached in `gateway-commands.txt` beside `gateway.json`, so they still complete when the editor is down. The scripts complete a command named `voltage`, so put a `voltage` shim or the published executable on your PATH. A script generated with `--config` calls back with the same file.

## MCP

`voltage mcp` is a Model Context Protocol server over stdio. Every gateway method becomes a tool with dots replaced by underscores (`entity_create`). A command registered with a `P.*` parameter table gets a typed input schema (types, enums, defaults, required list, `additionalProperties: false`); one without it gets properties parsed from the `params:` part of its help.

Command flags become MCP annotations: `readOnlyHint` and `idempotentHint` follow `readOnly`, `destructiveHint` is set for `destructive` or `unsafe` commands, and `openWorldHint` is always false. Unsafe tools also say so in their description.

`screenshot`, and any `aseprite.*` result that contains an image, comes back as image content so a multimodal agent can look at it. Text results are cut at 50,000 characters (200,000 for `tilemap.context`).

The server also serves resources: `voltage://screenshot/latest` (the last capture taken through this server, image/png), `voltage://editor/status`, `voltage://editor/commands`, `voltage://editor/scene` (JSON) and `voltage://editor/log` (the last 200 lines). It subscribes to editor events and sends `notifications/tools/list_changed` after `scripts.compiled` and `project.loaded`, so clients refresh their tool list when the command set may have changed. The tool list is cached in `gateway-tools.json`, so it exists before the editor is up. The connection is made lazily and remade if the editor restarts.

```
claude mcp add voltage -- C:\path\to\voltage.exe mcp
```

In this repository, Claude Code picks the server up from `.mcp.json`, which runs the `Voltage.Cli/bin/mcp` copy of the CLI through `dotnet`. Building the CLI refreshes that copy, so a running MCP server never locks the build output; a server started before a rebuild keeps the old copy until it restarts.

## Built games

A built game listens only when started with `--gateway`, or with `VOLTAGE_GATEWAY=1` in its environment. It answers the same protocol with the engine command set. `--gateway-port N`, `--gateway-info <path>` and `--gateway-safe` work as in the editor.

The game writes its discovery file to `<user app data>/VoltageEngine/Runtime/gateway.json` (`%APPDATA%\VoltageEngine\Runtime` on Windows) unless `--gateway-info` says otherwise. The file has `"host": "game"` and the game's entry assembly name under `"game"`; `status.host` and `status.game` report the same. Screenshots default to the `Screenshots` folder next to the file.

```
voltage build.run gateway=true            # launch the last build listening; answers {pid, port, gatewayInfo} once it does
voltage start --game path\to\Game.exe     # or launch any built game directly
voltage --game status                     # every command takes --game (or VOLTAGE_TARGET=game) to target the runtime file
voltage --game screenshot scale=0.5
voltage --game input.key key=space
voltage --game app.exit
```

`voltage start --game` ignores the editor launch flags and always starts the game with `--gateway --gateway-port 0 --gateway-info <file>`, so the game gets a free port. `voltage doctor` reports both discovery files.

The gateway serializes its answers with reflection, which a trimmed NativeAOT publish may strip, so `--gateway` is meant for debug and test builds (`build.run debug=true` launches those). A project with `PublishAot` still works in its plain `dotnet build` output because the engine names its resolver explicitly.

## Methods

The editor also exposes the 132 live `aseprite.*` tools and six helpers (`aseprite.status`, `aseprite.connect`, `aseprite.disconnect`, `aseprite.install`, `aseprite.guide`, `aseprite.save_to_project`) through the same gateway and MCP. See [Live Aseprite Integration](../editor/aseprite.md).

| Group | Methods |
|---|---|
| Editor | `ping`, `commands`, `status`, `editor.exit`, `app.exit`, `undo`, `redo`, `undo.history`, `window.list`, `window.show` |
| Console and log | `console.exec`, `console.commands`, `log.tail`, `log.subscribe`, `log.unsubscribe`, `log.clear` |
| Project and scene | `project.info`, `project.recent`, `project.load`, `project.create`, `scene.list`, `scene.info`, `scene.load`, `scene.save`, `scene.reload`, `scene.create` |
| Entities | `entity.list`, `entity.get`, `entity.create`, `entity.duplicate`, `entity.delete`, `entity.set`, `entity.reorder`, `entity.select`, `entity.deselect`, `entity.selected` |
| Components | `component.types`, `component.add`, `component.remove`, `component.get`, `component.set` |
| Scene components | `scenecomponent.types`, `scenecomponent.list`, `scenecomponent.add`, `scenecomponent.remove`, `scenecomponent.get`, `scenecomponent.set` |
| Play and scripts | `play.state`, `play.start`, `play.stop`, `play.pause`, `play.reset`, `scripts.compile`, `scripts.types`, `screenshot` |
| Script files | `script.templates`, `script.list`, `script.read`, `script.create`, `script.write`, `script.delete` |
| Input and window | `input.state`, `input.move`, `input.click`, `input.down`, `input.up`, `input.drag`, `input.hover`, `input.scroll`, `input.key`, `input.type`, `input.wait`, `input.script`, `input.record`, `input.release`, `hotkey.list`, `hotkey.press`, `ui.info`, `ui.resize`, `ui.maximize` |
| Widgets by label | `ui.windows`, `ui.popups`, `ui.tree`, `ui.find`, `ui.click`, `ui.hover`, `ui.focus` |
| Prompts and setup | `plugin.list`, `plugin.restore`, `effects.status`, `effects.compile`, `effects.setup` |
| Data assets | `data.types`, `data.get`, `data.set`, `data.create` |
| View | `view.get`, `view.set`, `view.focus` |
| Assets and builds | `asset.list`, `asset.get`, `asset.refresh`, `asset.kinds`, `asset.drop`, `asset.import`, `asset.importAseprite`, `asset.meta`, `build.platforms`, `build.game`, `build.run` |
| Asset build | `assetbuild.settings`, `assetbuild.rules`, `assetbuild.rule.set`, `assetbuild.rule.remove`, `assetbuild.scaffold`, `assetbuild.run`, `assetbuild.status`, `assetbuild.clean` (see [Asset Build](../editor/asset-build.md)) |
| Prefabs | `prefab.list`, `prefab.info`, `prefab.create`, `prefab.link`, `prefab.instantiate`, `prefab.apply`, `prefab.revert`, `prefab.open`, `prefab.close` |
| Lighting | `lighting.list_light_types`, `lighting.preview_sprite`, `lighting.show` |
| Tilemaps | `tilemap.list`, `tilemap.info`, `tilemap.context`, `tilemap.create`, `tilemap.get`, `tilemap.paint`, `tilemap.erase`, `tilemap.fill`, `tilemap.collision`, `tilemap.generateCollision`, `tileset.list`, `tileset.info`, `tileset.create` |
| Timelines and animation | `timeline.list`, `timeline.create`, `timeline.info`, `timeline.set`, `timeline.track.types`, `timeline.track.add`, `timeline.track.set`, `timeline.track.remove`, `timeline.clip.add`, `timeline.clip.set`, `timeline.clip.remove`, `timeline.key.add`, `timeline.key.remove`, `timeline.marker.list`, `timeline.events`, `timeline.properties`, `timeline.eases`, `timeline.validate`, `timeline.bind`, `timeline.unbind`, `timeline.director`, `timeline.director.state`, `timeline.editor.state`, `animation.list`, `animation.play`, `animation.stop` |
| Audio | `audio.zones`, `audio.zone.types`, `audio.zone.create`, `audio.mixer`, `audio.mixer.set`, `audio.state` |
| Scene loading | `loading.status`, `loading.history`, `loading.load` |
| Events and batches | `events.subscribe`, `events.unsubscribe`, `wait.event`, `batch`, `undo.group`, `debug.crash` |

### Values

`component.set`, `scenecomponent.set` and `data.set` share one value language:

- numbers, strings, booleans, and enums by name;
- `{x,y}` or `{x,y,z}` for vectors and points, `{r,g,b,a}` or `#RRGGBB[AA]` for colours, `{x,y,width,height}` for rectangles;
- seconds or `hh:mm:ss.fff` for durations;
- an asset path or GUID for asset and prefab references, an entity key for entity references and `Entity`/`Transform` fields, and `Entity/Type` for component references.

Lists and arrays take a JSON array for the whole member, `Clips[2]` for one element, `Clips[+]` to append, and `remove=true` with an index to drop an element. Elements that are data objects take a JSON object of their fields, and dotted paths continue into them (`Bindings[0].Entity`). A member marked `[AssetType]` rejects an asset the inspector would not accept and names the type it wants. `component.get` lists every member the gateway can write under `values`.

Component and scene-component edits, `component.remove` and `prefab.revert` are undoable. Data asset edits are written straight to disk through the shared asset cache, so an open Data Asset window for the same file sees them. On `entity.set`, only the position change is undoable; other fields mark the scene dirty without an undo entry.

### Views, drops and builds

`view.focus entity=Player zoom=2` points the game view at something before a screenshot; `screenshot x=0 y=0 width=400 height=300` crops a window region so small text stays legible at reduced scale.

`asset.drop` does what dragging from the Asset Browser does: a prefab instantiates, a texture or Aseprite file becomes a sprite entity, and the result is selected and undoable.

`build.game` runs the same publish as the Build window and answers with the output folder and executable. `aot=false` skips NativeAOT and trimming for a fast self-contained build that needs no C++ toolchain, `compileAssets=` overrides the project's Asset Build setting, and `linuxContainer=true` builds Linux targets inside a container. `build.run gateway=true` launches the result listening and answers once its discovery file names that process.

### Events

`events.subscribe` streams editor events, and `voltage watch` prints them. The editor emits:

- `scene.loaded`, `scene.saved`, `scene.created`, `scene.reset`;
- `play.started`, `play.stopped`, `play.paused`, `play.resumed`;
- `scripts.compiled`, `project.loaded`, `project.unloaded`;
- `aseprite.connected` and `aseprite.disconnected` with `{port}`;
- `ui.modal {name, open}` and `prompt.suppressed {name, detail}` (see [Prompts and modals](#prompts-and-modals));
- per-frame state diffs: `selection.changed {entities}`, `scene.dirty {dirty}`, `undo.changed {undo, redo}`, `entity.added {id, name, guid}` and `entity.removed {id}`. A change of more than 32 entities at once collapses into one `entities.changed {added, removed}` carrying counts.

`wait.event name=scene.loaded timeout=30` answers with the next matching event (`{name, data}`) or fails when the timeout passes; `name` may be exact, a group prefix such as `scene.*`, or `*`.

`batch requests=[{method, params}, …]` runs requests in order on the main thread, waits for deferred answers such as `scripts.compile` before moving on, and replies once with `{results: [{ok, result | error}], failed}`; `stopOnError=false` keeps going past a failure. In the editor, `undoGroup="Build level"` folds every undoable edit in the batch into one undo step, and `undo.group action=begin|end` does the same for requests sent separately.

## Scene loading

`voltage --game loading` reports the current stage, actual percentage, readiness, errors and loading-display redraw statistics. `voltage --game loading --history` returns the last eight diagnostic traces; the underlying commands are `loading.status` and `loading.history`. Debug builds collect traces automatically. For a Release or NativeAOT investigation, set `VOLTAGE_LOADING_TRACE=1` before launch, and `VOLTAGE_LOADING_TRACE_DIRECTORY` to a private folder to save each finished trace as JSON. A normal Release build keeps the loading presentation and progress without detailed tracing.

Traces include scene keys and hashes, configuration, NativeAOT and compiled-asset identity, time since process start, preceding actions, ordered operations, per-stage inclusive timings, total request-to-present time, slow operations and errors. Nested inclusive timings overlap, so do not add them together. Storage is bounded to eight traces, 2,048 events per trace, 64 slow operations, 32 preceding actions and 32 errors; `droppedEvents` reports truncation. `loading.load scene=Scenes/A1_S1` is a diagnostic standalone load through the same runtime loader. It requires tracing and refuses a second load while one is active. To measure a particular menu or checkpoint sequence, use normal game input.

**Project Settings > Loading Screen** sets the font, caption, corner margin, colours and sparks. The engine counts eight real pipeline phases: file reading, deserialization, scene settings, prior scene teardown, entity construction, scene preparation, activation and first-frame presentation. Entity construction and activation also report completed items. Progress measures completed work rather than estimating time left, and readiness and 100% wait until pending startup entities have activated, an update has completed and a frame has been presented.

The standalone loader draws loading frames between work items and during Aseprite preparation; GPU work and lifecycle callbacks stay on the game thread. While loading frames are drawn, the gateway serves read-only commands, screenshots and `app.exit` without running gameplay; other commands return a busy error for the caller to retry. A single indivisible operation can still delay a redraw, and the longest gap is reported. Loading errors stay visible and are available through the gateway. The editor's own project-load display is separate from this presentation.

## Authoring

**Projects and scenes.** `project.create name= directory=` builds a project the way the New Project window does, copies the editor's compiled engine effects into it, and opens it. `scene.create` writes an empty scene, or with `template=copy` saves the open one under a new name.

**Prefabs.** `prefab.create entity=` saves a `.vprefab` and leaves a linked instance in the scene. `prefab.link entity= asset=` connects an existing entity without changing its stable ID, transform or component settings. `prefab.apply` writes an instance back to the file and onto its copies, `prefab.revert` reloads it from the prefab, and `prefab.open` / `prefab.close` enter and leave the isolated prefab edit scene.

**Tilemaps.** `tilemap.paint`, `tilemap.fill` (rectangle, or `flood=true`), `tilemap.erase` and `tilemap.collision` edit a `TilemapRenderer` cell by cell with the same undo steps the palette records. `tileset.create` writes a `.vtileset` for a texture.

For tile work, start with `tilemap.context entity="Room 1 attack door"` or `tilemap.context x=600 y=200 width=256 height=128`. This read-only command returns the overlapping tile layers in draw order, their hierarchy and tileset source paths, source Aseprite layer names, sparse cell stacks and collision, atlas coordinates with blank and opaque flags, and nearby sprites. Cell records are `[column,row,bottom-to-top tile indices,orientation,solid]`. The MCP tool also attaches the current game view when the region overlaps it; pass `includeViewportImage=false` for metadata only. The default region is centred on the current view, or on the chosen entity and its children. Regions are capped at 512 by 256 world pixels and output at 512 cell records; `truncatedCells` tells you to query a smaller region. The image shows the actual composite and lighting, while the cell data shows what lies underneath.

**Timelines.** `timeline.track.*`, `timeline.clip.*` and `timeline.key.*` load the `.vtimeline`, change it and save it at once. Each file edit is one undo step that swaps the whole file back (a change made to the file in between is overwritten, with a warning in the log), and directors bound to the file and an open Timeline window reread it. `timeline.events` and `timeline.properties` list the `[TimelineEvent]` methods and `[TimelineProperty]` members the compiled scripts registered. `timeline.validate asset=` (or `entity=` for a director with its bindings) reports what would not resolve in Play mode. `timeline.bind` and `timeline.unbind` edit a director's role bindings, `timeline.director` drives a `TimelineDirector` on an entity, and `animation.play` a `SpriteAnimator`.

**Scripts.** `script.create name= template=component|scenecomponent|dataasset|empty` uses the same templates as the Asset Browser's New Script menu; `script.write` and `script.delete` complete the set. Each compiles afterwards and answers with the diagnostics; `script.list` shows the files with the types they declare. Script edits are compiled and run by the editor, the same trust level as `scripts.compile`, so they carry the destructive flag. A script edit is a file change like any other, so the script watcher also hot-reloads the scene from disk: save unsaved scene work first.

**Audio and assets.** `audio.zone.create type=ambience|music|reverb|snapshot|crowd|ducking` adds a zone entity with a trigger collider, and `audio.mixer.set` changes a bus for the session. `asset.import source=` copies a file into the Content folder and creates its `.meta` GUID; `asset.importAseprite ... entity=true` also drops it into the scene as an animated sprite. Every destination path must stay inside the project.

**Addressing and timing.** Entities are addressed by numeric id, persistent GUID or name (`entity=Player`). Numeric ids are runtime counters that change on every scene load, so use the GUID or name when a reference must outlive a reload. `scene.load` refuses to discard unsaved changes unless `force=true`. `project.load`, `screenshot` and every `input.*` command answer once the work has actually finished.

In Edit mode, `scripts.compile` answers with completed diagnostics. In Play or Pause it immediately returns `success=false, deferred=true` with no errors: the compile is queued. Script saves also queue while Play is running when hot reload is on. Once Play has fully stopped, the latest saved scripts compile and the scene reloads on success; `scripts.compiled` reports the actual result.

## Navigating like a person

Prefer clicking by label over coordinates. The editor draws its widgets through a thin wrapper that records every button, menu item, selectable, checkbox, tab, tree node, header and input field with its on-screen rectangle:

- `ui.tree` lists what is on screen without a screenshot, and `ui.find label=Play` locates a widget.
- `ui.click label="Player (4)" window="Scene Graph"` presses it with the same synthetic input a coordinate click uses. Menus are addressed by path: `ui.click path="View/Window/Core Window"` opens each level and clicks the last, skipping levels already open.
- `ui.hover` parks the cursor on a widget so its tooltip shows in the next screenshot, `ui.focus window=` brings a window to the front, and `ui.windows` reports every window, popup and modal with its rectangle.

A miss answers with nearby labels. Widgets that plugins draw by calling ImGui directly are not recorded; use coordinates for those.

`ui.resize width=1600 height=900` fixes the window size first, so coordinates learned from one screenshot stay valid for the next. `screenshot` captures the whole editor window after the next frame (`window=false` gives the game view only). `scale=0.5` returns a quarter of the pixels, usually still readable and much cheaper for a model; image coordinates times `1/scale` are window pixels. `ui.info` reports the window size and cursor position.

The `input.*` commands feed the engine's own input state, which ImGui and the game both read, so a synthetic click lands on the real widget, fires the real handler and produces the real undo entry. Steps are spaced across frames the way ImGui needs (press, settle, release). Once any synthetic input has been sent, the gateway owns the mouse and keyboard until `input.release`, so a hand on the physical mouse cannot fight the agent. The devices are handed back automatically when the last client disconnects.

While captured, the virtual cursor also drives `Input.MousePositionDelta` and the engine's double-click detector, so game code sees a synthetic drag or `input.click count=2` exactly as it would a physical one; `input.state` reports the last delta and whether the latest script double-clicked. `input.hover x= y= frames=10` parks the cursor so a tooltip has time to open before a `screenshot`.

`input.script` runs a list of steps in one request. A `waitEvent` step holds the script until the editor emits a matching event (`scene.loaded`, `selection.*`, `*`) and fails the rest of the script after `timeout` seconds (default 30):

```json
{"steps": [
  {"action": "click", "x": 14, "y": 8},
  {"action": "wait", "frames": 5},
  {"action": "type", "text": "Hello\n"},
  {"action": "key", "key": "s", "modifiers": ["ctrl"]},
  {"action": "waitEvent", "name": "scene.saved", "timeout": 10},
  {"action": "release"}
]}
```

`hotkey.list` shows the editor's rebindable commands, and `hotkey.press id=Global.SaveScene` presses whatever keys are bound to one.

## Prompts and modals

A modal swallows every click outside it, so an unattended agent has to notice one before doing anything else. `ui.popups` lists the open popups and modals with the buttons that answer them (`ui.click label=No window="Missing Engine Effects"` presses one), `ui.windows` marks them `modal: true`, and subscribed clients get `ui.modal {name, open}` whenever one opens or closes.

`--no-prompts` keeps the startup prompts closed instead. Missing Engine Effects and Plugins Needed log a warning, appear in `status.suppressedPrompts`, and reach subscribed clients as `prompt.suppressed {name, detail}`. The user's own "don't show again" setting is never written. What those prompts offer is available as commands:

- `plugin.list` says which declared plugins are missing and why (`fetchable`, `unpublished`, `brokenLocal`, `missingInRepo`), and `plugin.restore [name=]` fetches the fetchable ones as the prompt's install button does.
- `effects.status` reports whether compiled engine effects exist, and `effects.compile` builds them.
- `effects.setup` installs and verifies the shader toolchain on Linux and macOS, or selects an existing one with `wineLoader=` and `winePrefix=`. With prompts suppressed, a missing toolchain fails with setup guidance instead of waiting. See [Effects](../effects.md).

The unsaved-changes prompt stays; the commands that can trigger it take `force=true`.

## Headless and CI

`--headless` hides the SDL window at startup, fixes the size at 1600x900, keeps the loop running with or without a client, and implies `--no-prompts`. The hidden window still owns the GL context, so drawing continues into the back buffer and `screenshot` returns the same frame a visible editor would show; `status` reports `headless: true`.

An explicit `VOLTAGE_EDITOR_DATA` folder is an isolated editor profile: settings and layouts inside it are kept, and an empty profile starts from the bundled defaults. Legacy user data is migrated only when no override is set.

`Voltage.Editor.Tests` runs the gateway end to end. It writes a small project to a temp folder, starts the built editor from a private binary copy with `--headless --gateway-port 0 --gateway-info <tmp>/gateway.json <project>.voltage` and a fresh `VOLTAGE_EDITOR_DATA`, exercises entities, components, undo, batches, events, synthetic input, click-by-label, screenshots and script compilation, then sends `editor.exit force=true`. Run it with `dotnet test Voltage.Editor.Tests -c Editor-Debug` after building the editor. `VOLTAGE_EDITOR_EXE` overrides the executable and `VOLTAGE_TESTS_KEEP=1` keeps the temp folder. The `gateway-tests` CI job runs the suite on Linux under Xvfb with Mesa, and on Windows with a software `opengl32.dll` beside the editor.

## Crashes

An unhandled exception writes `crash_yyyyMMdd_HHmmss.log`, with the last 40 log lines, to the logs folder and exits immediately with code 70 (unless a debugger is attached), so no OS error dialog waits for a person. The CLI names the crash log when it finds the editor gone, and `voltage start` brings it back with the same arguments. `debug.crash confirm=true` exercises that whole path on purpose.

## Adding a method

Editor handlers live in `Voltage.Editor/Gateway/Commands`; host-neutral ones in `Voltage.Engine/Gateway/Commands/CoreCommands.cs`. Register one with:

```csharp
table.Add(name, help, (args, ctx) => …, P.Int("x", "Window pixels"), P.Str("name", required: true));
```

Return any serializable object, throw `GatewayException` for a user-facing error, or return a `Task<object>` when the answer depends on a later frame or event. The `P.*` helpers (`Str`, `Int`, `Float`, `Bool`, `Enum`, `List`, `Obj`, `Any`) declare the parameters that `commands` reports and the MCP schema is built from; a command without them still gets its `params:` help text parsed. Chain `.ReadOnly()`, `.Destructive()` or `.Unsafe()` to flag what the command does. Editor handlers reach the UI through `ctx.ImGui()`. Adding a name that already exists replaces the engine's version, which is how the editor supplies a richer `status`, `ui.info` and `batch`.

## Security

The listener binds to loopback only and requires the per-session token from `gateway.json`. Anything that can read that file can compile and run code through the editor, which is the same trust level as the user account itself.

`--gateway-safe` narrows that:

- Commands flagged unsafe are refused with `'<name>' is disabled by --gateway-safe`: `build.game`, `build.run`, `assetbuild.run`, `debug.crash`, `editor.exit`, `app.exit`, `console.exec`, `project.create`, `plugin.restore`, `effects.compile`, `effects.setup`, `aseprite.connect`, `aseprite.disconnect`, `aseprite.install`, `aseprite.save_to_project`, and every live `aseprite.*` tool that is not read-only. The Aseprite bridge is never started automatically in safe mode.
- Synthetic input can reach any menu, so the injecting commands are refused too: every `input.*` command except `input.state`, `input.wait`, `input.release` and `input.record`, plus `hotkey.press`, `ui.click` and `ui.hover`. `ui.tree`, `ui.find` and the semantic commands remain.
- `screenshot` may only write inside the host's Screenshots folder, and `data.create` only inside the project.

`status` reports `safe: true`, and `commands` lists the `unsafe`, `destructive` and `readOnly` flags of every command.
