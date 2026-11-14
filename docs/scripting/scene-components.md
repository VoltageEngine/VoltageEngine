---
title: Scene Components
sidebar_position: 2
description: Scene-scoped behaviour that belongs to the scene rather than to an entity.
---

# Scene Components

A `SceneComponent` belongs to the scene instead of an entity. Use it for scene-wide managers such as spawners, wave controllers or level rules. Scene components are saved in the `.vscene` file, so their public fields are edited and persisted like component fields.

```csharp
using Voltage;

public partial class WaveManager : SceneComponent
{
    public int CurrentWave = 1;

    public override void OnStart()
    {
        // The scene's entities are loaded.
    }

    public override void Update()
    {
        if (Core.IsEditMode) return;
        // Wave logic.
    }
}
```

Declare scene components `partial` for the same reason as components: the generator emits their save and load code.

## Adding one

In the editor, open the **Scene Components** section of the Scene Graph and press **Add Scene Component**. Remove one from its header's context menu.

From code:

```csharp
var waves = Core.Scene.AddSceneComponent<WaveManager>();
var same  = Core.Scene.GetSceneComponent<WaveManager>();
var lazy  = Core.Scene.GetOrCreateSceneComponent<WaveManager>();
Core.Scene.RemoveSceneComponent<WaveManager>();
```

## Lifecycle

| Callback | When it runs |
|---|---|
| `OnEnabled()` | When added to a running scene, or after scene data is applied during loading. |
| `OnStart()` | Once, after `OnEnabled`. Scene components saved in the scene start after the scene's entities are loaded. |
| `Update()` | Every frame before entities update, from the highest `UpdateOrder` to the lowest. Skipped while Play is paused. |
| `OnDisabled()` | When `Enabled` is set to `false`. |
| `OnRemovedFromScene()` | When the component is removed or the scene ends. |

Unlike entity components, `SceneComponent.Update` also runs while the editor is in Edit mode. Return early on `Core.IsEditMode` when the logic is gameplay. `SetUpdateOrder` changes the order among scene components; `Enabled` toggles one without removing it.

Scene components live and die with their scene. For a system that must survive scene changes, use a [global manager](core.md#global-managers).
