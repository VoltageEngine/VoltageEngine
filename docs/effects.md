# Effects

Voltage uses MonoGame DesktopGL on Windows, macOS and Linux. Every shader must be compiled with `/Profile:OpenGL`, including on Windows.

## First launch and game exports

The built-in effects are compiled at MonoGame 3.8.5.1 and embedded in `Voltage.dll`. A fresh checkout, relocated Editor, or exported game needs no MGFXC or Wine installation to load them. Existing loose DirectX or old-format built-in files are ignored in favor of the bundled OpenGL versions.

Engine builds verify source and bytecode hashes against `Voltage.Engine/Graphics/Effects/Compiled/manifest.props`, accepting both LF and CRLF source checkouts. Changing a shader or the MonoGame package requires regenerating the bundle:

```sh
dotnet run --project tools/effects/Voltage.Effects.Build.csproj
dotnet run --project tools/effects/Voltage.Effects.Build.csproj -- --verify
```

Commit the shader, generated `.mgfxo`, and manifest together. The compiled files are release resources, not local build debris. The generator passes relative source paths so bytecode contains no account or workstation paths. An existing compiler can be supplied with `--compiler=/path/to/mgfxc` at the matching package version.

## Custom shaders

The Effects menu and game publishing compile shaders from the project's configured Effects folder into `Content/Effects`, preserving subdirectories. Projects with no custom shaders need no compiler. Fingerprints include nested `#include` files, the tool version and profile; unchanged valid outputs are reused. Fingerprints also verify the output hash, so missing or damaged bytecode is rebuilt. Project stamps live in `obj/Effects`.

The first required compilation installs `dotnet-mgfxc` at the loaded MonoGame package version into the user application-data folder under `Voltage/Tools/mgfxc`. It requires the .NET SDK and NuGet access. A global MGFXC installation or shell PATH configuration is unnecessary. The Editor includes built-in shader sources in its output and compiles engine overrides into `Voltage/Effects/<version>/<bundle>/OpenGL` under user application data. The bundle key prevents overrides from an older engine release from masking updated built-in shaders. These overrides are included in game exports.

On macOS and Linux, compiling changed HLSL shaders requires Wine. **Compile Effects** and **Build Game** share a short **Shader Setup** prompt when Wine or its shader environment is missing or fails verification. Choose **Install and Continue**, **Choose Installation**, or **Cancel**. Setup runs in the background, supports cancellation, and resumes the requested operation after compiling a test shader.

After success, a per-user verification stamp checks local tool and runtime metadata. Both manual effects compilation and game builds reuse this stamp: they do not repeat downloads, installation, or the test shader while the installation is unchanged. Missing files, changed tools or permissions, a different compiler version, and toolchain failures invalidate it. Valid cached shader outputs need no Wine check at all.

Automatic setup uses the detected Linux package manager with an OS authorization prompt, or an existing Homebrew installation on macOS. Immutable systems such as SteamOS retain their system filesystem: use Wine in the development container or choose an existing portable installation. Other unsupported installations can follow [MonoGame's platform setup instructions](https://docs.monogame.net/errors/mgfx0001/). Native macOS installation still needs verification on a Mac.

Voltage prepares a dedicated per-user Wine prefix containing a Windows .NET runtime, native `d3dcompiler_47.dll`, and MonoGame's `fxccs.dll`. Downloads are pinned and hash-verified. An existing Wine prefix is reused when it works and is never overwritten or deleted by setup. A selected Wine executable and prefix are saved locally without editing shell profiles. With no saved selection, Voltage respects `WINELOADER` and `MGFXC_WINE_PATH`, then checks managed and system Wine installations.

With `--headless` or `--no-prompts`, missing setup reports a clear error instead of waiting for a dialog. Run `effects.setup` to install explicitly, or `effects.setup wineLoader=/path/to/wine winePrefix=/path/to/prefix` to select and verify an existing installation, then retry. `effects.status` reports `setupBusy` and `setupVerified`.

Both output pipes are drained concurrently. Shader compilation has a two-minute deadline; tool installation has a five-minute deadline. Cancellation terminates the compiler process tree. Output is compiled to a temporary file, checked for the current DesktopGL header, then promoted only on success. A failed build preserves the previous output and blocks publishing it as a successful build. Build completion reports failures, including prerequisite errors and cancellation.

Custom effects loaded by `VoltageContentManager.LoadEffect` resolve against the project content root. Built-in effect constructors use embedded resources, with valid Editor overrides or loose exported overrides taking precedence.

## Verification

```sh
dotnet test Voltage.Effects.Tests/Voltage.Effects.Tests.csproj -c Release
```

The isolated suite covers Windows/macOS/Linux environment rules, custom Wine prefixes, nested includes and cache corruption, argument safety, output-pipe deadlocks, cancellation and deadlines. Set `VOLTAGE_TEST_MGFXC` to a compiler path to test native compilation and failure recovery. Native GPU loading runs on Windows; set `VOLTAGE_TEST_GPU=1` on another OS with a desktop graphics session. Linux CI uses Mesa/Xvfb.

On macOS, run GPU checks on the process main thread with `dotnet run --project Voltage.Effects.Tests/Voltage.Effects.Tests.csproj -- --gpu-check`; Cocoa rejects graphics initialization from a test worker thread. This also checks shared animator textures, independent playback and origins, layer selections, refresh and scene-unload disposal.

The Effects workflow runs first-launch and process tests on native Windows, macOS and Linux runners, native compilation on Windows, and GPU loading on Linux. Local environment simulations are not a substitute for native macOS/Linux compiler or driver verification. Native Wine compilation on those hosts remains a separate verification step after installing MonoGame's prerequisites.

## Setup verification and follow-up

Validated paths include background prefix installation, manual compilation before and after a shader edit, persisted verification after restarting, Linux and Windows Debug exports with AOT and asset rebuilding disabled, cancellation before publishing, and selecting an existing prefix after a missing dependency. The effects suite passes 72 tests with 7 GPU tests skipped. Editor-Debug and Editor-Release build successfully. The editor gateway suite passes 23 of 24 tests; `Headless_layout_is_docked` also fails against the unchanged editor.

Further work and testing:

- Run native macOS setup on Intel and Apple Silicon, including Homebrew, Wine loader paths, authorization, and cancellation.
- Exercise automatic Wine and 7-Zip package installation on fresh supported Linux desktops and the portable-installation choice on immutable systems.
- Test package-manager denial, network interruption, hash mismatch, and cancellation during download or installation.
- Re-run game publishing with AOT and full asset compilation enabled, and opt into the GPU effect tests on supported desktops.
- Investigate the existing headless docking failure independently of shader setup.
