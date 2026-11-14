---
title: Your First Game
sidebar_position: 2
description: Create a project, place a sprite, and write a component that moves it in Play mode.
---

# Your First Game

This walkthrough creates a project, places a sprite in its scene, and writes a component that moves the sprite with the arrow keys.

## Create a project

1. Launch the Voltage Editor.
2. Choose **File > New Project**, enter a **Project Name** and **Project Path**, and click **Create Project**.
3. The editor writes the [project layout](../editor/projects.md) and opens the project. A new project contains `Scenes/MainScene.vscene`, which is the startup scene, and `Scripts/ExampleComponent.cs`, a component that moves its entity with WASD.

Every scene has a **Camera (Main)** entity. It is scene-required, so it cannot be deleted. To add more scenes, use **Project > New Scene**; the folder defaults to `Scenes/`, but a scene can live in any folder inside the project.

## Place a sprite

1. Copy `hero.png` into `Content/Characters/` inside the project. The Asset Browser picks it up and gives it a `.meta` file with a stable GUID.
2. Drag `hero.png` from the Asset Browser into the game view. The editor creates an entity named after the file, with a `SpriteRenderer` showing the image at the drop position.
3. Press `Ctrl+S` to save the scene.

## Write a component

In the Asset Browser, right-click the `Scripts` folder and choose **Create > New Script > Component**. Name it `HeroMover` and replace its contents:

```csharp
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Voltage;
using Voltage.Utils;

public partial class HeroMover : Component, IUpdatable
{
    public float Speed = 120f;

    public void Update()
    {
        var dir = Vector2.Zero;
        if (Input.IsKeyDown(Keys.Left))  dir.X -= 1f;
        if (Input.IsKeyDown(Keys.Right)) dir.X += 1f;
        if (Input.IsKeyDown(Keys.Up))    dir.Y -= 1f;
        if (Input.IsKeyDown(Keys.Down))  dir.Y += 1f;
        Entity.Position += dir * Speed * Time.DeltaTime;
    }
}
```

Save the file. The editor recompiles the scripts and reloads the scene. The class is `partial` so the source generator can save its public fields: `Speed` keeps the value you set in the Inspector. See [Serialization and NativeAOT](../engine/serialization-and-aot.md).

## Attach the component

1. Select the `hero` entity in the Scene Graph.
2. In the Inspector, click **Add Component** and choose `HeroMover`.
3. Press `Ctrl+S` to save the scene.

## Press Play

Click **Play** or press `F1`. In Edit mode only renderable components update; in Play mode every enabled `IUpdatable` runs, so the arrow keys now move the sprite.

Press `F1` again to stop. With `Core.ResetSceneAutomatically` on (the default), stopping reloads the scene from disk, so the sprite returns to its saved position. `F2` pauses and resumes Play mode.

## Next

- [Components and Lifecycle](../scripting/components.md) for the full callback order and Edit/Play behaviour.
- [Play Mode](../editor/play-mode.md) for the game view options.
- [Game Build](../editor/game-build.md) to publish the game.
