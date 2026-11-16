# Rendering backend roadmap

**Priority: high.** Run Voltage on DirectX 12 and Vulkan through MonoGame 3.8.6, falling back to OpenGL 4 and then to the OpenGL build used today when the newer backends are not available. The editor, every bundled effect and the lighting system (all eight light types, normal maps, projected lights) must look the same on a backend before it becomes a default.

## What MonoGame 3.8.6 provides

Checked against the 3.8.6.1 packages and source on 2026-11-15.

- `MonoGame.Framework.Native` is one managed `MonoGame.Framework.dll` that calls a native library named `mgruntime`.
- `mgruntime` ships in interchangeable builds with the same exported API: `MonoGame.Runtime.Windows.DX12`, `MonoGame.Runtime.Windows.Vulkan` and `MonoGame.Runtime.Windows.DesktopGL4`, plus Vulkan and DesktopGL4 builds for Linux and macOS. Picking a backend means picking which `mgruntime` is loaded.
- The effect compiler has three relevant profiles. `OpenGL` (format id 0) accepts shader model 3 and lower. `DirectX_12` (id 2) and `Vulkan` (id 80) require `vs_6_0` and `ps_6_0` and define `SM6`. The DesktopGL4 runtime reads the `OpenGL` profile, so it runs the effects Voltage already ships.
- The native runtimes link SDL statically and export no SDL functions. The Vulkan runtime loads the Vulkan loader at run time, so a missing or failing driver shows up at device creation, not at library load, and it takes the process down with an access violation that managed code cannot catch.
- `MonoGame.Framework.DesktopGL` (the build used today) is a different managed assembly with the same name.
- MonoGame's own tracking issue for the desktop Vulkan release (8944) is still open. Treat the native runtimes as preview quality.

## Done

- **MonoGame 3.8.6.1 on the current OpenGL build.** The 35 bundled effects recompile to identical bytes. Light previews and six scenes of a real project render the same before and after, and the effects and editor test suites fail on the same tests as before the upgrade.
- **Build flavour.** `-p:VoltageGraphics=Native` switches the engine, persistence and editor projects to `MonoGame.Framework.Native` and copies the three Windows runtimes to `backends\directx12`, `backends\vulkan` and `backends\opengl4`. Without the property nothing changes. Wired for win-x64 only.

  ```
  dotnet build Voltage.Editor/Voltage.Editor.csproj -c Editor-Release -p:VoltageGraphics=Native
  ```

- **Backend selection with fallback.** `GraphicsBackendSelector` runs from the `Core` static constructor, before MonoGame makes a native call. Automatic order is DirectX 12, Vulkan, OpenGL 4 on Windows and Vulkan, OpenGL 4 elsewhere. `--graphics-backend <DirectX12|Vulkan|OpenGL4>` or `VOLTAGE_GRAPHICS_BACKEND` moves one to the front. DirectX 12 is probed by asking Direct3D whether a device can be created, Vulkan by loading the Vulkan loader. A backend that dies in device creation leaves `backends\pending.txt` behind; the next start moves it to `backends\failed.txt` and skips it until it is requested by name or the file is deleted. The log line `[Graphics] Using ...` names the choice and every skipped backend with its reason.
- **Stock sprite effect.** On the native runtimes `EffectResource` takes the stock sprite effect from the runtime, which holds it compiled for the loaded backend.
- **Effect loading per profile.** `EffectResource.ShaderProfile` follows the loaded backend. Bundled and external effects for a profile other than OpenGL are read from a folder named after the profile (`DirectX_12`, `Vulkan`). An effect with no build for the active profile fails with a message that says so.

## Measured on the development machine

Windows 11, AMD Radeon RX 7900 XTX, native flavour of the editor.

- DirectX 12: selected automatically. The editor UI runs at 60 frames per second. No Voltage effect has a DirectX 12 build yet, so scenes that use one cannot draw.
- OpenGL 4: light previews for every light type are pixel identical to the OpenGL build. Opening a real project hangs (item 1).
- Vulkan: device creation fails inside the runtime (`vkGetPhysicalDeviceSurfaceCapabilitiesKHR` returns `VK_ERROR_UNKNOWN`). A bare MonoGame app with no Voltage code fails the same way, so this is upstream. The selector records the crash and falls through to the next backend on the following start.

## Work, in order

1. **GPU calls off the render thread.** The OpenGL 4 runtime stops with `OpenGL calls must run on the render thread` when a project loads, because scene loading creates GPU resources from worker threads. Find those calls and queue them to the main thread for backends that need it.
2. **Shader model 6 port of the bundled effects.** The sources hard code `vs_2_0`, `ps_2_0` and `ps_3_0` and use shader model 3 sampler syntax. Port them to a shared header that selects the shader model, constant buffer, texture declaration and sampling form per profile. The OpenGL output of a ported effect must stay byte identical, which the effects tool can check. Pixel shaders drawn through the sprite batch must take the full sprite vertex output (position, colour, texture coordinate) so their inputs line up with the stock vertex shader on DirectX 12 and Vulkan. A hand port of `SpriteEffect.fx` already compiles for all three profiles with the 3.8.6.1 compiler. Start with `DeferredSprite`, `DeferredLighting`, `ProjectedLight`, `ForwardLighting`, `PolygonLight` and `StencilLight`, then the post processors and transitions.
3. **Compiling per profile.** `EffectToolchain` compiles one profile and checks for format id 0. Give it a profile parameter, have `tools/effects` emit `Compiled/DirectX_12` and `Compiled/Vulkan` next to the OpenGL set and list them in the manifest, and make the editor compile engine and project effects for the active profile. The asset build picks the profile from the target.
4. **Remove direct SDL and GL calls from shared paths.** `Clipboard`, `SdlAudioDevice`, the event pump in `SceneLoading`, the GL strings in `RenderErrorLog`, and in the editor `SdlFileDropWatcher`, `SdlNativeInit`, `EditorWindowLayout`, `EditorDisplayScale`, `MacRetina` and the window hide and minimise checks in `Editor`. Each needs a path that works when SDL is not a separate library and `Window.Handle` is not an SDL window. Until then a headless editor on a native runtime keeps its window visible.
5. **Games.** `EngineLibsSync`, the project template and the game builder still write DesktopGL references. Give projects a graphics setting, copy the `backends` folder into game builds and add the selector's setting to `ProjectSettings.json`.
6. **Fallback to the current OpenGL build.** That build is a different managed assembly, so the choice has to happen before any MonoGame type loads. The editor and games built without AOT can resolve the assembly from a per backend folder at startup. Games published with Native AOT cannot swap managed assemblies; for them the last fallback is the OpenGL 4 runtime.
7. **Linux and macOS runtimes.** Reference the Vulkan and DesktopGL4 runtime packages for those platforms and extend the win-x64 check in the editor project.
8. **Editor on each backend.** ImGui renderer, scene and game views, Play view overlay windows, screenshots, gizmos, tilemap and light previews, file drop and clipboard.
9. **Lighting verification.** A gateway driven test that renders one scene per light type, with normal maps and a projected light, on every available backend and compares against the OpenGL reference within a tolerance.
10. **Vulkan.** Report the device creation failure upstream with the minimal repro, and retest on each MonoGame release.
11. **Defaults.** Keep the native flavour opt in. Make it the default only after items 8 and 9 pass on Windows, Linux and macOS.
