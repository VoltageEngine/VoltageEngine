# Effects

Voltage uses MonoGame DesktopGL on Windows, macOS and Linux. Every shader must be compiled with `/Profile:OpenGL`, including on Windows.

## First launch and game exports

The 34 built-in effects are compiled at MonoGame 3.8.5.1 and embedded in `Voltage.dll`. A fresh checkout, relocated Editor, or exported game needs no MGFXC or Wine installation to load them. Existing loose DirectX or old-format built-in files are ignored in favor of the bundled OpenGL versions.

Engine builds verify source and bytecode hashes against `Voltage.Engine/Graphics/Effects/Compiled/manifest.props`, accepting both LF and CRLF source checkouts. Changing a shader or the MonoGame package requires regenerating the bundle:

```sh
dotnet run --project tools/effects/Voltage.Effects.Build.csproj
dotnet run --project tools/effects/Voltage.Effects.Build.csproj -- --verify
```

Commit the shader, generated `.mgfxo`, and manifest together. The compiled files are release resources, not local build debris. The generator passes relative source paths so bytecode contains no account or workstation paths. An existing compiler can be supplied with `--compiler=/path/to/mgfxc` at the matching package version.

## Custom shaders

The Effects menu and game publishing compile shaders from the project's configured Effects folder into `Content/Effects`, preserving subdirectories. Projects with no custom shaders need no compiler. Fingerprints include nested `#include` files, the tool version and profile; unchanged valid outputs are reused. Fingerprints also verify the output hash, so missing or damaged bytecode is rebuilt. Project stamps live in `obj/Effects`.

The first required compilation installs `dotnet-mgfxc` at the loaded MonoGame package version into the user application-data folder under `Voltage/Tools/mgfxc`. It requires the .NET SDK and NuGet access. A global MGFXC installation or shell PATH configuration is unnecessary. The Editor includes built-in shader sources in its output and compiles engine overrides into `Voltage/Effects/<version>/<bundle>/OpenGL` under user application data. The bundle key prevents overrides from an older engine release from masking updated built-in shaders. These overrides are included in game exports.

On macOS and Linux, **compiling new HLSL shaders still requires Wine**; running bundled shaders does not. Follow [MonoGame's platform setup instructions](https://docs.monogame.net/errors/mgfx0001/). Voltage respects `MGFXC_WINE_PATH`, falls back to `~/.winemonogame`, checks Wine/winepath and prefix prerequisites, and supplies standard system, Homebrew, and Wine application paths to GUI-launched processes. It reports missing prerequisites without deleting or changing an existing Wine prefix.

Both output pipes are drained concurrently. Shader compilation has a two-minute deadline; tool installation has a five-minute deadline. Cancellation terminates the compiler process tree. Output is compiled to a temporary file, checked for the current DesktopGL header, then promoted only on success. A failed build preserves the previous output and blocks publishing it as a successful build. Build completion reports failures, including prerequisite errors and cancellation.

Custom effects loaded by `VoltageContentManager.LoadEffect` resolve against the project content root. Built-in effect constructors use embedded resources, with valid Editor overrides or loose exported overrides taking precedence.

## Verification

```sh
dotnet test Voltage.Effects.Tests/Voltage.Effects.Tests.csproj -c Release
```

The isolated suite covers Windows/macOS/Linux environment rules, custom Wine prefixes, nested includes and cache corruption, argument safety, output-pipe deadlocks, cancellation and deadlines. Set `VOLTAGE_TEST_MGFXC` to a compiler path to test native compilation and failure recovery. Native GPU loading runs on Windows; set `VOLTAGE_TEST_GPU=1` on another OS with a desktop graphics session. Linux CI uses Mesa/Xvfb.

The Effects workflow runs first-launch and process tests on native Windows, macOS and Linux runners, native compilation on Windows, and GPU loading on Linux. Local environment simulations are not a substitute for native macOS/Linux compiler or driver verification. Native Wine compilation on those hosts remains a separate verification step after installing MonoGame's prerequisites.
