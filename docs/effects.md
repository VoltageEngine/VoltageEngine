---
title: Effects
description: How built-in and custom shaders are compiled, cached and shipped, and how the Wine toolchain is set up on macOS and Linux.
---

# Effects

Voltage renders with MonoGame DesktopGL on every desktop platform, so every shader is compiled with `/Profile:OpenGL`, on Windows too.

## Built-in effects

The engine's own effects are compiled with MonoGame 3.8.5.1 and embedded in `Voltage.dll`. A fresh checkout, a relocated editor and an exported game all load them without MGFXC or Wine. Loose DirectX or old-format copies of built-in effects are ignored in favour of the embedded OpenGL versions.

Engine builds check each shader source and its bytecode against the hashes in `Voltage.Engine/Graphics/Effects/Compiled/manifest.props`, accepting LF and CRLF checkouts alike, and fail when they disagree. After changing a built-in shader or the MonoGame package version, regenerate the bundle and verify it:

```sh
dotnet run --project tools/effects/Voltage.Effects.Build.csproj
dotnet run --project tools/effects/Voltage.Effects.Build.csproj -- --verify
```

Commit the shader, the generated `.mgfxo` and the manifest together: the compiled files are release resources, not build output. The generator passes relative source paths, so the bytecode contains no machine paths. To use a compiler you already have, pass `--compiler=/path/to/mgfxc` at the matching package version.

## Custom shaders

Put `.fx` files in the project's Effects folder (`Effects/` in a new project). The **Effects** menu (**Compile Project Effects**, **Compile Engine Effects**, **Compile ALL Effects**) and every game build compile them into `Content/Effects`, keeping subfolders. A project without custom shaders needs no compiler.

Each output is fingerprinted from its source, every nested `#include`, the compiler version and the profile, together with a hash of the output itself. Unchanged, intact outputs are reused; missing or damaged bytecode is rebuilt. The project's stamps live in `obj/Effects`.

Load a custom effect with `VoltageContentManager.LoadEffect`, which resolves the name against the project's content root. Built-in effect constructors use the embedded bytecode unless a valid override exists (see below).

### The compiler

The first compilation that needs it installs `dotnet-mgfxc` at the loaded MonoGame version into `<local app data>/Voltage/Tools/mgfxc`. This needs the .NET SDK and NuGet access; no global MGFXC install or PATH change is required.

Compilation has a two-minute deadline and tool installation five minutes. Cancelling ends the whole compiler process tree. Output goes to a temporary file, is checked for a current DesktopGL header, and replaces the previous output only on success, so a failed compile keeps the last good bytecode and a game build reports the failure instead of publishing.

### Engine effect overrides

The editor ships the built-in shader sources. **Compile Engine Effects** writes overrides to `<local app data>/Voltage/Effects/<MonoGame version>/<bundle revision>/OpenGL`. The bundle revision keeps overrides made by an older engine release from masking updated built-in shaders. Game builds copy valid overrides into the build's `Content/Voltage/Effects`.

## Wine on macOS and Linux

MGFXC compiles HLSL through Windows components, so on macOS and Linux it runs under Wine. **Compile** commands and **Build Game** share a **Shader Setup** prompt that opens when Wine or its shader environment is missing or fails verification:

- **Install and Continue** installs everything in the background and resumes the requested operation after compiling a test shader.
- **Choose Installation** takes an existing Wine executable and prefix, then **Check and Continue** verifies them.
- **Cancel** stops setup.

Voltage prepares its own per-user Wine prefix containing a Windows .NET runtime, the native `d3dcompiler_47.dll` and MonoGame's `fxccs.dll`. Downloads are pinned and hash-verified. An existing prefix is reused when it works and is never overwritten or deleted. A chosen Wine executable and prefix are saved in a per-user file; shell profiles are never edited. Without a saved choice, Voltage honours `WINELOADER` and `MGFXC_WINE_PATH`, then checks its managed Wine and the system's.

Automatic installation uses the detected Linux package manager, with an OS authorization prompt, or an existing Homebrew install on macOS. On immutable systems such as SteamOS, use Wine inside a development container or choose an existing portable install. For other setups, follow [MonoGame's platform instructions](https://docs.monogame.net/errors/mgfx0001/).

After a successful setup, a per-user verification stamp records the tool and runtime details, so later compiles and builds skip the download, install and test shader. Missing files, changed tools or permissions, a different compiler version or a toolchain failure invalidate the stamp. Cached shader outputs that are still valid need no Wine check.

## Headless and agents

With `--headless` or `--no-prompts`, a missing toolchain fails with setup guidance instead of opening the prompt. Through the [Editor Gateway](gateway/README.md):

- `effects.setup` installs and verifies the toolchain.
- `effects.setup wineLoader=/path/to/wine winePrefix=/path/to/prefix` selects and verifies an existing install.
- `effects.status` reports the bundled and override effect counts, `compilerAvailable`, `wineReady`, `busy`, `setupBusy` and `setupVerified`.
- `effects.compile` builds the engine effects.

## Tests

`Voltage.Effects.Tests` covers platform environment rules, custom Wine prefixes, nested includes, cache corruption, argument handling, cancellation and deadlines:

```sh
dotnet test Voltage.Effects.Tests/Voltage.Effects.Tests.csproj -c Release
```

Set `VOLTAGE_TEST_MGFXC` to a compiler path to include real compilation, and `VOLTAGE_TEST_GPU=1` to run GPU loading on a desktop session outside Windows. On macOS, run the GPU checks on the main thread with `dotnet run --project Voltage.Effects.Tests/Voltage.Effects.Tests.csproj -- --gpu-check`, because Cocoa rejects graphics initialization from a test worker thread.
