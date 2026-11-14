---
title: UI
sidebar_position: 4
description: In-game menus and HUD with UICanvas, tables, widgets, skins and gamepad navigation.
---

# UI

Game UI is built from `Element`s on a `Stage` (namespace `Voltage.UI`), laid out by tables and styled by a `Skin`. The `UICanvas` component hosts a stage in the scene.

```csharp
using Voltage;
using Voltage.UI;

public partial class PauseMenu : Component
{
    public override void OnStart()
    {
        var canvas = Entity.AddComponent(new UICanvas());
        canvas.IsFullScreen = true;
        canvas.SetRenderLayer(RenderLayer.GetRenderLayer("UIElement"));

        var skin = Skin.CreateDefaultSkin();
        var table = canvas.Stage.AddElement(new Table());
        table.SetFillParent(true);

        table.Add(new Label("Paused", skin)).SetPadBottom(20);
        table.Row();
        var resume = table.Add(new TextButton("Resume", skin)).SetMinWidth(160).GetElement<TextButton>();
        resume.OnClicked += _ => Core.IsPauseMode = false;
        table.Row();
        var volume = table.Add(new Slider(skin, null, 0, 1, 0.05f)).GetElement<Slider>();
        volume.OnChanged += v => Core.Audio.SetBusVolume("Master", v);

        canvas.Stage.SetGamepadFocusElement(resume);
    }
}
```

`UICanvas` keeps updating while Play is paused, so pause menus work without extra code. With `IsFullScreen` the stage uses scene render-target pixels, which suits HUDs and menus on the UIElement layer; that layer draws after post-processing, so screen effects never distort the UI. With `IsFullScreen` off, the stage lives in world space and moves with the camera, for speech bubbles and health bars. Fading the canvas `Color` alpha to 0 also disables its input.

## Layout

`Table` arranges children in rows and cells. `Add(element)` returns a `Cell` you configure with padding, alignment, size, expand and fill; `Row()` starts a new row. Other containers: `HorizontalGroup`, `VerticalGroup`, `Stack`, `Container`, `ScrollPane`, `SplitPane`, `TabPane`, `Tree` and `Window` (draggable, with `Dialog` for modal prompts; `UICanvas.ShowDialog` shows a quick message box).

## Widgets

`Label`, `Image`, `Button`, `TextButton`, `ImageButton`, `ImageTextButton`, `IconButton`, `CheckBox`, `Slider`, `ProgressBar`, `TextField`, `NumberField`, `SelectBox`, `ListBox`, `TextTooltip` and `Tooltip`. Buttons raise `OnClicked` and `OnChanged`; sliders and progress bars raise `OnChanged` with the value. `ButtonGroup` makes radio buttons.

## Skins

A `Skin` stores fonts, colours, drawables and a style object per widget type. `Skin.CreateDefaultSkin()` gives a plain skin to start from. Build your own by adding resources and styles in code:

```csharp
var skin = new Skin();
skin.AddSprites(Core.Content.LoadAsepriteAtlas("Content/UI/Widgets.aseprite"));
skin.Add("title", new LabelStyle(Core.Content.LoadBitmapFont("Content/Fonts/Title.fnt"), Color.Gold));
var title = new Label("Voltage", skin, "title");
```

`GetDrawable(name)` returns a sprite, nine-patch or tinted drawable, and `NewTintedDrawable` recolours one. Widgets take a skin plus an optional style name.

## Gamepad and keyboard navigation

Buttons, check boxes and sliders implement `IGamepadFocusable`. Give the stage a starting element with `SetGamepadFocusElement`; the stick, D-pad or arrow keys then move focus to the nearest element in that direction and `GamepadActionButton` (A by default) or `KeyboardActionKey` (Enter) activates it. `KeyboardEmulatesGamepad` is on by default. For fixed routes, call `EnableExplicitFocusableControl(up, down, left, right)` on an element. `Stage.NavigationRepeatDelay` and `NavigationRepeatInterval` tune held directions.

`Stage.Debug` or `SetDebugAll(true)` outlines every element, which helps when a layout misbehaves.

## Editor UI

The Voltage Editor itself is built with Dear ImGui. That UI belongs to the editor and is not part of published games; extend it with [plugin windows](../plugins/README.md), not in game code.
