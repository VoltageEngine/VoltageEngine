---
title: Components and Lifecycle
sidebar_position: 1
description: Writing components, the order of lifecycle callbacks, Edit/Play behaviour and the entity API.
---

# Components and Lifecycle

Game behaviour lives in components. An `Entity` is a sealed container with a `Transform` and a list of components; you never subclass it. Derive from `Component` and attach the component to an entity in the editor or from code.

```csharp
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Voltage;
using Voltage.Utils;

public partial class PlayerController : Component, IUpdatable
{
    public float MoveSpeed = 200f;

    public override void OnStart()
    {
        if (Core.IsEditMode) return;   // see "Edit and Play" below
        // Gameplay setup: other components on this entity are available here.
    }

    public void Update()
    {
        var dir = Vector2.Zero;
        if (Input.IsKeyDown(Keys.Left))  dir.X -= 1f;
        if (Input.IsKeyDown(Keys.Right)) dir.X += 1f;
        Entity.Position += dir * MoveSpeed * Time.DeltaTime;
    }
}
```

Declare components `partial`. The source generator then emits the code that saves and loads public fields, so values set in the Inspector survive saving and reloading. A non-partial component still runs, but its fields reset to their defaults on every load. See [Serialization and NativeAOT](../engine/serialization-and-aot.md).

## Lifecycle

| Callback | When it runs |
|---|---|
| `OnAddedToEntity()` | Immediately inside `AddComponent`, before the component joins the entity's live list. `Entity` and `Transform` are set. Other components may not be ready. |
| `OnEnabled()` | When the component becomes active: after it is committed to the entity, and whenever the component or its entity is re-enabled. |
| `OnStart()` | Once, after `OnEnabled`. Every component added in the same batch has had `OnEnabled` first, so it is safe to look up siblings and other entities. |
| `Update()` | Every frame for components that implement `IUpdatable`, in `UpdateOrder`. |
| `OnEntityTransformChanged(Transform.Component)` | When the entity's position, rotation or scale changes, including through a parent. |
| `OnDisabled()` | When the component or its entity is disabled. |
| `OnRemovedFromEntity()` | When the component is removed or its entity is destroyed. Unsubscribe from events and release resources here. |
| `DebugRender(Batcher)` | When debug rendering is on. Draw gizmos for your component. |

Components added during another component's `OnStart` or `Update` receive `OnEnabled` and `OnStart` before their first `Update`. An exception thrown in any callback is logged with the entity and component name; it does not stop the rest of the scene.

## Edit and Play

When the editor opens a scene, every component runs `OnAddedToEntity`, `OnEnabled` and `OnStart` in Edit mode. Pressing Play does not reload the scene: it only starts calling `Update`. Stopping reloads the scene from disk when `Core.ResetSceneAutomatically` is on (the default).

| Mode | Which `Update` methods run |
|---|---|
| Edit | Only `RenderableComponent`s that implement `IUpdatable`, so animated sprites, particles and tilemaps stay visible while you edit. |
| Play | Every enabled `IUpdatable`. |
| Paused | Only components that also implement `IUpdatableInPauseMode` (menus, HUD, audio zones). |

So gameplay that should start with Play needs two pieces: skip it in `OnStart` while `Core.IsEditMode` is true, and start it from `Core.OnSwitchEditMode`:

```csharp
public partial class IntroMusic : Component
{
    public override void OnStart()
    {
        Core.OnSwitchEditMode += OnModeChanged;
        if (!Core.IsEditMode) Begin();
    }

    public override void OnRemovedFromEntity() => Core.OnSwitchEditMode -= OnModeChanged;

    void OnModeChanged(bool isEditMode)
    {
        if (!isEditMode) Begin();
    }

    void Begin() { /* start gameplay */ }
}
```

`Core.OnSwitchEditMode` is static and outlives scenes, so always unsubscribe. In a published game `Core.IsEditMode` is always `false`, and `OnStart` behaves like a normal start.

## Adding and finding components

```csharp
var sprite = entity.AddComponent(new SpriteRenderer());
var mover  = entity.AddComponent<Mover>();
var health = entity.GetComponent<Health>();
if (entity.TryGetComponent<Health>(out var h)) { }
bool hasCollider = entity.HasComponent<Collider>();
var animator = entity.GetOrCreateComponent<SpriteAnimator>();
List<Collider> colliders = entity.GetComponents<Collider>();

var childSprite = entity.GetComponentInChildren<SpriteRenderer>();
var owner = entity.GetComponentInParent<Health>();

entity.RemoveComponent<Mover>();
entity.RemoveComponent(sprite);
```

Inside a component the same methods exist as shortcuts (`GetComponent<T>()`, `AddComponent(...)`), and `RemoveComponent()` removes the component itself.

`AddComponent` refuses a second component of the same type and returns the existing one, unless you pass `allowSameComponentsOnEntity: true`. Duplicates get unique names (`BoxCollider`, `BoxCollider_1`), and `GetComponent<T>(name)` selects one by name.

`[RequireComponent(typeof(T))]` and `[RequireComponentInChildren(typeof(T))]` (namespace `Voltage.Serialization`) add the listed dependencies when the component is added through the Inspector. They are not applied by `AddComponent` in code.

## Entity basics

| Member | Purpose |
|---|---|
| `Name`, `Tag` | Lookup keys. Tags are integers named in Project Settings; see [Scenes and Entities](scenes-and-entities.md). |
| `Enabled` | Enables or disables the entity and all its components. |
| `UpdateOrder` | Order of this entity's update relative to others. |
| `UpdateInterval` | Update every N frames instead of every frame. |
| `Position`, `LocalPosition`, `Rotation` (radians), `RotationDegrees`, `Scale`, `LocalScale` | Shortcuts to `Transform`. |
| `Parent` | The parent's `Transform`. Use `Transform.SetParent` to reparent. |
| `Destroy()` | Removes the entity, its children and their components. |
| `Destroy(seconds)` | Destroys after a delay. |

Entities placed in the editor are `Serialized`. Entities created with `new Entity("Name")` in code are `NonSerialized` and are never written to the scene file. `SceneRequired` entities, such as the main camera, cannot be destroyed.
