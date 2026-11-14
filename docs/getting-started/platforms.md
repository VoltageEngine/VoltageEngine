---
title: Platforms
sidebar_position: 3
description: Where the editor runs, which platforms games build for, and the native tools each target needs.
---

# Platforms

The Voltage Editor and the games it builds run on MonoGame DesktopGL (SDL2 and OpenGL), so the same project works on Windows, macOS and Linux.

## Build targets

| Target | Status | Output |
|---|---|---|
| Windows x64 | Available | Folder with the game executable, `Content`, `Data` and settings. |
| Linux x64 | Available | Folder with a native executable. |
| macOS x64 (Intel) | Available | `<Project>.app` bundle. |
| macOS ARM64 (Apple Silicon) | Available | `<Project>.app` bundle. |
| Nintendo Switch, PlayStation 4/5, Xbox (GDK) | Listed, not yet available | Need the platform SDK and a platform MonoGame backend. |

Choose the target in **Build > Build Game**; see [Build Game](../editor/game-build.md). With **Native AOT** checked (the default), the game is compiled ahead of time and trimmed into a native executable that needs no .NET runtime; this is the build to ship. Unchecked, the build is a plain self-contained publish: faster, and no C++ toolchain needed, which suits quick tests. Engine serialization is generated at compile time so both work (see [Serialization and NativeAOT](../engine/serialization-and-aot.md)).

## Native toolchains

NativeAOT links with the platform's native tools:

| Building on | Needs |
|---|---|
| Windows | Visual Studio C++ build tools (the "Desktop development with C++" workload). |
| Linux | `clang` and the zlib development package. |
| macOS | Xcode command-line tools. |

Unavailable targets in the Build window explain what is missing. On Linux the editor also checks the runtime libraries games need (OpenGL, SDL2, OpenAL) and names the package for your distribution's package manager.

## Linux and Steam Deck

NativeAOT binaries depend on the C library of the machine that built them. When podman or Docker is available, Voltage publishes Linux builds inside an Ubuntu 20.04 container, so the executable needs only glibc 2.31 and runs on SteamOS and older distributions, not just the machine that built it. Without a container runtime it builds directly on the host.

## macOS

A macOS build is a signed `.app` bundle with the SDL2 and OpenAL libraries in `Contents/Frameworks` and game content in `Contents/Resources`. It is signed with your configured identity, or ad-hoc; distributing outside your own Macs needs a Developer ID signature and notarization, which you do separately. Icons and signing require building on a Mac.

## Shaders

Shaders compile for OpenGL on every platform. Built-in effects ship precompiled; custom shaders compile on Windows directly, and on macOS and Linux through Wine, which the editor can set up for you. See [Effects](../effects.md).

## Input and display

Keyboard, mouse, up to the platform's gamepad limit and touch are supported on every desktop target (see [Input](../scripting/input.md)). Display size, fullscreen and vsync come from **Project > Settings > Display & Audio**, and the design resolution and resolution policy decide how the game scales to any window (see [Scenes and Entities](../scripting/scenes-and-entities.md#resolution-policies)).
