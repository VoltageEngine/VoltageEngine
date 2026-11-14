---
title: Input
sidebar_position: 2.6
description: Keyboard, mouse, gamepad and touch state, and virtual buttons and axes that map several devices to one action.
---

# Input

`Voltage.Input` is a static class updated once per frame. Each control reports three states: **Down** (held this frame), **Pressed** (went down this frame) and **Released** (went up this frame).

## Keyboard and mouse

```csharp
if (Input.IsKeyPressed(Keys.Space)) Jump();
if (Input.IsKeyDown(Keys.LeftShift, Keys.RightShift)) Run();   // either key
if (Input.IsKeyReleased(Keys.E)) Interact();

if (Input.LeftMouseButtonPressed) Fire();
Vector2 mouse  = Input.MousePosition;          // in scene render-target pixels
Vector2 world  = Core.Scene.Camera.MouseToWorldPoint();
int scroll     = Input.MouseWheelDelta;
```

Mouse buttons include left, right, middle and the two extended buttons, each with `Pressed`, `Down` and `Released`. `MousePosition` is already scaled to the scene's render target, so it matches what the player sees under any resolution policy, including inside the editor's game view. `RawMousePosition` is the window position and `MousePositionDelta` the movement since the last frame. `InputUtils.IsShiftDown()`, `IsControlDown()` and `IsAltDown()` check either modifier key.

## Gamepads

```csharp
var pad = Input.GamePads[0];
if (pad.IsButtonPressed(Buttons.A)) Jump();
Vector2 move = pad.GetLeftStick();            // dead zone applied
if (pad.IsRightTriggerDown(0.3f)) Aim();
pad.SetVibration(0.6f, 0.6f, 0.25f);          // left, right, seconds
```

Only one gamepad is tracked by default; set `Input.MaxSupportedGamePads` (up to the platform limit) to track more. `GamePadData` also has D-pad states, stick direction checks such as `IsLeftStickLeftPressed`, inverted vertical axes and `IsConnected()`. `Input.Emitter` raises connect and disconnect events.

## Touch

Call `Input.Touch.EnableTouchSupport()` once, then read `Input.Touch.CurrentTouches` and `CurrentGestures`. Desktop builds report touch only on touch-capable hardware.

## Virtual input

A virtual input combines several physical controls into one action, so gameplay code asks "jump?" instead of checking every device. Create them once, for example in `OnStart`, and keep the instances: they update automatically every frame.

```csharp
VirtualButton _jump;
VirtualJoystick _move;
VirtualIntegerAxis _menu;

public override void OnStart()
{
    _jump = new VirtualButton()
        .AddKeyboardKey(Keys.Space)
        .AddGamePadButton(0, Buttons.A);
    _jump.BufferTime = 0.1f;                // remember a press for 0.1 s

    _move = new VirtualJoystick(normalized: true)
        .AddGamePadLeftStick()
        .AddKeyboardKeys(VirtualInput.OverlapBehavior.TakeNewer, Keys.A, Keys.D, Keys.W, Keys.S);

    _menu = new VirtualIntegerAxis()
        .AddGamePadDPadUpDown()
        .AddKeyboardKeys(VirtualInput.OverlapBehavior.CancelOut, Keys.Up, Keys.Down);
    _menu.SetRepeat(0.4f, 0.08f);           // first repeat, then every 0.08 s
}

public void Update()
{
    if (_jump.IsPressed) { _jump.ConsumeBuffer(); Jump(); }
    Vector2 dir = _move.Value;
    if (_menu.JustPushed(1)) NextItem();
}

public override void OnRemovedFromEntity()
{
    _jump.Deregister();
    _move.Deregister();
    _menu.Deregister();
}
```

| Type | Value | Typical use |
|---|---|---|
| `VirtualButton` | `IsDown`, `IsPressed`, `IsReleased`, `IsRepeating` | Jump, attack, confirm. Keys (optionally with a modifier), gamepad buttons, triggers, D-pad directions and mouse buttons. |
| `VirtualAxis` | `float` from -1 to 1 | One stick axis or a pair of keys. |
| `VirtualIntegerAxis` | `int` of -1, 0 or 1 | Menu navigation; supports repeat. |
| `VirtualJoystick` | `Vector2` | Movement from a stick, the D-pad or four keys. |

When two opposing keys are held, the `OverlapBehavior` decides the result: `CancelOut` gives zero, `TakeOlder` keeps the first key, `TakeNewer` the latest. Virtual inputs also convert implicitly (`if (_jump)`, `int step = _menu;`, `Vector2 dir = _move;`). Call `Deregister()` when the owner goes away, or the input keeps updating.

Nodes can be added directly too, for example `_jump.Nodes.Add(new VirtualButton.KeyboardKey(Keys.Z))`, which suits rebinding menus.

## Editor and automation

In Edit mode the editor owns the mouse and keyboard; game input applies while playing. The [Editor Gateway](../gateway/README.md) can drive input from scripts with `input.key`, `input.click`, `input.type` and the input recorder, which is how automated play tests run.
