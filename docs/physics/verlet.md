---
title: Verlet Physics
sidebar_position: 2
description: Particles, constraints and composites for ropes, cloth, ragdolls and soft bodies.
---

# Verlet Physics

`VerletWorld` (namespace `Voltage.Verlet`) simulates particles joined by constraints. It suits ropes, chains, cloth, hanging signs, ragdolls and soft bodies. It only simulates: you decide how to draw the result, and composites do not collide with each other. Particles and constraints can collide with the scene's regular colliders.

```csharp
using Microsoft.Xna.Framework;
using Voltage;
using Voltage.Verlet;

public partial class HangingBanner : Component, IUpdatable
{
    VerletWorld _world;

    public override void OnStart()
    {
        _world = new VerletWorld(new Rectangle(0, 0, 1280, 720));
        var cloth = _world.AddComposite(new Cloth(Entity.Position, 120, 80, segments: 12, stiffness: 0.3f));
    }

    public void Update() => _world.Update();

    public override void DebugRender(Batcher batcher) => _world.DebugRender(batcher);
}
```

`VerletWorld` settings: `Gravity` (default `(0, 980)`), `ConstraintIterations` (more is stiffer and slower), `MaximumStepIterations`, `SimulationBounds` (particles stay inside), and `AllowDragging`, which lets you grab the nearest particle with the mouse while testing. `GetNearestParticle(position)` finds a particle to attach gameplay to.

## Particles

A `Particle` has `Position`, `Mass`, `Radius` and `CollidesWithColliders`. `Pin()` fixes it where it is, `PinTo(position)` fixes it somewhere else, `Unpin()` releases it, and `ApplyForce(force)` pushes it.

## Constraints

| Constraint | Keeps |
|---|---|
| `DistanceConstraint` | Two particles at a resting distance. `Stiffness` from 0 (loose) to 1 (rigid); `TearSensitivity` breaks the link when stretched too far. `SetShouldApproximateCollisionsWithPoints` lets long links collide along their length. |
| `AngleConstraint` | The angle formed by three particles, for bendy but springy shapes. |

Derive from `Constraint` and implement `Solve()` for your own rules; override `HandleCollisions` and `DebugRender` as needed.

## Composites

A `Composite` groups particles and constraints. Build one at runtime with `AddParticle` and `AddConstraint`, or use a ready-made one:

| Composite | Builds |
|---|---|
| `Ball` | A single colliding particle. |
| `Box` | A springy box. |
| `Cloth` | A grid of particles; its top row is pinned. |
| `LineSegments` | A rope or chain through a list of points. |
| `Ragdoll` | A jointed humanoid. |
| `Tire` | A wheel of spokes and tread. |
| `Tree` | A branching tree that sways. |

`Friction` damps a composite's motion, `CollidesWithLayers` limits which colliders it hits, and `DrawParticles`/`DrawConstraints` control its debug drawing.
