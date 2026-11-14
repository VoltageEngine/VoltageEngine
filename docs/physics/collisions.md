---
title: Collisions and Movement
sidebar_position: 1
description: Colliders, physics layers, the spatial hash, physics queries, movers, triggers, one-way platforms, arcade rigid bodies and tilemap collision.
---

# Collisions and Movement

Voltage's built-in physics is designed for platformers and top-down games: precise collision tests, queries and movement that stops at walls, rather than a full rigid-body simulation. For stacking, joints and realistic dynamics, install the **Farseer Physics** plugin (`voltage.farseer`) from **Plugins > Plugin Manager > Browse Plugins**. It adds the `FS*` components such as `FSRigidBody`, collision shapes, joints and `FSWorld`. See [Plugins](../plugins/README.md).

## Colliders

| Collider | Shape |
|---|---|
| `BoxCollider` | Axis-aligned rectangle, or a rotated box when the entity rotates. `new BoxCollider(width, height)` centres it on the entity. |
| `CircleCollider` | Circle with `Radius`. |
| `PolygonCollider` | Convex polygon from `Points`. Edit its vertices in the editor with the collider gizmo. |

Without a size, a collider added in code fits itself to the entity's renderable. Common settings:

| Setting | Meaning |
|---|---|
| `IsTrigger` | Reports overlaps but never blocks movement. |
| `PhysicsLayer` (`Layer` in code) | The layer this collider is on, a single bit. |
| `CollidesWithLayers` | Mask of layers this collider tests against. Defaults to all layers. |
| `LocalOffset` | Offset from the entity's position. |
| `ShouldColliderScaleAndRotateWithTransform` | Follow the entity's scale and rotation. On by default. |
| `OneWayNormal` | Makes a one-way platform: movement is only blocked along this direction. `(0, -1)` lets bodies jump up through and land on top. |

Layers are named in **Project > Settings > Physics Layers** (Default and Ground by default). In code, `PhysicsLayer.GetPhysicsLayerBit("Ground")` and `PhysicsLayer.GetPhysicsLayerMask("Default", "Ground")` turn names into bits and masks; `Flags.IsFlagSet`, `SetFlag` and `UnsetFlag` edit masks; `collider.AddLayer` and `IgnoreLayer` change `CollidesWithLayers`.

Every enabled collider registers itself in a spatial hash, a grid of cells that makes queries cheap. `Physics.SpatialHashCellSize` (default 100 pixels) is read when a scene begins. Debug rendering outlines colliders, and the `physics` console command reports how many are registered.

## Queries

```csharp
// first collider along a line
RaycastHit hit = Physics.Linecast(eye, target, PhysicsLayer.GetPhysicsLayerMask("Ground"));
if (hit.Collider != null) Debug.DrawLine(eye, hit.Point, Color.Red, 1f);

// colliders in an area
Collider any = Physics.OverlapCircle(center, 48f, enemyMask);
var results = new Collider[16];
int count = Physics.OverlapCircleAll(center, 48f, results, enemyMask);
var rect = new RectangleF(x, y, w, h);
int inRect = Physics.OverlapRectangleAll(ref rect, results, enemyMask);

// broadphase: bounds only, then refine
foreach (var other in Physics.BoxcastBroadphaseExcludingSelf(myCollider, myCollider.CollidesWithLayers))
    if (myCollider.Overlaps(other)) { }
```

`RaycastHit` has `Collider`, `Point`, `Normal`, `Distance` and `Fraction`. `LinecastAll` returns every hit along the line. `Physics.RaycastsHitTriggers` (off by default) and `Physics.RaycastsStartInColliders` change what lines report.

A collider can also test itself directly: `Overlaps(other)`, `CollidesWith(other, out CollisionResult result)`, `CollidesWith(other, motion, out result)` and `CollidesWithAny(ref motion, out result)`. A `CollisionResult` carries the other `Collider`, the contact `Normal`, `Point` and the `MinimumTranslationVector` that separates the shapes. `Voltage.Collisions` has shape-level helpers such as `LineToLine`, `CircleToLine` and `RectToCircle`.

## Movement with Mover

Add `Mover` to move an entity and stop at solid colliders. It also drives triggers.

```csharp
public partial class Walker : Component, IUpdatable
{
    Mover _mover;
    public float Speed = 120f;

    public override void OnStart() => _mover = GetComponent<Mover>();

    public void Update()
    {
        var motion = new Vector2(Input.IsKeyDown(Keys.D) ? 1 : Input.IsKeyDown(Keys.A) ? -1 : 0, 0) * Speed * Time.DeltaTime;
        if (_mover.Move(motion, out CollisionResult hit))
            Debug.Log($"Bumped into {hit.Collider.Entity.Name}");
    }
}
```

`Move` adjusts the motion so the entity stops at the first obstacle, applies it and returns whether it hit something. `CalculateMovement` and `ApplyMovement` split those steps, and `AdvancedCalculateMovement` reports every collision. `ProjectileMover` is a lighter version for bullets: it moves without being blocked and reports what it touched.

## Triggers

A component that implements `ITriggerListener` hears when a collider on its entity overlaps a trigger, or when a collider enters its entity's trigger:

```csharp
public partial class Coin : Component, ITriggerListener
{
    public void OnTriggerEnter(Collider other, Collider local) => Entity.Destroy();
    public void OnTriggerStay(Collider other, Collider local) { }      // optional, every frame inside
    public void OnTriggerExit(Collider other, Collider local) { }
}
```

Trigger events are produced by `Mover` and `ProjectileMover`, so at least one side of the pair must move through one of them. An entity moved by setting `Position` directly passes through triggers silently. Audio zones and other trigger-based components rely on the same mechanism.

## Arcade rigid body

`ArcadeRigidbody` adds simple dynamics: velocity, gravity (`Physics.Gravity`, default `(0, 300)`), mass (0 makes it immovable), `Elasticity` (bounce), `Friction` and `Glue` (stickiness to surfaces). It resolves collisions against other colliders each frame.

```csharp
var body = Entity.AddComponent(new ArcadeRigidbody().SetMass(2f).SetElasticity(0.3f));
body.AddImpulse(new Vector2(0, -400));
body.Velocity.X = 80;
```

It is a single-pass arcade solver, suited to crates, debris and bouncing pickups, not to stacks of resting bodies.

## Tilemap collision

A [`TilemapRenderer`](../scripting/tilemaps.md) builds its colliders from a per-cell collision mask painted in the Tile Palette's Collision mode or generated from tiles marked **Solid**. Plain cells merge into as few boxes as possible; slope, circle and custom tile shapes from the tileset add individual colliders, and one-way tiles get a one-way normal. The layer's `PhysicsLayer`, `CollidesWithLayers` and `IsTrigger` apply to all of them. Prefer box cells for large areas: custom shapes cost one collider per cell.

Tiled maps loaded with `TiledMapRenderer` create colliders from a named collision layer, and `TiledMapMover` provides platformer movement against it, including slopes and one-way platforms.

## Verlet physics

For ropes, cloth, ragdolls and soft bodies, see [Verlet Physics](verlet.md).
