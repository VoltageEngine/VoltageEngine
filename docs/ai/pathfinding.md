---
title: Pathfinding
sidebar_position: 2
description: Breadth-first, Dijkstra and A* search over grids or any graph you define.
---

# Pathfinding

`Voltage.AI.Pathfinding` provides three searches. Each works on any graph that implements its interface, and each has a ready-made grid graph.

| Search | Graph interface | Grid graph | Use when |
|---|---|---|---|
| Breadth-first | `IUnweightedGraph<T>` | `UnweightedGridGraph` | Every step costs the same. |
| Dijkstra | `IWeightedGraph<T>` | `WeightedGridGraph` | Steps have different costs (mud, water). |
| A* | `IAstarGraph<T>` | `AstarGridGraph` | Like Dijkstra, guided by a distance heuristic; usually the fastest choice for grids. |

## Grids

```csharp
using Microsoft.Xna.Framework;
using Voltage.AI.Pathfinding;

var graph = new AstarGridGraph(width: 40, height: 23);
graph.Walls.Add(new Point(10, 5));
graph.WeightedNodes.Add(new Point(12, 7));    // costs WeightedNodeWeight (5) instead of DefaultWeight (1)

List<Point> path = graph.Search(new Point(1, 1), new Point(30, 18));   // null when unreachable
```

Grid graphs move in the four cardinal directions; the unweighted and Dijkstra grids take `allowDiagonalSearch: true` for eight. Each grid graph also has a constructor that reads walls from a Tiled `TmxLayer`, treating every filled tile as a wall.

For a native [tilemap](../scripting/tilemaps.md), fill the walls from its collision mask. `TileExtents` is the bounding box of the painted cells in tile coordinates, which can start below zero, so offset by it:

```csharp
var map = GetComponent<TilemapRenderer>();
var extents = map.TileExtents;
var graph = new AstarGridGraph(extents.Width, extents.Height);
foreach (var (x, y) in map.EnumerateCollision())
    graph.Walls.Add(new Point(x - extents.X, y - extents.Y));
```

Convert results back to the world with `map.TileToWorld(x + extents.X, y + extents.Y)`.

## Your own graphs

Implement the interface for waypoints, navigation meshes or anything else:

```csharp
var graph = new UnweightedGraph<string>()
    .AddEdgesForNode("Hall", new[] { "Kitchen", "Stairs" })
    .AddEdgesForNode("Stairs", new[] { "Hall", "Attic" });
var route = BreadthFirstPathfinder.Search(graph, "Kitchen", "Attic");
```

`IWeightedGraph<T>` adds `Cost(from, to)`, and `IAstarGraph<T>` adds `Heuristic(node, goal)`. `WeightedPathfinder.Search` and `AStarPathfinder.Search` run them, and overloads that return the `cameFrom` map let you reuse one search for several queries. The searches use the [priority queues](../utilities/collections-and-math.md) from `Voltage.Utils.Collections`.
