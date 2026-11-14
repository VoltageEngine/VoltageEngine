---
title: Source Generation, Serialization, and NativeAOT
sidebar_position: 1
description: What the source generator emits for partial components, which members are saved, the identity attributes, and how a game is published.
---

# Source Generation, Serialization, and NativeAOT

## Why components are `partial`

`Voltage.SourceGenerators` (`ComponentDataGenerator`) writes serialization code at compile time. For every concrete `partial` subclass of `Component` or `SceneComponent` that does not override `Data` itself, it emits into the other half of the class:

1. A nested `public sealed class <Name>GeneratedData : ComponentData` with one field per saved member.
2. A `Data` property override. The getter snapshots the component's members into that class; the setter applies loaded data back.
3. A static `Read_<Name>GeneratedData(JsonTokenReader)` method: a hand-unrolled JSON reader with no reflection.
4. A `[ModuleInitializer]` method, `__RegisterDeserializer_<Name>`, that registers the reader with `ComponentDataAotDeserializer` under the data type's full name.

A second pass emits `ComponentDataAotBootstrap.AutoRegister`, another module initializer. It registers **every** concrete component and scene component with a public parameterless constructor in `ComponentAotFactory`, partial or not, so scenes can create them by name without reflection. It also registers each `[ComponentId]` in `ComponentIdRegistry`, each `[FormerlyKnownAs]` name in `TypeRenameRegistry`, and a deserializer for any hand-written `Data` override.

In practice:

- A component without `partial` still loads from a scene, in the editor and in a published build, but it has no generated `Data`, so its fields come back with their default values on every load.
- `ComponentDataAotDeserializer` is consulted before reflection in every build, editor included. The generated code uses `new T()`, direct member assignment and `JsonTokenReader` only, which is what makes a trimmed NativeAOT build safe.
- Data assets (`DataAsset` subclasses) must be `partial` as well; a non-partial one is error VLT012.

## Which members are saved

The generator walks the class and its base classes up to, but not including, `Component` or `SceneComponent`. It saves:

- every public instance field that is not `const`;
- every property with a public getter and a public setter (except the built-in ones such as `Enabled`, `UpdateOrder`, `Name`, `Entity` and `Transform`);
- non-public fields and properties marked `[Serialize]` (`Voltage.SerializeAttribute`). `[Tooltip]` and `[Range]` derive from `[Serialize]`, so they also mark a private member for saving.

Members marked `[JsonExclude]` are skipped.

| Category | Types |
|---|---|
| Primitives | `bool`, `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `float`, `double`, `string` |
| Enums | Any `enum` |
| Engine values | `Vector2`, `Color`, `RectangleF` |
| Asset references | `AssetReference`, `PrefabReference` |
| Scene references | Fields typed as a `Component` subclass, `Entity` or `Transform`. They are stored as `ComponentReference` or `EntityReference` and resolved after every entity in the scene exists. |
| Your structs | Structs with public fields and no explicit parameterless constructor |
| Your classes | Classes implementing `IComponentGroup`, with public fields. The generator emits a reader and a deep-copy helper for each. |
| Collections | `List<T>`, `Dictionary<string, TValue>` and `T[]` of any of the above, including references |

Other class types are skipped. When a component has public fields but none could be saved, the build warns with VLT002, and VLT001 when it has no saved members at all.

A struct with an explicit parameterless constructor is error VLT003: the reader calls `new T()` and never runs the constructor, so its defaults would silently differ between the editor and a build. Use an `IComponentGroup` class instead. Initialize `IComponentGroup` fields at their declaration (`= new Stats()`); a null group is logged as an error on save.

## Renaming

| What changes | What to do |
|---|---|
| Component class name or namespace | Nothing. Scenes reference the class by its `[ComponentId]`. |
| Data asset class name or namespace | Nothing. `.vasset` files store the `[AssetTypeId]`. |
| A saved field or property on a `partial` component | The generated reader matches JSON keys to exact member names and skips unknown keys, so the saved value is dropped and the member loads with its default. Re-enter the value after renaming. |
| A field in a hand-written `ComponentData` class | Add `[DecodeAlias("oldName")]` to the renamed field. These classes are read by the reflection decoder, which honours the alias. `[DecodeAlias]` has no effect on generated data. |

## Identity attributes

| Attribute | Applies to | Effect |
|---|---|---|
| `[ComponentId("alias")]` | Component and scene-component classes | The stable identity scenes store. The editor stamps it on the first compile that finds a class without one, using the class's simple name, and never rewrites it. Do not change or reuse an id once a scene refers to it; two classes with one id is error VLT004. The attribute is emitted by the generator into each compilation, so scripts compile even against an outdated engine reference. |
| `[AssetTypeId("alias")]` | `DataAsset` classes | The same scheme for data assets: stamped once, then frozen. A duplicate is VLT010; a missing one is warning VLT015. |
| `[FormerlyKnownAs("Old.Type")]` | Classes | Obsolete. Maps old type names for scenes saved before `[ComponentId]` existed. |
| `[JsonExclude]` | Fields and properties | Never saved. The engine uses it on back-references such as `Component.Entity` to avoid cycles. |

## Publishing a game

`GameBuilder.BuildGameAsync`, behind the Build window and the gateway's `build.game`, runs these steps:

1. Checks that the startup scene exists and is included in the chosen configuration.
2. Builds `Voltage.Persistence` and `Voltage.Engine` without `EDITOR` and copies them into the project's `EngineLibs/`.
3. Verifies every plugin and regenerates the plugin build files. An `Unavailable` or `Failed` plugin stops the build.
4. Compiles the project's custom effects.
5. Runs `dotnet publish --self-contained` for the target runtime. With AOT on (the default) this adds NativeAOT and trimming; with AOT off it is a plain self-contained build that needs no C++ toolchain.
6. Copies the engine content (fonts and effect overrides), optionally compiles `Content/` with MGCB, then copies the project's content and plugin content.
7. Copies `Data/` (including `assets.manifest`), then copies the selected scenes and every prefab at their project-relative paths, dropping excluded scenes from the manifest.
8. Copies `ProjectSettings.json`, and on macOS packages an `.app` bundle.

The editor-flavoured engine assemblies are restored to `EngineLibs/` afterwards, whether the build succeeded or not. Output goes to `<project>/bin/<Debug|Release>/<rid>/`, for example `bin/Release/win-x64/`.

Trimming keeps what reflection still needs through `TrimmerRoots.xml`, which the editor copies into the game project before publishing. It preserves `Voltage.dll`, `Voltage.Persistence.dll` and the MonoGame assemblies wholesale.

## Editor scripts versus a published game

In the editor, scripts compile with Roslyn into an in-memory `DynamicScripts` assembly. After each compile the editor sets `Core.LatestScriptAssembly` and runs the new assembly's module initializers, so the same generated registrations exist as in a build.

A published game has no `DynamicScripts` assembly: `dotnet publish` compiles `Scripts/` into the game executable, and the module initializers run at startup before any scene is read.
