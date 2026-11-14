---
title: Tilemaps
sidebar_position: 3.5
description: Reading and writing native tilemap layers from code, tilemap collision, and loading Tiled maps.
---

# Tilemaps

Voltage has two tilemap paths. Native tilemaps are painted in the editor with the [Tile Palette](../editor/tile-palette.md) from `.vtileset` assets and saved in the scene. Tiled maps (`.tmx`) are loaded from files made in the Tiled editor.

## Native tilemaps

Each layer is one `TilemapRenderer` component that draws from one tileset, so a level usually has several (background, main, foreground), each with its own render layer, collision mask and colour. Cells are stored in sparse 32 by 32 chunks, so maps can be large and extend in any direction, including negative coordinates.

```csharp
var ground = GetComponent<TilemapRenderer>();

Point cell = ground.WorldToTile(Core.Scene.Camera.MouseToWorldPoint());
int tile = ground.GetTile(cell.X, cell.Y);          // top tile of the cell, -1 when empty
ground.SetTile(cell.X, cell.Y, 12);                  // tile index in the tileset
ground.EraseTile(cell.X, cell.Y);
Vector2 world = ground.TileToWorld(cell.X, cell.Y);

foreach (var (x, y, index) in ground.EnumerateTiles()) { }
Rectangle painted = ground.TileExtents;              // bounding box of painted cells, in tiles
```

A cell can stack up to 8 tiles (`PushTile`, `GetCellStack`, `SetCellStack`), for example a decoration over a floor tile. Each cell can be rotated in quarter turns and flipped: `SetOrientation(x, y, TilemapRenderer.MakeOrientation(quarterTurns, flipX, flipY))`. The renderer follows the entity's transform, so moving, scaling or rotating the entity moves the whole layer.

Tile behaviour comes from the tileset: per-tile names, animation frames (played from `Time.TotalTime`), terrain masks for autotiling, and collision shapes. `ReloadTileset()` re-reads the tileset after it changes; in the editor this happens automatically when a `.vtileset` is saved. When the scene uses deferred lighting and the tileset has a normal map, the layer is lit like a sprite (`ApplyDeferredMaterial`).

### Collision

Collision is a separate mask over cells: a cell can be solid without a tile, or hold a tile without being solid.

```csharp
ground.SetCollision(cell.X, cell.Y, true);
bool solid = ground.GetCollision(cell.X, cell.Y);
int count = ground.GenerateCollisionFromTiles(onlyTilesetSolidFlagged: true);
ground.RebuildColliders();
```

`RebuildColliders` turns the mask into colliders: plain box cells merge into as few rectangles as possible (`GetCollisionRects()` returns them), while slope, circle, custom-polygon and one-way tiles from the tileset become individual colliders. The layer's `PhysicsLayer`, `CollidesWithLayers` and `IsTrigger` apply to all of them, and with `AutoBuildColliders` on they rebuild when the scene starts or the entity moves. The colliders are registered directly with the physics system rather than as components, so they work with `Mover`, `Physics.Linecast` and every other query. See [Collisions](../physics/collisions.md).

The gateway's `tilemap.*` commands (paint, fill, erase, collision, generate collision) let scripts and agents edit layers too.

## Tiled maps

Load a `.tmx` map and draw it with `TiledMapRenderer`:

```csharp
using Voltage.Tiled;

var map = Entity.Scene.Content.LoadTiledMap("Content/Maps/Level1.tmx");
var renderer = Entity.AddComponent(new TiledMapRenderer(map, collisionLayerName: "Collision"));
renderer.SetLayersToRender("Ground", "Props");
renderer.PhysicsLayer = PhysicsLayer.GetPhysicsLayerBit("Ground");

var spawns = map.GetObjectGroup("Spawns");
```

In the editor, **File > Load Tiled Map** adds a map to the scene. Every non-empty tile in the collision layer becomes a collider, merged into rectangles. Draw layers at different depths with one renderer per render layer and `SetLayersToRender` on each. `TiledMapRenderer` also offers `GetTileAtWorldPosition`, `GetTilesIntersectingBounds`, and row and column lookups. The map's `ObjectGroups`, `ImageLayers`, `Groups` and custom `Properties` are available for spawning entities from Tiled data, and `SpriteRenderer.LoadTmxFile` draws a Tiled image layer.

`TiledMapMover` moves a box collider against a Tiled collision layer with platformer rules, reporting `Below`, `Above`, `Left`, `Right`, `SlopeAngle` and one-way platforms through its `CollisionState`.

Native tilemaps are the recommended path for new projects: they are painted in the editor, saved in the scene, lit, animated and edited live, while Tiled maps suit existing Tiled workflows.
