# Voltage Engine

Voltage is a standalone 2D game engine and editor for C# developers. It ships as a single executable that opens a Godot/Unity-style editor — a dockable ImGui interface sitting in front of a live MonoGame render window — and can publish finished games as self-contained, NativeAOT-compiled binaries with no runtime dependency.

---

## Table of Contents

1. [Overview and Architecture](#1-overview-and-architecture)
2. [Editor Guide](#2-editor-guide)
   - [Projects and Folder Layout](#21-projects-and-folder-layout)
   - [Main Windows](#22-main-windows)
   - [Play, Pause, Reset, and Audio](#23-play-pause-reset-and-audio)
   - [Scenes and Prefabs](#24-scenes-and-prefabs)
   - [The Asset Browser and .meta GUIDs](#25-the-asset-browser-and-meta-guids)
   - [Hot-Reload Script Workflow](#26-hot-reload-script-workflow)
3. [Scripting Guide](#3-scripting-guide)
   - [The Component Model and Lifecycle](#31-the-component-model-and-lifecycle)
   - [SceneComponents](#32-scenecomponents)
   - [Content Loading](#33-content-loading)
   - [Audio](#34-audio)
4. [Hands-On Tutorial](#4-hands-on-tutorial)
5. [Source Generation, Serialization, and NativeAOT](#5-source-generation-serialization-and-nativeaot)
6. [Scripting Rules and Gotchas](#6-scripting-rules-and-gotchas)
7. [Engine Engineer Notes](#7-engine-engineer-notes)
8. [Documentation Site](#8-documentation-site)

---

## 1. Overview and Architecture

### What Voltage Is

Voltage is a **standalone editor + runtime engine** for 2D pixel-art games written in C#. The editor is a single executable; game code lives in a separate C# project that the editor compiles, hot-reloads, and eventually publishes.

At runtime the world is structured as **Scenes containing Entities containing Components** — a traditional component-based model similar to Unity's MonoBehaviour system rather than a strict ECS. Entities carry a `Transform` (position, rotation, scale, parent/child hierarchy). Components hold data and behavior. `SceneComponent`s attach to the scene itself rather than to any entity and are useful for scene-wide managers.

### Engine vs Editor Split

| Layer | Project | Role |
|---|---|---|
| `Voltage.Engine` | `Voltage.Engine.dll` | Core runtime: `Core`, `Scene`, `Entity`, `Component`, content loading, rendering, physics, audio. Compiles with or without the `EDITOR` preprocessor symbol. |
| `Voltage.Editor` | `Voltage.Editor.exe` | ImGui editor shell, project/asset management, hot-reload, serialization manager, game builder. Depends on `Voltage.Engine` with `EDITOR` defined. |
| `Voltage.SourceGenerators` | Roslyn analyzer | Generates AOT-safe serialization code for every `partial Component` and `partial SceneComponent` subclass. |

Game code (your scripts) lives in a separate game project that references `Voltage.Engine` but not `Voltage.Editor`. The editor compiles your scripts into a `DynamicScripts` assembly and loads it at runtime; a published game includes only your compiled scripts + the engine.

### Tech Stack

- **Language / runtime:** C# / .NET (modern)
- **Windowing / graphics:** MonoGame.Framework.DesktopGL 3.8.5-preview.1 (SDL2 backend, OpenGL renderer)
- **Editor UI:** ImGui.NET (immediate-mode GUI)
- **Publishing:** .NET NativeAOT + trimming
- **Serialization:** Custom `Voltage.Persistence.Json` with AOT-safe deserializers; no reflection at runtime in published builds
- **Asset formats:** `.png`, `.ase`/`.aseprite`, `.tmx` (Tiled), `.wav`, `.ogg` (via MonoGame)

### Comparison to Other Engines

| Concept | Voltage | Unity analog | Godot analog |
|---|---|---|---|
| Script attachment | `Component` on `Entity` | `MonoBehaviour` on `GameObject` | `Node` script |
| Scene-wide logic | `SceneComponent` | `MonoBehaviour` on scene root | `AutoLoad` singleton |
| Scene file | `.vscene` | `.unity` | `.tscn` |
| Prefab file | `.vprefab` | `.prefab` | `.tscn` (instanced) |
| Asset GUID | `.meta` sidecar (editor-only) | `.meta` sidecar | `uid://` embedded |
| Game loop entry | `Core : Game` | Hidden by engine | Hidden by engine |

### Notable Limitations

- The `.meta` / GUID asset-reference system is **editor-time only**. Published games resolve assets by file path.
- Prefab overrides are at the **component level** only (the entire component's data is replaced). Field-level property overrides (like Unity's) are not supported.
- `Entity.Transform` position/rotation/scale are **not** part of prefab delta data; position is supplied at instantiation time via `Scene.LoadPrefab(path, position)`.
- There is no visual prefab editing mode; prefabs are authored by saving an entity as a prefab from the Scene Graph.

---

## 2. Editor Guide

### 2.1 Projects and Folder Layout

The editor works with **Voltage projects** — a directory containing a `.voltage` metadata file alongside the game C# project. Open a project from `File > Load Project…` or create one with `File > New Project`.

A freshly created project has this layout (relative paths are stored in `ProjectMetadata`):

```
MyGame/
  MyGame.csproj              # game script project
  MyGame.voltage             # project metadata (JSON)
  ProjectSettings.json       # display resolution, vsync, startup scene
  Content/                   # textures, audio, tiled maps, fonts
  Scripts/                   # C# source files hot-reloaded by the editor
  Effects/                   # custom GLSL/HLSL shader source files
  Data/
    Scenes/                  # .vscene files
    Prefabs/                 # .vprefab files
  Build/                     # output of the Build system (generated)
```

The paths `ContentsFolder`, `ScriptsFolder`, `EffectsFolder`, `DataFolder`, `ScenesFolder`, and `PrefabsFolder` are all properties on `RuntimeGameProject` and are resolved from the metadata at load time.

`ProjectSettings.json` controls the startup scene name, target design resolution, and display settings applied on the first `Scene.LoadLevel` call.

### 2.2 Main Windows

| Window | Purpose |
|---|---|
| **Scene Graph** | Entity hierarchy for the active scene. Create, rename, reparent, duplicate, and delete entities. Select an entity to open its inspector. |
| **Inspector** | Edits the selected entity's `Transform` and component fields. Component data is read and written through each component's `Data` property. |
| **Asset Browser** | File tree rooted at `Content/`, `Scripts/`, `Effects/`, `Data/Scenes/`, and `Data/Prefabs/`. Drag a texture onto a `SpriteRenderer` field in the inspector to assign it. Right-click for Copy / Paste / Duplicate / Delete. Keyboard: `Ctrl+C`, `Ctrl+V`, `Ctrl+D`, `Delete`. |
| **Game Viewport** | The live MonoGame render target displayed as an ImGui image. The editor UI is rendered on top; the game content renders into the same window as a passthrough. |
| **Editor Tools bar** | Cursor/zoom mode toggle, audio mute toggle, and the Play / Stop / Pause / Reset button cluster. |

Layouts and theme can be changed from the **View** menu.

### 2.3 Play, Pause, Reset, and Audio

The Editor Tools bar contains a four-button cluster that controls the game loop:

| Button | Keyboard | What it does |
|---|---|---|
| **Play** | `F1` | Enters Play mode (`Core.IsEditMode = false`). Component `Update` methods start running. |
| **Stop** | `F1` (again) | Returns to Edit mode. If `Core.ResetSceneAutomatically` is true the scene is reloaded from disk, discarding Play-mode changes. |
| **Pause** | `F2` | Toggles `Core.IsPauseMode`. Components that do not implement `IUpdatableInPauseMode` stop updating. Only meaningful while in Play mode. |
| **Reset** | `F5` | Calls `Core.InvokeResetScene()`. Reloads the scene from disk without leaving Play mode. |

Additional shortcuts:

| Action | Keyboard |
|---|---|
| Reload scene (recompile scripts + reload) | `F6` |
| Save scene | `Ctrl+S` |
| Toggle fullscreen | `F11` |

The **audio mute** button in the toolbar calls `Core.InvokeSwitchAudio(bool)`, which sets `Core.IsAudioOn`, fires `Core.OnSwitchAudio`, and applies `SoundEffect.MasterVolume` as a global backstop. Components that implement `IAudioComponent` receive an `OnAudioStateChanged` callback.

> Scene data **cannot be saved** while in Play or Pause mode. `Ctrl+S` is ignored and a notification is shown.

### 2.4 Scenes and Prefabs

**Scenes** are `.vscene` files stored in `Data/Scenes/`. Each scene is a JSON document containing entity transforms, component data, and a list of `SceneComponent` entries. The scene's display name comes from `SceneData.Name`; the key used for loading is the file name without extension (`Scene.LevelName`).

Load a scene from code:

```csharp
Scene.LoadLevel("Level2");
```

Reload the current scene (e.g. for a "restart" button):

```csharp
Scene.ReloadCurrentLevel();
```

**Prefabs** are `.vprefab` files stored in `Data/Prefabs/`. A prefab captures one entity (with its components and child entities) as a reusable template. To create a prefab, right-click an entity in the Scene Graph and choose *Save as Prefab*. To instantiate a prefab from code at runtime:

```csharp
Entity enemy = Core.Scene.LoadPrefab("Enemies/Slime.vprefab", spawnPosition);
```

**Prefab overrides** work at the component level. When a scene entity was created from a prefab, loading the scene re-instantiates the prefab and then overwrites individual component data entries from the scene's delta. There is no field-level override system.

**Important caveats:**
- `Entity.Transform` (position/rotation/scale) is **not** part of the prefab delta. Position is supplied at instantiation via the `position` parameter of `LoadPrefab`.
- `Entity.OriginalPrefabGuid` is always `Guid.Empty` at runtime because the GUID/.meta system is editor-only. Runtime prefab identity falls back to `Entity.OriginalPrefabName` (the file name without extension).

### 2.5 The Asset Browser and .meta GUIDs

Every file in a watched asset folder gets a `.meta` sidecar file the first time it is seen by the editor. The sidecar contains a stable `Guid` that survives renames and moves within the project. The editor uses this GUID to resolve prefab references when a `.vprefab` file is renamed or moved.

**The `.meta`/GUID system is editor-time only.** Published games do not read `.meta` files. Asset references in scenes and prefabs are stored as project-relative file paths, and `VoltageContentManager` resolves them against `VoltageContentManager.ContentRoot` at runtime.

Do not delete `.meta` files while the project is open. If a `.meta` file is missing the editor regenerates it with a new GUID, which will break any prefab references that pointed to the old GUID.

### 2.6 Hot-Reload Script Workflow

The editor watches the project's `Scripts/` folder using `ScriptWatcher`. When a `.cs` file changes, `ScriptManager` recompiles all scripts in the folder into a transient `DynamicScripts` assembly. If compilation succeeds and `AutoReloadSceneOnChange` is enabled, the scene is reloaded so the new code takes effect immediately.

Key settings (persisted per user, not per project):

| Setting | Default | Meaning |
|---|---|---|
| `EnableHotReload` | `true` | Auto-compile on file save |
| `AutoReloadSceneOnChange` | `true` | Reload scene after successful compile |
| `CompileOnStartup` | `true` | Compile scripts when a project is first opened |

The hot-reload assembly is for **edit-time only**. When you build the game (see Section 5), the scripts are compiled ahead-of-time as part of the NativeAOT publish step — the source generator runs at that point to emit reflection-free deserialization code.

---

## 3. Scripting Guide

### 3.1 The Component Model and Lifecycle

Derive from `Component` to attach behavior to an entity:

```csharp
public partial class PlayerController : Component, IUpdatable
{
    public float MoveSpeed = 200f;

    public override void OnAddedToEntity()
    {
        // Called immediately when AddComponent is called.
        // Entity.Transform is available. Other components may not be ready yet.
    }

    public override void OnEnabled()
    {
        // Called after OnAddedToEntity when the entity is enabled.
    }

    public override void OnStart()
    {
        // Called after OnEnabled. Entity.Scene is set. Safe to query other components.
    }

    public void Update()
    {
        // Called every frame while in Play mode and not paused.
        var dir = Vector2.Zero;
        if (Input.IsKeyDown(Keys.Left))  dir.X -= 1f;
        if (Input.IsKeyDown(Keys.Right)) dir.X += 1f;
        Entity.Position += dir * MoveSpeed * Time.DeltaTime;
    }

    public override void OnRemovedFromEntity()
    {
        // Cleanup. Called when the component is removed or the entity is destroyed.
    }

    public override void OnDisabled()
    {
        // Called when the entity or this component is disabled.
    }
}
```

**The `partial` keyword is required** on any component that serializes fields (see Section 5). Omitting it means the source generator cannot emit the `Data` property, so no fields will be saved or loaded.

**Lifecycle order summary:**

```
AddComponent(c) called
  → c.OnAddedToEntity()          (immediate, before added to live list)
  → c.OnEnabled()                (if enabled)
  → c.OnStart()                  (after enabled; Entity.Scene is set)
  ...per-frame...
  → c.Update()                   (IUpdatable only; skipped while paused unless IUpdatableInPauseMode)
  → c.OnEntityTransformChanged() (when parent transform changes)
RemoveComponent(c) / entity.Destroy()
  → c.OnRemovedFromEntity()
```

**Common Entity API:**

```csharp
// Add / get / remove components
entity.AddComponent(new SpriteRenderer());
var sr = entity.GetComponent<SpriteRenderer>();
bool found = entity.TryGetComponent<SpriteRenderer>(out var sr);
entity.RemoveComponent<SpriteRenderer>();

// Hierarchy traversal
var childSprite = entity.GetComponentInChildren<SpriteRenderer>();
var parentHealth = entity.GetComponentInParent<HealthComponent>();

// From within a Component, shortcut methods delegate to Entity:
var sr = GetComponent<SpriteRenderer>();
```

**Entity properties:**

```csharp
entity.Name
entity.Tag              // int tag for scene-wide queries
entity.Enabled          // enables/disables the entity and all its components
entity.UpdateOrder      // sort order within the scene's entity list
entity.Position         // world-space shortcut to entity.Transform.Position
entity.LocalPosition    // local-space position
entity.Rotation         // radians
entity.Scale
entity.Parent           // Transform of the parent entity
entity.Destroy()        // queues destruction at end of frame
entity.Destroy(float)   // destroy after N seconds (coroutine internally)
```

**Pause-mode behavior:** Components that implement `IUpdatableInPauseMode` continue receiving `Update` calls while `Core.IsPauseMode` is true. Regular gameplay components are frozen. Use this for HUD, menus, or any component that must remain responsive while the game is paused.

```csharp
public partial class PauseMenuUI : Component, IUpdatable, IUpdatableInPauseMode
{
    public void Update() { /* runs even while paused */ }
}
```

### 3.2 SceneComponents

`SceneComponent` is a scene-scoped behavior — it lives on the scene rather than on an entity. Good for game-wide managers (spawn systems, wave controllers, audio mixers):

```csharp
public partial class WaveManager : SceneComponent
{
    public int CurrentWave = 1;

    public override void OnStart()
    {
        // Scene and all entities are ready.
    }

    public override void Update()
    {
        // Called each frame before entity updates.
        // Frozen while Core.IsPauseMode is true (same as regular components).
    }
}
```

Add from code or from the editor's Scene Component panel:

```csharp
var wm = Core.Scene.AddSceneComponent<WaveManager>();
var wm = Core.Scene.GetSceneComponent<WaveManager>();
Core.Scene.RemoveSceneComponent<WaveManager>();
```

`SceneComponent` must also be declared `partial` if it has serializable public fields.

### 3.3 Content Loading

Voltage uses a **path-based content system** rather than asset-reference objects. `VoltageContentManager` resolves paths against `VoltageContentManager.ContentRoot`, which the editor sets to the open project's root directory. In a published game it defaults to `AppContext.BaseDirectory`.

Paths are typically relative to the project root, e.g. `"Content/Characters/Hero.png"`.

The scene's `Content` manager is the right one for scene-lifetime assets; `Core.Content` is for assets that should persist across scene transitions.

```csharp
// Load a PNG texture
Texture2D tex = Entity.Scene.Content.LoadTexture("Content/Tiles/Grass.png");

// Load an Aseprite file (returns AsepriteFile)
AsepriteFile ase = Entity.Scene.Content.LoadAsepriteFile("Content/Characters/Hero.aseprite");

// Load a sound effect
SoundEffect sfx = Entity.Scene.Content.LoadSoundEffect("Content/Audio/Jump.wav");

// Load a Tiled map
TmxMap map = Entity.Scene.Content.LoadTiledMap("Content/Maps/Level1.tmx");
```

All loaders cache by path — loading the same path twice returns the same object. Assets are disposed when the `VoltageContentManager` is disposed (at scene end for `scene.Content`, never for `Core.Content`).

**`SpriteRenderer` convenience loaders** — these are the methods to call when loading a sprite from code inside a component. They internally use the scene's content manager and also update the component's serialized data so the path survives a save/load cycle:

```csharp
// PNG
spriteRenderer.LoadPngFile("Content/Characters/Hero.png");

// Aseprite — optional layer name and 0-based frame index
spriteRenderer.LoadAsepriteFile("Content/Characters/Hero.aseprite");
spriteRenderer.LoadAsepriteFile("Content/Characters/Hero.aseprite", layerName: "Body", frameNumber: 0);

// Tiled map image layer
spriteRenderer.LoadTmxFile("Content/Maps/Level1.tmx");
spriteRenderer.LoadTmxFile("Content/Maps/Level1.tmx", imageLayerName: "Background");
```

Do not use `Core.Content` or `scene.Content` directly to load a sprite and then assign it to a `SpriteRenderer` — the path will not be serialized and the sprite will disappear on scene reload.

### 3.4 Audio

Any component that produces audio should implement `IAudioComponent` and register itself with `AudioComponentRegistry` so it respects the global mute toggle:

```csharp
public partial class FootstepPlayer : Component, IUpdatable, IAudioComponent
{
    private SoundEffect _sfx;
    private SoundEffectInstance _instance;

    public override void OnAddedToEntity()
    {
        AudioComponentRegistry.Register(this);
        _sfx = Entity.Scene.Content.LoadSoundEffect("Content/Audio/Footstep.wav");
        _instance = _sfx.CreateInstance();
    }

    public override void OnRemovedFromEntity()
    {
        AudioComponentRegistry.Unregister(this);
        _instance?.Dispose();
    }

    public void OnAudioStateChanged(bool isAudioOn)
    {
        if (!isAudioOn)
            _instance?.Pause();
        else if (_instance?.State == SoundState.Paused)
            _instance?.Resume();
    }

    public void Update()
    {
        if (Input.IsKeyPressed(Keys.Space) && Core.IsAudioOn)
            _instance.Play();
    }
}
```

`Core.IsAudioOn` — global read/write property. Setting it calls `Core.InvokeSwitchAudio`, which fires `Core.OnSwitchAudio` and applies `SoundEffect.MasterVolume` as a backstop for any audio that bypasses `IAudioComponent`.

---

## 4. Hands-On Tutorial

This walkthrough creates a new project, adds a scene, and writes a component that loads a PNG sprite.

### Step 1 — Create a New Project

1. Launch the Voltage Editor.
2. Choose `File > New Project`. Fill in a project name and directory and click *Create*.
3. The editor creates the folder structure described in Section 2.1 and opens the project.

### Step 2 — Create a Scene

1. In the **Asset Browser**, navigate to `Data/Scenes/`.
2. Right-click the folder and choose *New Scene*. Name it `GameScene`.
3. Double-click `GameScene.vscene` to open it. The Scene Graph shows a default Camera entity.

### Step 3 — Add an Entity

1. In the **Scene Graph**, right-click the root and choose *Create Entity*. Name it `Hero`.
2. The Inspector shows the entity's `Transform`. Set **Position** to `(0, 0)`.

### Step 4 — Write a Component

In the `Scripts/` folder create a new file `HeroVisuals.cs`:

```csharp
using Voltage;
using Voltage.Sprites;

public partial class HeroVisuals : Component
{
    public override void OnStart()
    {
        var sprite = AddComponent(new SpriteRenderer());
        sprite.LoadPngFile("Content/Characters/hero.png");
    }
}
```

Place `hero.png` in the project's `Content/Characters/` folder.

Save the file. The editor detects the change, recompiles the scripts, and reloads the scene.

### Step 5 — Attach the Component

1. Select the `Hero` entity in the Scene Graph.
2. In the Inspector, click *Add Component* and choose `HeroVisuals` from the list.
3. Press `Ctrl+S` to save the scene.

### Step 6 — Press Play

Click the **Play** button in the Editor Tools bar (or press `F1`). The scene enters Play mode. `OnStart` runs, `LoadPngFile` loads the texture, and the sprite appears at the origin of the game viewport.

Press `F1` again to return to Edit mode. The scene reloads automatically if `Core.ResetSceneAutomatically` is true (the default).

---

## 5. Source Generation, Serialization, and NativeAOT

### Why Components Must Be Declared `partial`

Voltage uses a Roslyn source generator (`ComponentDataGenerator`) to emit serialization code at compile time. For every `partial Component` or `partial SceneComponent` subclass that does not already override `Data`, the generator emits:

1. A nested `sealed class XxxGeneratedData : ComponentData` with one public field per serializable field on the component.
2. An override of the `Data` property — a getter that snapshots the component's current fields into a `XxxGeneratedData` instance, and a setter that applies a loaded `XxxGeneratedData` back to the component's fields.
3. A private static `Read_XxxGeneratedData(JsonTokenReader)` method — a hand-unrolled JSON parser with no reflection, safe under NativeAOT trimming.
4. A `[ModuleInitializer]` method `__RegisterDeserializer_Xxx` that registers the reader with `ComponentDataAotDeserializer` so the engine can deserialize the type by its fully-qualified name at runtime.

A second generator pipeline emits a shared `[ModuleInitializer]` in `ComponentDataAotBootstrap.AutoRegister` that registers every concrete Component and SceneComponent in `ComponentAotFactory` (so instances can be created by name without reflection).

**What this means in practice:**

- A component without `partial` has no `Data` override. Its fields serialize to nothing. In the editor this is immediately visible as "data not persisting on save/reload". In a published build the component type also cannot be instantiated by the factory, so it would not load from a scene file at all.
- The generated code uses only `new T()`, direct field assignment, and `JsonTokenReader` — no `Activator.CreateInstance`, no `GetType().GetFields()`, no LINQ. This is what makes the published build trim-safe.

### Serializable Fields

The generator picks up every `public` non-`static` field on the component (and its non-`Component` base classes, up to but not including `Component` itself). Fields of these types are supported out of the box:

| Category | Types |
|---|---|
| Primitives | `bool`, `int`, `uint`, `long`, `float`, `double`, `string` |
| Enums | Any `enum` |
| Engine value types | `Vector2`, `Color`, `RectangleF` |
| User structs | Plain structs with only public fields (no explicit constructor — see gotchas) |
| `IComponentGroup` | Classes implementing `IComponentGroup` with public fields — the generator emits both a `Read_` and a `Clone_` helper |
| Collections | `List<T>`, `Dictionary<string, TValue>`, `T[]` of any above |
| References | `ComponentReference`, `EntityReference` (resolved post-load) |

Fields marked `[JsonExclude]` are skipped entirely. Private fields are skipped unless marked `[SerializedField]` (`Voltage.SerializedFieldAttribute`).

### Serialization Attributes

| Attribute | Where | What it does |
|---|---|---|
| `[JsonExclude]` | Field or property | Excluded from serialization. Applied to `Entity.Scene`, `Component.Entity`, `Component.Transform`, and similar back-references to prevent cycles. |
| `[DecodeAlias("oldName")]` | Field in a `ComponentData` class | When loading JSON, a key matching `oldName` is mapped to this field. Use this when a field is renamed to keep old save files loading correctly. |
| `[FormerlyKnownAs("Old.Namespace.ClassName")]` | Component class | When the class or its namespace is renamed, the source generator registers the old name in `TypeRenameRegistry` so scenes that reference the old name still load. Pass multiple old names to handle a chain of renames. |
| `[DynamicallyAccessedMembers(...)]` | Engine internals | Preserves reflection metadata for trim-sensitive code paths. You will rarely need this in game scripts. |

### FormerlyKnownAs Example

```csharp
// Class was "Jolt.Scripts.Enemies.BatEnemy", then moved to "Jolt.Scripts.BatEnemy".
[Voltage.Serialization.FormerlyKnownAs(
    "Jolt.Scripts.Enemies.BatEnemy",
    "Jolt.Scripts.BatEnemy")]
public partial class BatController : Component, IUpdatable
{
    public float Speed = 80f;
    public void Update() { /* ... */ }
}
```

Both old names are registered at module init. Any `.vscene` or `.vprefab` that references either old name will load and instantiate `BatController` correctly.

### The Build System

`GameBuilder.BuildGameAsync` in the editor orchestrates the publish:

1. Builds the engine DLLs without the `EDITOR` preprocessor symbol.
2. Runs `dotnet publish` on the game project with NativeAOT and trimming enabled.
3. Copies the `Content/` folder and the `Data/` folder (scenes, prefabs) into the build output.
4. Compiles custom effect shaders from `Effects/` using `EffectsCompiler`.

The output lands in `Build/<Configuration>/<platform>/` inside the project folder. Each `dotnet publish` target platform gets its own subfolder.

The `[ModuleInitializer]` pattern guarantees that AOT registrations run before any deserialization is attempted — no runtime reflection needed.

### Editor Hot-Reload vs Published Build

In the editor the `DynamicScripts` assembly is loaded via reflection. This is fine because the editor runs on full .NET with a JIT. When the editor recompiles scripts it sets `Core.LatestScriptAssembly` so type resolution finds the newest types.

In a published build there is no `DynamicScripts` assembly. All game code is statically compiled into the executable. The source generator's `[ModuleInitializer]` registrations replace the runtime reflection that the editor uses.

---

## 6. Scripting Rules and Gotchas

### Do

- **Always declare serialized components `partial`.**
  Any `Component` or `SceneComponent` subclass that you want saved/loaded must be `partial`. If you forget, the class still works in code but its public fields reset to defaults on every scene reload.

- **Use `SpriteRenderer.LoadPngFile` / `LoadAsepriteFile` / `LoadTmxFile` from code, not raw content loading.**
  These methods update the component's internal `_data` so the file path is saved with the scene. Direct calls to `Scene.Content.LoadTexture` and `SetSprite(new Sprite(tex))` work visually but the path is not serialized.

- **Register audio components with `AudioComponentRegistry`.**
  Implement `IAudioComponent`, call `AudioComponentRegistry.Register(this)` in `OnAddedToEntity`, and `Unregister` in `OnRemovedFromEntity`. Guard your own `Play` calls with `Core.IsAudioOn`.

- **Use `[DecodeAlias]` when renaming a field, `[FormerlyKnownAs]` when renaming a class or moving a namespace.**
  Without these, old save files will silently lose data for renamed fields, or fail to find the type for renamed classes.

- **Use `IUpdatableInPauseMode` for UI and menus.**
  Without it, your component stops updating the moment the user presses Pause.

- **Initialize `IComponentGroup` fields at the declaration site.**
  ```csharp
  public MyGroup Settings = new MyGroup();  // correct
  public MyGroup Settings;                  // null at snapshot time → logged error
  ```

- **Prefer `Scene.Content` for scene-lifetime assets, `Core.Content` for cross-scene assets.**
  `Scene.Content` is disposed when the scene ends. Using `Core.Content` for a texture that is only needed in one scene leaks memory.

### Do Not

- **Do not use structs with explicit parameterless constructors as serializable fields.**
  The source generator emits `new T()` in the AOT reader, which ignores any field defaults set inside an explicit constructor. The editor works (reflection is available) but the published build silently drops those defaults. The generator emits a compile-time error (`VLT003`) when it detects this pattern. Use a class implementing `IComponentGroup` instead.

- **Do not save scene data in Play or Pause mode.**
  `Ctrl+S` is blocked. Changes made during Play mode are intended to be discarded when you return to Edit mode (or when `Core.ResetSceneAutomatically` reloads the scene).

- **Do not rely on `Entity.OriginalPrefabGuid` at runtime.**
  It is always `Guid.Empty` in a published game. Use `Entity.OriginalPrefabName` for runtime prefab identity.

- **Do not cache `Core.Scene.Content` across scene transitions.**
  The content manager is disposed when the scene ends. Cache the asset reference instead, not the manager.

- **Do not delete `.meta` files while the project is open.**
  The editor regenerates them with a new GUID, breaking all editor-side prefab references that pointed to the old one.

- **Do not apply `[FormerlyKnownAs]` to an abstract class.**
  The attribute is `Inherited = false`. Apply it to each concrete class that needs the rename registered.

---

## 7. Engine Engineer Notes

This section covers internals that game developers using the prebuilt editor do not need to know.

### Core Lifecycle

`Core : Game` is the MonoGame `Game` subclass. It owns:
- The active `Scene` and the queued `_nextScene` (swap happens at end of `Update`).
- `GlobalManager` instances (coroutine manager, tween manager, timer manager, render target).
- The `Emitter<CoreEvents>` event bus for engine-level events (`SceneChanged`, `GraphicsDeviceReset`, `OrientationChanged`, `Exiting`).
- Static events `OnSwitchEditMode`, `OnSwitchPauseMode`, `OnSwitchAudio`, `OnResetScene` for mode changes.

In `EDITOR` builds the title bar is updated with FPS and memory each second, and `drawCalls` is tracked via `GraphicsDevice.Metrics`.

`Core.IsEditMode` is set to `true` on construction in `EDITOR` builds and `false` in standalone builds. The editor sets it to `false` when Play is pressed.

### Scene Resolution Policy

`Scene.SetDesignResolution(width, height, SceneResolutionPolicy)` sets the internal render target size and the final blit rectangle. Policies:

| Policy | Behavior |
|---|---|
| `None` | Render target = screen size. No scaling. |
| `ExactFit` | Fixed render target; stretched to fill screen. May distort. |
| `ShowAll` | Letterboxed to preserve aspect ratio. |
| `ShowAllPixelPerfect` | Integer-only scale. |
| `NoBorder` | Crops to fill screen. No distortion. |
| `FixedHeight` / `FixedWidth` | Fixes one axis; the other adapts to device aspect ratio. |
| `BestFit` | Bleed-area based; gives a safe zone. |

`Scene.SetDefaultDesignResolution` sets the policy for all future scenes.

### Persistence Layer

`Voltage.Persistence.Json` (custom) is used for all scene and component serialization. It is not `System.Text.Json` or `Newtonsoft.Json`, though it shares some naming. `JsonSettings` controls `PrettyPrint`, `TypeNameHandling`, and `PreserveReferencesHandling`.

`AotDeserializers.DeserializeSceneData` and `DeserializePrefabData` are the scene-load entry points in published builds. They call registered `ComponentDataAotDeserializer` functions that were registered by the source generator's `[ModuleInitializer]` methods.

`TypeRenameRegistry` maps old fully-qualified type names to current `Type` objects. It is populated by the generated `ComponentDataAotBootstrap.AutoRegister` based on `[FormerlyKnownAs]` attributes.

### Entity Instance Types

| `InstanceType` | Meaning |
|---|---|
| `NonSerialized` | Code-only. Not saved. Cannot be selected in editor. |
| `Serialized` | Default editor entity. Saved in `.vscene`. |
| `SerializedPrefab` | Instantiated from a `.vprefab`. Saved as a delta against the prefab. |
| `SceneRequired` | Always present; cannot be deleted. Currently used for the main Camera entity. |

### Adding a New Serializable Engine Component

1. Declare the component `partial`.
2. If the serialization is complex (e.g., `SpriteRenderer`'s file-type union), write a manual `ComponentData` subclass and override `Data` yourself. The generator skips partial classes that already have a `Data` override.
3. If using manual `Data`, register a deserializer in `ComponentDataAotBootstrap` or via a `[ModuleInitializer]` so NativeAOT builds can deserialize it.
4. Add the component type to any AOT reflection annotations (`[DynamicDependency]`) if its data type is referenced only via a string name.

---

## 8. Documentation Site

This repository (`Voltage.github.io`) also hosts the Voltage documentation website, built with [Docusaurus 2](https://v2.docusaurus.io/).

**Install dependencies:**

```bash
yarn install
```

**Local development server** (live-reloads on change):

```bash
yarn start
```

**Build static site:**

```bash
yarn build
```

The output goes to the `build/` directory and can be served by any static hosting service.

**Deploy to GitHub Pages:**

```bash
GIT_USER=<your-github-username> USE_SSH=true yarn deploy
```

This builds the site and pushes it to the `gh-pages` branch.
