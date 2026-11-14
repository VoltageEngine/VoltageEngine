---
title: Content Loading
sidebar_position: 3
description: Loading assets by file path or through GUID-backed asset references, content lifetimes, caching, async loading and custom loaders.
---

# Content Loading

Voltage loads assets two ways, and both work in the editor and in published games:

- **Asset references.** A public `AssetReference` or `PrefabReference` field gives the Inspector a slot you fill by dragging from the Asset Browser. The reference stores the asset's stable GUID from its `.meta` file, so renaming or moving the file does not break it.
- **File paths.** Every loader also accepts a path such as `"Content/Characters/Hero.png"`, which suits generated or data-driven names.

Pick whichever fits. References are the safer choice for anything assigned in the editor; paths are simpler for assets you choose in code.

## Asset references

```csharp
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Voltage;
using Voltage.Serialization;

public partial class Pickup : Component
{
    public AssetReference Icon;          // drag a .png or .aseprite here
    public AssetReference Sound;         // drag a .wav, .ogg or .mp3 here
    public PrefabReference Effect;       // drag a .vprefab here

    public override void OnStart()
    {
        var icon  = Entity.Scene.LoadAsset<Texture2D>(Icon);
        var sound = Entity.Scene.LoadAsset<SoundEffect>(Sound);
    }

    public void Collect() => Effect.Instantiate(Entity.Position);
}
```

A reference resolves GUID-first. In the editor the asset database answers; in a published game the baked `Data/assets.manifest` (GUID to project-relative path, written by the editor on every asset refresh and shipped with the build) does. The path stored alongside the GUID is the fallback. If a build never opened in the editor has no manifest, references fall back to that path.

`Scene.LoadAsset<T>` loads through the scene's content manager and supports `Texture2D`, `SoundEffect`, `SpriteAtlas`, `BitmapFont`, `AsepriteFile`, `TmxMap`, `TilesetAsset`, `Effect`, `string` (raw JSON) and any type registered with `VoltageContentManager.RegisterLoader<T>`. Other types go through the MonoGame content pipeline. Data assets load with `DataAssets.Load<T>(reference)`; see [Data Assets](../editor/data-assets.md). `AssetReference.IsValid` tells you whether the slot was assigned, and `ResolvePath()` returns the current absolute path.

Several engine components already take references: `SpriteAnimator.AsepriteAsset`, `TilemapRenderer.Tileset`, `TimelineDirector.Timeline`, `AnimatedNormalMap.Source` and the audio components' clips.

## File paths

Relative paths resolve against `VoltageContentManager.ContentRoot`. The editor sets it to the open project's root; a published game uses the executable's folder (`Contents/Resources` inside a macOS app). Absolute paths are used as they are, and backslashes are normalized so paths authored on Windows load on macOS and Linux.

```csharp
var content = Entity.Scene.Content;

Texture2D   tiles  = content.LoadTexture("Content/Tiles/Grass.png");
AsepriteFile hero  = content.LoadAsepriteFile("Content/Characters/Hero.aseprite");
SpriteAtlas walk   = content.LoadAsepriteAtlas("Content/Characters/Hero.aseprite");
SoundEffect jump   = content.LoadSoundEffect("Content/Audio/Jump.wav");
TmxMap      level  = content.LoadTiledMap("Content/Maps/Level1.tmx");
BitmapFont  font   = content.LoadBitmapFont("Content/Fonts/Pixel.fnt");
TilesetAsset set   = content.LoadTileset("Content/Tiles/Cave.vtileset");
string      json   = content.LoadJson("Data/Waves.json");
```

`LoadTexture` also accepts an Aseprite file and returns its flattened first frame. A name without an extension is loaded as a compiled `.xnb`.

When the project uses the [asset build](../editor/asset-build.md), the content manager checks `Content/content.index` first and loads the compiled `.xnb` for a source path, so the same code works with raw and compiled content.

## Lifetimes and caching

| Manager | Lifetime | Use for |
|---|---|---|
| `Entity.Scene.Content` | Disposed when the scene ends | Assets used by one scene |
| `Core.Content` | Lives for the whole session | Fonts, shared atlases, UI art, music used everywhere |

Each manager caches by path: loading the same file twice returns the same object, and disposing the manager disposes what it loaded. Don't keep a reference to a scene's content manager, or to its assets, after the scene ends. `IsAssetLoaded(name)`, `UnloadAsset<T>(name)` and `EvictCachedAsset(name)` manage individual entries, and the `assets` console command lists what is loaded.

Aseprite atlases loaded through a content manager are shared: animators that use the same file and layer selection reuse one texture. Don't dispose them yourself. When you need to modify pixels or sprite data, build a private copy with `AsepriteFile.ToSpriteAtlas` and dispose it when you are done.

## Loading through components

`SpriteRenderer` and `SpriteAnimator` have loaders that also record the file in the component's saved data, so the sprite reappears after the scene is saved and reloaded:

```csharp
spriteRenderer.LoadPngFile("Content/Characters/Hero.png");
spriteRenderer.LoadAsepriteFile("Content/Characters/Hero.aseprite", layerName: "Body", frameNumber: 0);
spriteRenderer.LoadTmxFile("Content/Maps/Level1.tmx", imageLayerName: "Background");
```

Assigning a sprite with `SetSprite(new Sprite(texture))` works at runtime, but nothing is saved: use it for sprites your code creates every time.

## Async loading

```csharp
Core.Content.LoadAsync<Texture2D>("Content/Art/Title.png", tex => _title = tex);
```

`LoadAsync` loads on a worker thread and invokes the callback on the main thread. Overloads take a context object, or several names with one completion callback. It goes through MonoGame's content pipeline, so use it for compiled content; load raw PNG files on the main thread.

## Effects

| Method | Loads |
|---|---|
| `LoadEffect(path)` / `LoadEffect<T>(path)` | A compiled project shader (`.mgfxo`) from `Content/Effects`. See [Effects](../effects.md). |
| `LoadEffect<T>(name, bytes)` | An effect from raw bytecode. |
| `LoadVoltageEffect<T>()` | One of the engine's built-in effects, such as `SpriteBlinkEffect`. |
| `LoadMonoGameEffect<T>()` | A MonoGame stock effect, such as `BasicEffect`. |

## Custom asset types

Register a loader once, usually from a `[ModuleInitializer]`, and `Scene.LoadAsset<T>` and asset references work for your type:

```csharp
VoltageContentManager.RegisterLoader<DialogueTable>((content, path) => DialogueTable.Read(path));
```

For JSON asset formats with their own extension, icons and editor support, see [Asset File Formats](../assets/README.md).
