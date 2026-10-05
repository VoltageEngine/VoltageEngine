# Voltage Bridge — AI Pixel Art Skill Guide

You are an AI assistant controlling Aseprite through MCP tools. This guide teaches you how to create quality pixel art efficiently.

## Golden Rules

1. **Every pixel matters.** At 32x32, one misplaced pixel ruins a shape. Plan before drawing.
2. **Fill the canvas.** A 32x32 character should be 24-28px tall. Never leave half the canvas empty.
3. **Use few colors.** 8-16 colors is ideal. More colors ≠ better pixel art. Use `set_lospec_palette` first.
4. **Work in layers.** Always: outline (bottom) → body → detail → effects/glow (top).
5. **Batch with `execute_script`.** For anything over 10 pixels, write a Lua script instead of calling put_pixel repeatedly.

## Canvas Size Guidelines

| Size | Use Case | Character Height | Head Size |
|------|----------|-----------------|-----------|
| 16x16 | Icons, small items, tiny characters | 12-14px | 4-5px |
| 32x32 | Standard characters, items, tiles | 24-28px | 7-9px |
| 48x48 | Detailed characters, portraits | 36-42px | 10-14px |
| 64x64 | Large sprites, bosses | 50-58px | 14-18px |

**Character proportions (32x32 standard):**
- Head: rows 2-9 (~8px), width 10-12px, centered
- Torso: rows 10-17 (~8px), width 12-16px (shoulders wider)
- Legs: rows 18-27 (~10px), width per leg 3-4px
- Arms: beside torso, 1-2px wide, 6-8px long
- Leave 1-2px margin on all sides for outline

## Workflow: Step by Step

### Phase 1: Setup
```
1. create_sprite (or create_character_template / create_tileset_template)
2. set_lospec_palette — pick a palette FIRST, not after drawing
3. Plan the silhouette mentally before any drawing
```

### Phase 2: Base Drawing (use execute_script for efficiency)
```
4. Draw silhouette with mid-tone color (fill the shape solid)
5. Add base color regions (skin, armor, cloth, metal)
6. Use symmetric drawing (draw_symmetry) for front-facing characters
```

### Phase 3: Shading & Detail
```
7. Add shadow color (1 shade darker) on bottom/right edges of each region
8. Add highlight (1 shade lighter) on top/left edges
9. Add detail pixels: eyes, belt buckle, buttons, seams
10. generate_color_ramp for consistent shading palette
```

### Phase 4: Polish
```
11. Generate outline (execute_script with 8-directional edge detection)
12. Add effects layer: glowing eyes, energy, particles
13. Review with get_sprite_screenshot — check readability at 1x
```

### Phase 5: Animation
```
14. Create keyframes (frame 1 and frame N with different poses)
15. interpolate_frames for in-between frames
16. Adjust timing: set_frame_duration
17. Review with get_animation_preview
```

### Phase 6: Export
```
18. export_for_godot — generates sprite sheet + .tres in one call
19. Or export_png / export_sprite_sheet for other engines
```

## Palette Strategy

**Always use a curated palette.** Never pick random hex colors.

Recommended Lospec palettes by mood:
| Palette | Colors | Best For |
|---------|--------|----------|
| `slso8` | 8 | Dark/moody, cyberpunk, noir |
| `pico-8` | 16 | Retro games, bright/cheerful |
| `endesga-32` | 32 | General purpose, fantasy |
| `sweetie-16` | 16 | Soft/pastel, cozy games |
| `arne-16` | 16 | Classic pixel art, NES-style |
| `resurrect-64` | 64 | Detailed work, many shades |
| `crimson` | 8 | Blood/horror, intense |

**Color ramp rule:** For each material, use 3-5 shades:
- 1 deep shadow (darkest)
- 1 shadow
- 1 base color (mid-tone — most area)
- 1 highlight
- 1 specular (brightest, only 1-2 pixels)

Use `generate_color_ramp` with `hue_shift: 10-20` for natural-looking ramps:
- Shadows shift toward blue/purple
- Highlights shift toward yellow/orange

## Drawing with execute_script

For any shape more complex than a simple rectangle, use `execute_script`. It's faster and more precise than calling individual tools.

**Template: Symmetric character drawing**
```lua
local sprite = app.sprite
local function h2p(hex)
  hex = hex:gsub("#","")
  return app.pixelColor.rgba(
    tonumber(hex:sub(1,2),16),
    tonumber(hex:sub(3,4),16),
    tonumber(hex:sub(5,6),16), 255)
end
local function mk()
  local i = Image(32, 32, sprite.colorMode); i:clear(); return i
end

local img = mk()
-- Symmetric draw: mirrors across center X
local function sym(x, y, c)
  img:putPixel(x, y, c)
  img:putPixel(31 - x, y, c)  -- adjust 31 for sprite width - 1
end

-- Draw character here using sym() calls...
-- sym(14, 5, COLOR) -- draws at x=14 AND x=17 (mirrored)

app.transaction("Draw", function()
  local layer = -- get target layer
  local cel = layer:cel(1)
  if cel then sprite:deleteCel(cel) end
  sprite:newCel(layer, 1, img, Point(0, 0))
end)
return { done = true }
```

**Template: Auto-generate outline**
```lua
local function gen_outline(src_img, outline_color)
  local oimg = Image(src_img.width, src_img.height, src_img.colorMode)
  oimg:clear()
  local dirs = {{-1,0},{1,0},{0,-1},{0,1},{-1,-1},{1,-1},{-1,1},{1,1}}
  for y = 0, src_img.height - 1 do
    for x = 0, src_img.width - 1 do
      if app.pixelColor.rgbaA(src_img:getPixel(x, y)) == 0 then
        for _, d in ipairs(dirs) do
          local nx, ny = x + d[1], y + d[2]
          if nx >= 0 and ny >= 0 and nx < src_img.width and ny < src_img.height then
            if app.pixelColor.rgbaA(src_img:getPixel(nx, ny)) > 0 then
              oimg:putPixel(x, y, outline_color)
              break
            end
          end
        end
      end
    end
  end
  return oimg
end
```

**Template: Auto-generate shadow**
```lua
local function gen_shadow(src_img, offset_x, offset_y, shadow_color)
  local simg = Image(src_img.width, src_img.height, src_img.colorMode)
  simg:clear()
  for y = 0, src_img.height - 1 do
    for x = 0, src_img.width - 1 do
      if app.pixelColor.rgbaA(src_img:getPixel(x, y)) > 0 then
        local sx, sy = x + offset_x, y + offset_y
        if sx >= 0 and sy >= 0 and sx < src_img.width and sy < src_img.height then
          simg:putPixel(sx, sy, shadow_color)
        end
      end
    end
  end
  return simg
end
```

## Animation Guidelines

**Frame counts by animation type:**
| Animation | Frames | Duration/frame | Notes |
|-----------|--------|---------------|-------|
| Idle | 2-4 | 200-400ms | Subtle breathing/bob. 2 frames minimum. |
| Walk | 4-8 | 100-150ms | 4 = basic, 6 = smooth, 8 = detailed |
| Run | 4-6 | 80-100ms | Faster than walk, more lean |
| Attack | 3-6 | 60-120ms | Anticipation(1-2) → Swing(1-2) → Recovery(1-2) |
| Hit/Hurt | 2-3 | 100-150ms | Flash + knockback pose |
| Death | 4-6 | 150-200ms | Collapse sequence |
| Idle item | 3-4 | 200-300ms | Pulse, glow, float, spin |

**Animation principles for pixel art:**
- **Idle:** Don't move too much. 1-2px vertical bob is enough. Shift the body cel position, don't redraw.
- **Walk:** Key poses are Contact (foot down) and Passing (feet cross). Draw 2 key poses, use `interpolate_frames` for in-between.
- **Squash & stretch:** At this resolution, means ±1px height/width change.
- **Anticipation:** 1 frame of pulling back before an attack swing.
- **Ease in/out:** Use longer durations on start/end frames, shorter in the middle.

**Efficient animation workflow:**
1. Draw frame 1 (key pose A) fully detailed
2. Draw last frame (key pose B) 
3. Use `interpolate_frames` to generate in-betweens
4. Manually fix the interpolated frames (interpolation is linear, may need cleanup)
5. Use `shift_cel` for positional animations (bobbing, shaking)
6. Use `set_cel_opacity` for fade effects

## Common Recipes

### Recipe: Game Character (32x32)
```
1. create_character_template (size: 32)
2. set_lospec_palette ("endesga-32" or "slso8")
3. execute_script: draw body with sym() — fill canvas 24-28px tall
4. execute_script: gen_outline() from body
5. Add detail layer: eyes, gear, weapon
6. Add effects: glowing elements
7. Copy frame 1 to idle frames, shift_cel for bob
8. Draw walk key poses on walk frames
9. set_frame_duration for each tag
10. export_for_godot
```

### Recipe: Item/Pickup (16x16 or 32x32)
```
1. create_sprite (size matches game)
2. set_lospec_palette
3. draw_rect: base shape (fill most of canvas)
4. draw_ellipse: rounded elements
5. Shade: shadow on bottom-right, highlight on top-left
6. Add glow layer for magical/tech items
7. 2-4 frame pulse animation (change glow opacity per frame)
8. export_for_godot
```

### Recipe: Tileset (16x16 tiles)
```
1. create_tileset_template (tile_size: 16, columns: 8, rows: 8)
2. set_lospec_palette
3. Draw tiles in grid cells:
   - Row 1: Ground variants (grass, dirt, stone)
   - Row 2: Wall tops and sides
   - Row 3: Decorations (flowers, cracks, moss)
   - Row 4: Interactive (door, chest, lever)
4. define_autotile_rules if needed
5. export_tileset_for_godot
```

### Recipe: UI Element (9-patch)
```
1. create_sprite (e.g. 24x24 for a panel)
2. Draw border (2-3px) and fill
3. create_slice with 9-patch center region
4. export_9patch_for_godot
```

## Quality Checklist

Before exporting, verify:
- [ ] **Readability at 1x:** `get_sprite_screenshot` — can you tell what it is at native resolution?
- [ ] **Canvas usage:** Character fills 70-85% of canvas height
- [ ] **Color count:** `get_color_stats` — under 16 unique colors for 32x32?
- [ ] **No orphan pixels:** Stray single pixels that don't belong
- [ ] **Consistent light source:** Shadows all on same side (usually bottom-right)
- [ ] **Outline clean:** No broken outline, no double-thick spots
- [ ] **Animation smooth:** `get_animation_preview` — no jarring jumps?
- [ ] **validate_animation:** No empty frames or orphan frames without tags

## Aseprite API Pitfalls (for execute_script)

- **Image coordinates are local to the cel**, not the sprite. Use `cel.position` to convert.
- **app.pixelColor.rgba(r,g,b,a)** creates a pixel value. Use `app.pixelColor.rgbaR/G/B/A()` to read.
- **app.transaction("name", fn)** wraps changes in an undo-able group. Always use it.
- **Layer stacking:** `stackIndex = 1` is the bottom layer. Higher = on top.
- **Frame numbers are 1-based** in Lua, not 0-based.
- **Sprite size change:** After rotate_sprite or resize_sprite, all coordinate assumptions change.

## Tool Selection Quick Reference

| Task | Best Tool |
|------|-----------|
| Draw 1-3 pixels | `put_pixel` |
| Draw 4-50 pixels | `put_pixels` |
| Draw 50+ pixels / complex shapes | `execute_script` |
| Simple rectangle/ellipse/line | `draw_rect` / `draw_ellipse` / `draw_line` |
| Symmetric character | `draw_symmetry` or `execute_script` with sym() |
| Gradient / dither fill | `apply_dither` |
| Change colors globally | `replace_color` |
| Check progress | `get_sprite_screenshot` |
| Check animation | `get_animation_preview` |
| Verify quality | `get_color_stats` + `validate_animation` |
| Export to Godot | `export_for_godot` (includes .tres generation) |
| Any complex/custom operation | `execute_script` |
