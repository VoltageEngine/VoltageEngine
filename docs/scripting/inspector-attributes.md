---
title: Inspector Attributes
sidebar_position: 2.8
description: Control how component fields appear in the Inspector, add buttons for methods and use layer and file pickers.
---

# Inspector Attributes

The Inspector shows a component's public fields and readable public properties automatically. Attributes in the `Voltage` namespace change what is shown and how.

```csharp
using Microsoft.Xna.Framework;
using Voltage;
using Voltage.Serialization;

public partial class Turret : Component
{
    [Tooltip("Shots per second")]
    [Range(0.5f, 10f)] public float FireRate = 2f;

    [Range(0)] public int Ammo = 30;                 // drag field with a minimum only

    [PhysicsLayerMask] public int TargetLayers = -1;  // checklist of physics layers
    [PhysicsLayer] public int BulletLayer = 1 << 2;   // single layer
    [RenderLayer] public int MuzzleLayer;             // render layer dropdown

    [FilePath(".png|.aseprite")] public string MuzzleFlash = "";

    public AssetReference FireSound;                  // asset slot, drag from the Asset Browser
    public PrefabReference Bullet;                    // prefab slot
    public Entity Target;                             // entity picker

    [Serialize] float _heat;                          // private, but saved and shown
    [HideInInspector] public float Cooldown;          // public, but hidden

    [InspectorCallable]
    public void Fire() { }                            // button in the Inspector

    [InspectorCallable]
    public void SetAmmo(int value) => Ammo = value;   // button with an int field
}
```

| Attribute | Effect |
|---|---|
| `[Range(min, max)]` | Slider for `float` or `int`. `[Range(min, max, step)]` sets the step, `[Range(min, max, true)]` uses a drag field, and `[Range(min)]` gives a drag field with only a minimum. Unsigned integers get a minimum of 0. |
| `[Tooltip("...")]` | Hover text on the field's label. Also works on methods. |
| `[InspectorCallable]` | Adds a button for a method with no parameters, or one `int`, `float`, `string` or `bool` parameter. |
| `[HideInInspector]` | Hides a public member. |
| `[Serialize]` | Shows a non-public field or property. On a field it also includes it in the saved data. |
| `[PhysicsLayer]`, `[PhysicsLayerMask]` | On an `int`: a single physics-layer picker, or a checklist that builds a mask. Names come from Project Settings. |
| `[RenderLayer]` | On an `int`: a render-layer dropdown. |
| `[FilePath(".png|.jpg")]` | On a `string`: a browse button rooted at the project's Content folder, storing a relative path. The filter is optional. |
| `[ComponentGroup("Label", defaultExpanded)]` | On an `IComponentGroup` field: its header label and whether it starts expanded. |

Field types the Inspector edits directly include numbers, `bool`, `string`, enums, `Vector2`, `Color`, `RectangleF`, plain structs, `IComponentGroup` classes, arrays and lists of supported types, `AssetReference`, `PrefabReference`, `Entity`, `Transform`, component references, materials and effects.

Group related settings into a class that implements `IComponentGroup`; it is drawn as a collapsible section and saved with the component. Lists of `IComponentGroup` items are editable too.

```csharp
public class AimSettings : IComponentGroup
{
    public float Spread = 4f;
    public bool AutoAim;
}

public partial class Gun : Component
{
    [ComponentGroup("Aim", defaultExpanded: false)]
    public AimSettings Aim = new AimSettings();
}
```

Component-level additions:

- `[RequireComponent(typeof(T))]` and `[RequireComponentInChildren(typeof(T))]` add dependencies when the component is added from the Inspector.
- `[TimelineProperty]` makes a `float`, `Vector2` or `Color` member animatable by Timeline property tracks, and `[TimelineEvent]` lists a method in Timeline event tracks. See [Timeline Scripting](timelines.md).

Custom Inspector drawers for your own types are editor extensions; see the [plugin guide](../plugins/README.md).
