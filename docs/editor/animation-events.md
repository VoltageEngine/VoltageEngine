---
title: Animation Event Inspector
---

# Animation Event Inspector

Animation events are named markers on the frames of a `SpriteAnimator` animation. Game code subscribes to them by name, for example to deal damage on the frame a sword connects or to play a footstep sound.

## Open it

Select an entity whose **SpriteAnimator** has animations loaded (see [Live Aseprite Integration](aseprite.md)), then press **Manage Animation Events** in its Inspector. This connects that animator to the window. **View > Window > Animation Event Inspector** only shows or hides the window; without an animator it asks you to select one.

## Edit events

Choose an **Animation**; the list shows how many events each one has. **Play** and **Stop** preview it, and the **Frame** slider steps through it. Frames count from zero.

Press **Add Event**, choose a type and press **Create**:

| Type | Fires |
|---|---|
| `AnimationEvent` | Once, as its frame ends and before the next frame shows |
| `LongAnimationEvent` | Every update while the current frame is between its start and end frames, inclusive |

Each row has the start frame (and end frame for a long event), a name, the animation it belongs to and **Delete**. Frames are clamped to the animation's length. Set the end frame of a long event no lower than its start, since the inspector does not check it and such an event never fires. An event with no name never fires. Names are unique within an animation: a duplicate gets a numbered suffix.

Events are stored on the scene's animator component; the Aseprite file is not changed.

## Save

Press **Save** to copy the edited list to the animator and save the scene. Edits are not applied until then, and the preview controls never save. Save before switching to another animator. Adding an event can be undone; other edits in this window cannot, so do not rely on undo to back out of them.

## Use them in code

Subscribe once the animator has its events, for example in a component's `OnStart`:

```csharp
var animator = Entity.GetComponent<SpriteAnimator>();
animator.SubscribeToEvent("Attack", "Hit", DealDamage);
```

`SubscribeToEvent` throws if the animation has no event with that name. Events only fire while the animation is running, and follow the animator's update, so they pause with it.
