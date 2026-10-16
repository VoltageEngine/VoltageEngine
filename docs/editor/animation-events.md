---
title: Animation Event Inspector
---

# Animation Event Inspector

Select an entity with a loaded **SpriteAnimator**, then press **Manage Animation Events** in its Inspector. This assigns that animator to the event window. **View > Window > Animation Event Inspector** restores the window; opening it from the menu alone does not assign an animator. Load the sprite's animations first; see [Aseprite integration](aseprite.md).

Choose an **Animation**, then use **Play/Stop** and the **Frame** slider to preview it. Frame numbers start at zero. Each event row contains its frame, name, animation assignment and a delete action. A **LongAnimationEvent** also has an end frame; the end cannot precede the start.

Use **Add Event** to choose an `AnimationEvent` or `LongAnimationEvent`, then **Create**. Give it a meaningful name and assign the animation and frame or frame range. These events belong to the scene's animator component; editing them does not change the Aseprite source file.

Press **Save** to copy the working event list to the animator and save scene changes. The preview controls only preview frames; they do not save the list. Save before changing animators. Adding an event participates in undo, but other edits in this window do not currently have consistent undo coverage; do not rely on undo as a general cancel operation.
