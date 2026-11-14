---
title: Asset Build
sidebar_position: 7
description: Compile the Content folder into MonoGame .xnb files for published games, with the same loaders resolving compiled or raw assets.
---

# Asset Build

Without the asset build, a published game ships the `Content/` folder as raw files and decodes them at load time: images through `Texture2D.FromStream`, Aseprite files through the engine's own parser, audio through NVorbis and NLayer. The asset build compiles that folder with the MonoGame Content Builder (MGCB) into `.xnb` files, so the game loads pre-decoded textures and pre-flattened Aseprite frames instead. It is off by default and never affects the editor, which always loads raw files.

## Menu

**Build > Asset Build** holds the switch and the tools:

| Item | What it does |
|---|---|
| **Compile assets on build** | Stores `AssetBuild.Enabled` in `ProjectSettings.json`. When on, every game build runs the asset build before copying content. If the asset build fails, the game build stops. |
| **Build assets now** | Runs the asset build on its own into `<project>/bin/AssetBuild/<platform>` and opens a report with counts, timings and errors. The folder is emptied first. Game builds never use this output. |
| **Clean asset build** | Deletes that output and the compile cache. |
| **Settings** | The Asset Build Settings window (below). |
| **New pipeline extension** | Opens the settings window at the scaffold section; see [Extending the asset build](#extending-the-asset-build). |
| **Show last report** | Reopens the report of the last run, including one started by a game build. |
| **Open output folder** | Opens the standalone output in your file manager. |

## Settings

| Setting | Default | Meaning |
|---|---|---|
| **Compile assets on build** | Off | As above. |
| **Platform** | `DesktopGL` | MGCB platform for **Build assets now**. One of DesktopGL, Windows, Android, iOS, Switch, PlayStation4, PlayStation5, XboxOne, WindowsStoreApp; unknown names save as DesktopGL. Game builds for Windows, macOS and Linux always use DesktopGL. |
| **Texture format** | `Color` | `Color` or `Compressed`. |
| **Premultiply alpha** | Off | Passed to the texture, Aseprite and bitmap font processors. |
| **Compress .xnb files** | On | MGCB `/compress`. |
| **Compile audio** | Off | Converts `.wav`, `.ogg` and `.mp3` to PCM `.xnb` files, which are larger than the sources. |
| **Include** | `**/*` | Globs relative to `Content/`. Files that match no include glob are skipped. |
| **Exclude** | `**/Voltage/**`, `**/*.fx`, `**/*.mgfxo` | Matching files are copied raw instead of compiled. |

Globs are case-insensitive: `**` spans folders, `*` and `?` stay inside one folder name. The window also edits rules and runs the extension scaffold. The same settings are exposed to the gateway as `assetbuild.settings`, and the build as `assetbuild.run` (`platform`, `clean`, `copyRaw`, `output` inside the project). `assetbuild.status` and `assetbuild.clean` complete the set, and `build.game compileAssets=` overrides the stored switch for one build.

## What gets compiled

| Source | Compiled as | Runtime type | Notes |
|---|---|---|---|
| `.png .jpg .jpeg .bmp .tga` | `TextureImporter` + `VoltageTextureProcessor` | `Texture2D` | Fully transparent pixels are blacked out, matching `Texture2D.FromStream`. Colour key, mipmaps, power-of-two resize and make-square are off; premultiply and format follow the settings. Uses MonoGame's `TextureProcessor` if `Voltage.Pipeline.dll` is missing. |
| `.aseprite .ase` | `AsepriteImporter` + `AsepriteProcessor` | `AsepriteFile` | Frames, layers, tags, slices and per-layer pixels are flattened at build time, so the runtime skips cel decompression and compositing. |
| `.fnt` and its pages | `BitmapFontImporter` + `BitmapFontProcessor` | `BitmapFont` | Read back by the engine's `BitmapFontReader`. |
| `.wav .ogg .mp3` | copied raw, or `WavImporter`/`OggImporter`/`Mp3Importer` + `SoundEffectProcessor` | `SoundEffect` | Compiled only with **Compile audio** on. |
| `.vtileset .vasset .vtimeline .vprefab .vscene .json .tmx .tsx .meta` | copied raw | unchanged | JSON and Tiled formats stay as files. |
| `.fx .mgfxo` | copied raw | `Effect` | Effects use the separate shader compiler from the Effects menu. |
| anything else | copied raw | | |

Files under `Content/Voltage/` (engine content) are never compiled. Aseprite and `.fnt` files are copied raw, with a warning, when `Voltage.Pipeline.dll` is missing beside the editor. A file whose name contains `;` or a line break, or starts with `#`, `$` or `/`, is skipped because the response file cannot carry it.

Skipped files are left out of a standalone run's output. In a game build, `dotnet publish` still copies them along with the rest of `Content/`.

## Incremental builds

MGCB keeps compiled outputs in `obj/AssetBuild/<platform>/compiled/<tool-version>` and dependency records in `obj/AssetBuild/<platform>/obj`. Game builds and **Build assets now** share this cache, so recreating an output folder only copies current compiled files. MGCB rebuilds a file when its source, dependencies or processor settings change, or when its output is missing. The report separates newly compiled files from **up to date** ones. **Clean asset build** deletes the cache, so the next run compiles everything.

## How a build finds compiled assets

The asset build writes `Content/content.index` next to the compiled files: one line per compiled asset, `source path<TAB>asset name`. The source path is the project-relative path scene data and scripts use (`Content/Characters/Hero.aseprite`); the asset name is the MGCB name relative to the content root, without extension (`Characters/Hero`). When two sources would share a name (`Hero.png` and `Hero.aseprite`), the later one gets its extension appended (`Characters/Hero_aseprite`), then a number if that is taken too.

`VoltageContentManager` reads the index from the game's `Content` folder on first use. Every `Load*` method, `LoadByType<T>` behind `AssetReference`, and therefore every `SpriteRenderer`, `SpriteAnimator`, `TilemapRenderer`, audio component and direct script call such as `Content.LoadTexture("Content/UI/Cursor.png")` or `AsepriteUtils.LoadAsepriteAnimation(entity, "Content/Characters/Hero.aseprite", "Idle")` asks the index first and loads the `.xnb` when there is an entry. Anything without an entry loads the raw file as before, so a project can compile a subset of its content, or none, and still run. The editor has no index, so its behaviour never changes.

A compiled source never ships next to its `.xnb`: the output holds one or the other. Game builds start from an empty output folder, and every run deletes `.xnb` files the current plan did not produce, so renamed or deleted sources leave nothing behind.

## Extending the asset build

Rules decide what happens to an extension before the built-in table does. Use them to route your own file types through custom importers, or to change how a built-in type is handled (send `.png` through another processor, or `copy` or `skip` a type). A rule has a name, extensions, an action (`compile`, `copy` or `skip`), importer and processor class names, optional `Key=Value` processor parameters, and optionally the project-relative DLL that holds the classes; rules without a DLL use MonoGame's built-in importers. Rules live under `AssetBuild.Rules` in `ProjectSettings.json`, in the settings window, and on the gateway as `assetbuild.rules`, `assetbuild.rule.set` and `assetbuild.rule.remove`. Editor plugins register rules in code with `AssetBuildRules.Register`; a project rule for the same extension wins.

**Build > Asset Build > New pipeline extension** (or `assetbuild.scaffold name=Level extension=.lvl`) writes a working starting point:

- `Pipeline/Level/Level.csproj` with `LevelImporter`, `LevelProcessor` and `LevelWriter`: a .NET 8 class library referencing the MonoGame content pipeline. The game project excludes `Pipeline/**` from its own compile (new projects carry the exclusion; the scaffold adds it to older ones).
- `Scripts/Content/LevelAsset.cs` with the runtime `LevelAsset` type, its reader, and a module initializer that registers a raw-file loader through `VoltageContentManager.RegisterLoader`, so `[AssetType(typeof(LevelAsset))] AssetReference` fields load in the editor and in builds without the asset build.
- The rule `Level` for `.lvl`, pointing at `Pipeline/Level/bin/Release/net8.0/Level.dll`.

When a rule's DLL sits in a folder with exactly one `.csproj`, the asset build runs `dotnet build -c Release` on it whenever a source file is newer than the DLL. The scaffolded project references `Voltage.Pipeline` through the `VoltagePipelineDir` MSBuild property, which the editor passes on that command line, so the `.csproj` holds no machine-specific path; to build it by hand, add `-p:VoltagePipelineDir=<editor folder>/Pipeline/`. Extension projects must stay on .NET 8, which is what `mgcb` loads. Only the runtime file compiles with the game scripts; the pipeline project is never loaded by the editor or the game.

Rules are validated wherever they come from. The DLL must be inside the project, the importer and processor must be class names, parameters must be `Key=Value`, extensions must be letters and digits, and no field may contain `;` or line breaks. Files covered by a rule that fails a check, or whose DLL is missing, fail with the reason instead of being copied.

## Tooling

The build uses the `dotnet-mgcb` tool at the same version as the engine's MonoGame package, recorded in a tool manifest inside the project (`<project>/.config/dotnet-tools.json`) and installed with `dotnet tool restore`. The editor runs the restore the first time and whenever the version changes, so the first run needs NuGet access. Custom importers live in `Voltage.Pipeline.dll`, which ships in the editor's `Pipeline` folder and is passed to MGCB with `/reference`. The generated response file is `<project>/obj/AssetBuild/<platform>.rsp`; inspect it or run it by hand with `dotnet mgcb /@:<file>`. It deliberately avoids the `.mgcb` extension, which the game's own MonoGame build task would pick up during publish.

Requirements: the .NET SDK on the path, which game builds already need. No Visual Studio components are required.

## Diagnostics

The report window and `assetbuild.run` list every file with its outcome: compiled, up to date, copied, skipped, or failed with MGCB's message. A file MGCB accepted but produced no `.xnb` for is reported as failed. `assetbuild.status` reports the tool version, whether the manifest is restored, the last output and its index size. `log.tail` carries the same lines with the `[AssetBuild]` prefix.
