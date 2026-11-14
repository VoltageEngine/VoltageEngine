---
title: Collections, Pooling and Math
sidebar_position: 2
description: Allocation-friendly collections, object pools, bit flags, random numbers and math helpers.
---

# Collections, Pooling and Math

## Collections

`Voltage.Utils.Collections` holds collections tuned for per-frame game code.

| Type | Use |
|---|---|
| `FastList<T>` | A growable array without bounds checks or enumerators. Iterate `Buffer` up to `Length`; `RemoveAtWithSwap` removes in constant time when order doesn't matter. |
| `Deque<T>` | A double-ended queue: `AddFront`, `AddBack`, `RemoveFront`, `RemoveBack` in constant time. Also a full `IList<T>`. |
| `PriorityQueue<T>` | A fast binary heap for nodes deriving from `PriorityQueueNode`, with `UpdatePriority`; fixed maximum size. Used by the pathfinders. |
| `SimplePriorityQueue<T>` | The same behaviour for any type, without a size limit or node base class. |
| `Pair<T>` | A mutable pair of references (`First`, `Second`) that compares by contents, handy as a dictionary key. |

```csharp
var list = new FastList<Enemy>();
list.Add(enemy);
for (var i = 0; i < list.Length; i++)
    list.Buffer[i].Think();

var open = new SimplePriorityQueue<Point>();
open.Enqueue(start, 0);
Point next = open.Dequeue();
```

## Pooling

`Pool<T>` reuses objects instead of allocating new ones. Types that implement `IPoolable` are reset when returned.

```csharp
public class Bullet : IPoolable
{
    public Vector2 Velocity;
    public void Reset() => Velocity = Vector2.Zero;
}

Pool<Bullet>.WarmCache(64);          // allocate up front
var b = Pool<Bullet>.Obtain();
Pool<Bullet>.Free(b);                 // calls Reset, back to the pool
Pool<Bullet>.TrimCache(16);
Pool<Bullet>.ClearCache();
```

`ListPool<T>` does the same for `List<T>`, clearing each list when it is freed:

```csharp
var nearby = ListPool<Collider>.Obtain();
// fill and use
ListPool<Collider>.Free(nearby);
```

Some engine calls hand you pooled lists; `Entity.GetComponents<T>()` returns one you may give back with `ListPool<T>.Free` when you are done.

## Flags

Physics layers and masks are bit fields. `Voltage.Flags` edits them:

```csharp
int mask = 0;
Flags.SetFlag(ref mask, 2);            // turn on bit 2
Flags.UnsetFlag(ref mask, 0);
Flags.SetFlagExclusive(ref mask, 3);   // only bit 3
bool on = mask.IsUnshiftedFlagSet(2);  // test by bit index
bool hit = layers.IsFlagSet(PhysicsLayer.GetPhysicsLayerBit("Ground"));   // test by bit value
string bits = mask.BinaryStringRepresentation();
```

## Random

`Voltage.Random` wraps a shared generator: `NextFloat()`, `Range(min, max)`, `NextInt(max)`, `Chance(0.25f)`, `Choose(a, b, c)`, `MinusOneToOne()`, `NextAngle()`, `NextUnitVector()` and `NextColor()`. `SetSeed` makes sequences repeatable, for replays and tests.

## Math helpers

| Type | Contents |
|---|---|
| `Mathf` | Float versions of common functions plus game helpers such as `Approach`, `Map`, `Clamp01`, `SignThreshold`, angle and rounding utilities. |
| `RectangleF` | Floating-point rectangle used for bounds. |
| `Matrix2D` | 2D affine transform used by the camera and transforms. |
| `Bezier`, `BezierSpline` | Curve evaluation and splines, for paths and SVG curves. |
| `SubpixelFloat`, `SubpixelVector2` | Accumulate fractional movement and apply whole pixels, for crisp pixel-art motion. |
| `Vector2Ext`, `RectangleExt`, `ColorExt` and other extensions | Rotation, normalisation, colour mixing and similar helpers. |
