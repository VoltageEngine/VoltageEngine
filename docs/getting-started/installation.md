---
title: Installation
sidebar_position: 1
description: Download the editor, or build it from source, and open your first project.
---

# Installation

## Download the editor

Builds for Windows, macOS (Apple Silicon and Intel) and Linux are on the [download page](/download). Each is self-contained, so running the editor needs no .NET install. Unzip or open it and start `Voltage.Editor`.

Publishing a game runs `dotnet publish`, so install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) before your first build. To change the engine itself, build it from source as described below.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or newer. Every engine, editor and game project targets `net8.0`.
- Windows, macOS or Linux with an OpenGL 3 capable GPU driver. The editor and games use MonoGame DesktopGL (SDL2 and OpenGL).
- For NativeAOT game builds, the platform's native toolchain: the Visual Studio C++ build tools on Windows, `clang` and `zlib1g-dev` on Linux, the Xcode command-line tools on macOS. A build with AOT turned off needs none of these.
- For custom shaders on macOS and Linux, Wine. The editor offers to install it the first time a shader needs compiling; see [Effects](../effects.md).

## Build the editor

```bash
git clone https://github.com/VoltageEngine/VoltageEngine.git
cd VoltageEngine
dotnet build Voltage.Editor/Voltage.Editor.csproj -c Editor-Debug
```

The executable lands in `Voltage.Editor/bin/Editor-Debug/<rid>/`, for example `win-x64/Voltage.Editor.exe` on Windows. `Editor-Release` builds an optimized editor to `bin/Editor-Release/<rid>/`.

The `Editor-*` configurations define the `EDITOR` preprocessor symbol, which adds the inspector, serialization manager and hot-reload hooks to the engine. Plain `Debug` and `Release` build the engine the way a published game uses it.

### Repeated builds on macOS

Close the editor, then run the build helper from the engine folder:

```bash
./tools/build-editor-macos.sh
```

It picks Intel or Apple Silicon automatically, allows one helper build per checkout, uses a single MSBuild node and disables the compiler and build servers. Pass `--no-restore` once dependencies are restored, or `-c Editor-Release` for the optimized editor. It refuses to run while an editor is open and never terminates an application. If an interrupted build leaves its lock directory behind, make sure the build has stopped, then remove the directory it reports.

Ordinary macOS builds also turn off shared compilation and parallel project builds by default. These settings limit build concurrency; they do not limit the editor's memory at runtime. If the whole Mac restarts, read the macOS panic report and follow [Apple's unexpected-restart guide](https://support.apple.com/102382).

### Dock app on macOS

Every macOS build also refreshes `Voltage.Editor/bin/<configuration>/Voltage Editor.app`, a bundle with the Voltage icon. Drag it to the Dock once; later builds update it in place, so the Dock icon always launches the newest build. Pass `-p:VoltageMacApp=false` to skip it.

If the checkout or your projects are in Documents, Desktop or Downloads, macOS asks for access on first launch and remembers the answer per code signature. Builds are ad-hoc signed, so the question returns after each rebuild. To keep the answer, put a signing identity from `security find-identity -v -p codesigning` as the only line of `Voltage.Editor/mac-signing-identity.txt` (gitignored), or pass `-p:VoltageMacSigningIdentity=...`.

## Run it

Start the editor with no arguments to get the project picker, or pass a `.voltage` file to open a project directly:

```bash
Voltage.Editor/bin/Editor-Debug/win-x64/Voltage.Editor.exe C:/Games/MyGame/MyGame.voltage
```

**File > New Project** creates a game project with the [standard folder layout](../editor/projects.md), its own `.csproj`, and an `EngineLibs/` folder that the editor fills with the engine assemblies on every project load.

## The command-line client

`Voltage.Cli` builds `voltage`, a client for the [Editor Gateway](../gateway/README.md) that lets scripts and AI agents drive a running editor:

```bash
dotnet build Voltage.Cli/Voltage.Cli.csproj -c Editor-Debug
dotnet Voltage.Cli/bin/Editor-Debug/net8.0/voltage.dll status
```

## Next

Follow [Your First Game](first-game.md) to create a project, a scene and a component.
