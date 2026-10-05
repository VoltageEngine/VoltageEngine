---
title: Aseprite Tool Catalogue
sidebar_position: 9
---

# Complete Aseprite tool catalogue

All 123 Aseprite tools are exposed as `aseprite.<name>` through the Voltage gateway and as `aseprite_<name>` through MCP. Every tool also accepts optional `voltage_sprite_id` for stable document targeting. Required parameters appear in bold. Nested fields and bounds are preserved in the machine-readable schemas in `Voltage.Editor/Aseprite/Resources/tools.json`.

## advanced

### `interpolate_frames`

Generate intermediate animation frames by interpolating pixel colors between existing frames

Parameters: **`from_frame` (number)**; **`to_frame` (number)**; `steps` (number).

### `draw_symmetry`

Draw pixels with automatic mirroring (horizontal, vertical, or both)

Parameters: **`pixels` (array)**; **`mode` (string)**: horizontal, vertical, both; `axis` (number).

### `generate_color_ramp`

Generate a color ramp from shadow to highlight through a base color for pixel art shading

Parameters: **`base_color` (string)**; `steps` (number); `hue_shift` (number); `set_palette` (boolean).

### `get_animation_preview`

Get an animation preview as a horizontal frame strip image

Parameters: `scale` (number); `max_frames` (number).

### `flip_sprite`

Flip sprite content horizontally or vertically

Parameters: **`direction` (string)**: horizontal, vertical; `scope` (string): sprite, frame, cel.

### `rotate_sprite`

Rotate the sprite by 90, 180, or 270 degrees

Parameters: **`angle` (number)**.

### `shift_cel`

Shift cel content by pixel offset, optionally wrapping edges

Parameters: **`dx` (number)**; **`dy` (number)**; `wrap` (boolean); `layer` (string); `frame` (number).

### `apply_dither`

Fill a rectangle with a dithering pattern between two colors

Parameters: **`color_a` (string)**; **`color_b` (string)**; **`pattern` (string)**: checker, bayer2, bayer4, horizontal, vertical; **`x` (number)**; **`y` (number)**; **`width` (number)**; **`height` (number)**; `layer` (string); `frame` (number).

### `load_reference_image`

Load an external image as a reference layer

Parameters: **`path` (string)**; `opacity` (number); `name` (string).

### `generate_animation_player_tres`

Generate a Godot AnimationPlayer .tres resource from sprite animations

Parameters: **`texture_path` (string)**; **`output_path` (string)**; `sheet_type` (string): horizontal, vertical; `property_path` (string).

### `define_autotile_rules`

Set up autotile bitmask metadata for a tileset sprite

Parameters: **`tile_size` (number)**; **`type` (string)**: blob47, minimal16, wang.

### `get_autotile_rules`

Read back the autotile bitmask metadata stored by define_autotile_rules (type, tile size, grid, and rules).

Parameters: none.

### `set_lospec_palette`

Load a popular pixel art palette by name (Lospec collection)

Parameters: **`name` (string)**.

### `copy_between_sprites`

Copy a layer's cel from one open sprite to another. Address sprites by stable id (from list_open_sprites, unambiguous) or filename.

Parameters: `source_id` (number); `target_id` (number); `source_filename` (string); `target_filename` (string); **`source_layer` (string)**; `target_layer` (string); `frame` (number).

## analysis

### `get_color_stats`

Analyze the color usage in the active sprite (unique colors, most used, color distribution)

Parameters: `scope` (string): cel, layer, sprite.

### `compare_frames`

Compare two frames and report pixel differences

Parameters: **`frame_a` (number)**; **`frame_b` (number)**; `layer` (string).

### `find_unused_colors`

Find palette colors that are not used in any frame (indexed-mode sprites only). Duplicate palette entries are collapsed and results are capped by max_colors.

Parameters: `max_colors` (number).

### `compare_screenshots`

Compare two frames visually and return a diff image (red=changed, green=only in A, blue=only in B)

Parameters: **`frame_a` (number)**; **`frame_b` (number)**.

### `get_sprite_bounds`

Get the bounding box of non-transparent content in the sprite

Parameters: `layer` (string); `frame` (number).

### `validate_animation`

Validate animation setup: check for empty frames, inconsistent durations, orphan cels, and missing tags

Parameters: none.

## cel

### `get_cel`

Get cel information at a specific layer and frame

Parameters: **`layer` (string)**; **`frame` (number)**.

### `set_cel_position`

Move a cel's offset position

Parameters: **`layer` (string)**; **`frame` (number)**; **`x` (number)**; **`y` (number)**.

### `set_cel_opacity`

Set a cel's opacity (0-255)

Parameters: **`layer` (string)**; **`frame` (number)**; **`opacity` (number)**.

### `clear_cel`

Clear the content of a cel

Parameters: **`layer` (string)**; **`frame` (number)**.

### `link_cels`

Link cels across frames so they share content

Parameters: **`layer` (string)**; **`from_frame` (number)**; **`to_frame` (number)**.

## drawing

### `put_pixel`

Set a single pixel's color at the specified coordinates on the active layer and frame

Parameters: **`x` (number)**; **`y` (number)**; **`color` (string)**; `layer` (string); `frame` (number).

### `put_pixels`

Set multiple pixels at once (batch operation for efficiency). Each pixel is `{x, y, color}`.

Parameters: **`pixels` (array)**; `layer` (string); `frame` (number).

### `get_pixel`

Read the color of a pixel at the specified coordinates

Parameters: **`x` (number)**; **`y` (number)**; `layer` (string); `frame` (number).

### `get_image_data`

Get all pixel data from a cel as a flat array of hex colors (row-major order) or as base64 PNG

Parameters: `format` (string): hex_array, base64_png; `layer` (string); `frame` (number).

### `set_image_data`

Replace the entire image data of a cel from a base64-encoded PNG (format=base64_png, needs 'data') or a row-major hex-color array (format=hex_array, needs 'pixels'+'width'+'height'). The hex_array shape mirrors get_image_data's output for round-trips.

Parameters: `data` (string); `format` (string): base64_png, hex_array; `pixels` (array); `width` (number); `height` (number); `layer` (string); `frame` (number).

### `draw_line`

Draw a line between two points using the specified color and brush size

Parameters: **`x1` (number)**; **`y1` (number)**; **`x2` (number)**; **`y2` (number)**; **`color` (string)**; `brush_size` (number); `layer` (string); `frame` (number).

### `draw_rect`

Draw a rectangle (filled or outline)

Parameters: **`x` (number)**; **`y` (number)**; **`width` (number)**; **`height` (number)**; **`color` (string)**; `filled` (boolean); `brush_size` (number); `layer` (string); `frame` (number).

### `draw_ellipse`

Draw an ellipse (filled or outline) within the specified bounding box

Parameters: **`x` (number)**; **`y` (number)**; **`width` (number)**; **`height` (number)**; **`color` (string)**; `filled` (boolean); `brush_size` (number); `layer` (string); `frame` (number).

### `flood_fill`

Bucket fill at the specified point with the given color

Parameters: **`x` (number)**; **`y` (number)**; **`color` (string)**; `tolerance` (number); `contiguous` (boolean); `layer` (string); `frame` (number).

### `draw_brush_stroke`

Draw along a path of points using the specified brush

Parameters: **`points` (array)**; **`color` (string)**; `brush_size` (number); `brush_type` (string): circle, square, line; `layer` (string); `frame` (number).

### `clear_image`

Clear the entire image/cel to transparent or a specified color

Parameters: `color` (string); `layer` (string); `frame` (number).

### `replace_color`

Replace all occurrences of one color with another in the active cel or entire sprite

Parameters: **`from_color` (string)**; **`to_color` (string)**; `scope` (string): cel, layer, sprite; `tolerance` (number).

### `outline`

Draw an outline around non-transparent pixels in the cel

Parameters: **`color` (string)**; `layer` (string); `frame` (number); `outside` (boolean).

## editor

### `execute_script`

Execute arbitrary Lua code in Aseprite's scripting environment. Use this for operations not covered by other tools. The script can access all Aseprite API objects (app, Sprite, Image, etc.)

Parameters: **`code` (string)**.

### `get_server_info`

Report the extension version, MCP tool count, Aseprite version and Lua apiVersion, UI availability, and connection port. Useful for bug reports.

Parameters: none.

### `undo`

Undo the last operation(s) in the active sprite

Parameters: `steps` (number).

### `redo`

Redo the last undone operation(s) in the active sprite

Parameters: `steps` (number).

## export

### `export_png`

Export the sprite as PNG file(s). Can export single frame, all frames, or specific frame range

Parameters: **`path` (string)**; `frame` (number); `scale` (number).

### `export_sprite_sheet`

Export a sprite sheet (all frames in a grid) with optional JSON data file

Parameters: **`path` (string)**; `json_path` (string); `sheet_type` (string): horizontal, vertical, rows, columns, packed; `columns` (number); `rows` (number); `padding` (number); `inner_padding` (number); `trim` (boolean); `scale` (number); `tag` (string).

### `export_gif`

Export the sprite as an animated GIF

Parameters: **`path` (string)**; `scale` (number).

### `export_tileset`

Export the tileset of a tilemap layer as an image

Parameters: **`layer` (string)**; **`path` (string)**.

### `export_layers`

Export each layer as a separate image file

Parameters: **`output_dir` (string)**; `format` (string): png, gif, aseprite; `visible_only` (boolean).

### `export_tags_as_sheets`

Export each animation tag as a separate sprite sheet

Parameters: **`output_dir` (string)**; `sheet_type` (string): horizontal, vertical, rows, columns; `scale` (number); `json` (boolean).

## frame

### `get_frames`

Get all frames in the active sprite with their durations

Parameters: none.

### `add_frame`

Add a new frame to the sprite

Parameters: `after` (number); `copy` (boolean).

### `delete_frame`

Delete a frame by number

Parameters: **`frame` (number)**.

### `set_frame_duration`

Set the duration of a specific frame in milliseconds

Parameters: **`frame` (number)**; **`duration_ms` (number)**.

### `set_frame_range_duration`

Set the duration for a range of frames

Parameters: **`from_frame` (number)**; **`to_frame` (number)**; **`duration_ms` (number)**.

### `set_active_frame`

Navigate to a specific frame

Parameters: **`frame` (number)**.

### `duplicate_frame`

Duplicate an existing frame

Parameters: `frame` (number).

### `reverse_frames`

Reverse the order of frames in a range

Parameters: **`from_frame` (number)**; **`to_frame` (number)**.

## godot

### `export_for_godot`

Export sprite sheet + generate Godot SpriteFrames .tres resource. Creates both the PNG sprite sheet and a ready-to-use .tres file with animations based on Aseprite tags

Parameters: **`output_dir` (string)**; `name` (string); `sheet_type` (string): horizontal, vertical, rows, columns; `scale` (number); `trim` (boolean); `texture_path` (string).

### `export_atlas_texture`

Export sprite regions as Godot AtlasTexture .tres files. Each slice becomes a separate AtlasTexture resource

Parameters: **`output_dir` (string)**; **`texture_path` (string)**.

### `export_9patch_for_godot`

Export a 9-patch sprite with slice data as a Godot NinePatchRect-compatible resource

Parameters: **`slice_name` (string)**; **`output_dir` (string)**; **`texture_path` (string)**.

### `export_tileset_for_godot`

Export tileset as image + generate Godot TileSet .tres resource

Parameters: **`layer` (string)**; **`output_dir` (string)**; `tile_size` (number); `name` (string); `texture_path` (string).

### `get_animation_data`

Get structured animation data (tags, frame regions, durations) suitable for Godot integration

Parameters: `sheet_type` (string): horizontal, vertical, rows, columns; `columns` (number).

### `generate_spriteframes_tres`

Generate a Godot SpriteFrames .tres file from the current sprite's tags and frames without exporting images

Parameters: **`texture_path` (string)**; **`output_path` (string)**; `sheet_type` (string): horizontal, vertical, rows, columns; `columns` (number).

### `sync_to_godot_project`

Export the sprite and copy all output files to a Godot project. Automatically detects tags for animations and exports both sprite sheet and SpriteFrames resource

Parameters: **`godot_project_path` (string)**; `target_dir` (string); `name` (string); `scale` (number).

### `batch_export_for_godot`

Export all open sprites to a Godot project in one operation

Parameters: **`godot_project_path` (string)**; `target_dir` (string); `scale` (number).

## layer

### `get_layers`

Get all layers in the active sprite with their properties (name, type, visibility, opacity, blend mode, hierarchy)

Parameters: none.

### `add_layer`

Add a new layer to the active sprite

Parameters: `name` (string); `type` (string): normal, group, tilemap; `parent` (string); `below` (string).

### `delete_layer`

Delete a layer by name

Parameters: **`name` (string)**.

### `rename_layer`

Rename a layer

Parameters: **`name` (string)**; **`new_name` (string)**.

### `set_layer_visibility`

Show or hide a layer

Parameters: **`name` (string)**; **`visible` (boolean)**.

### `set_layer_opacity`

Set a layer's opacity (0-255)

Parameters: **`name` (string)**; **`opacity` (number)**.

### `set_layer_blend_mode`

Set a layer's blend mode

Parameters: **`name` (string)**; **`blend_mode` (string)**: normal, multiply, screen, overlay, darken, lighten, color_dodge, color_burn, hard_light, soft_light, difference, exclusion, hue, saturation, color, luminosity, addition, subtract, divide.

### `reorder_layer`

Move a layer to a new position in the stack

Parameters: **`name` (string)**; **`stack_index` (number)**.

### `duplicate_layer`

Duplicate a layer

Parameters: **`name` (string)**; `new_name` (string).

### `merge_layer_down`

Merge the specified layer with the one below it

Parameters: **`name` (string)**.

## palette

### `get_palette`

Get all colors in the active sprite's palette

Parameters: `max_colors` (number).

### `set_palette_color`

Set the color at a specific palette index

Parameters: **`index` (number)**; **`color` (string)**.

### `set_palette`

Replace the entire palette with a new set of colors

Parameters: **`colors` (array)**.

### `add_palette_color`

Add a color to the end of the palette

Parameters: **`color` (string)**.

### `resize_palette`

Resize the palette to a specific number of colors

Parameters: **`size` (number)**.

### `load_palette`

Load a palette from a file (.gpl, .pal, .ase, .png)

Parameters: **`path` (string)**.

### `save_palette`

Save the palette to a file

Parameters: **`path` (string)**.

### `set_fg_color`

Set the foreground drawing color

Parameters: **`color` (string)**.

### `set_bg_color`

Set the background drawing color

Parameters: **`color` (string)**.

### `generate_palette_from_sprite`

Generate a palette from the colors used in the current sprite

Parameters: `max_colors` (number).

### `sort_palette`

Sort the palette colors by hue, saturation, lightness, or individual RGB channels

Parameters: `sort_by` (string): hue, saturation, lightness, brightness, red, green, blue; `reverse` (boolean).

### `import_palette_from_image`

Extract colors from an image file and set them as the active sprite's palette

Parameters: **`path` (string)**; `max_colors` (number).

## selection

### `select_rect`

Select a rectangular region

Parameters: **`x` (number)**; **`y` (number)**; **`width` (number)**; **`height` (number)**; `mode` (string): replace, add, subtract, intersect.

### `select_ellipse`

Select an elliptical region

Parameters: **`x` (number)**; **`y` (number)**; **`width` (number)**; **`height` (number)**; `mode` (string): replace, add, subtract, intersect.

### `select_all`

Select the entire canvas

Parameters: none.

### `deselect`

Clear the current selection

Parameters: none.

### `select_by_color`

Select all pixels matching a specific color

Parameters: **`color` (string)**; `tolerance` (number); `mode` (string): replace, add, subtract, intersect.

### `get_selection`

Get the current selection bounds and status

Parameters: none.

### `invert_selection`

Invert the current selection

Parameters: none.

## slice

### `get_slices`

Get all slices in the active sprite

Parameters: none.

### `create_slice`

Create a new slice (named region) in the sprite

Parameters: **`name` (string)**; **`x` (number)**; **`y` (number)**; **`width` (number)**; **`height` (number)**; `color` (string).

### `delete_slice`

Delete a slice by name

Parameters: **`name` (string)**.

### `set_slice_9patch`

Set a slice's 9-patch / nine-slice center rectangle for scalable UI sprites

Parameters: **`name` (string)**; **`center_x` (number)**; **`center_y` (number)**; **`center_width` (number)**; **`center_height` (number)**.

### `set_slice_pivot`

Set a slice's pivot point

Parameters: **`name` (string)**; **`pivot_x` (number)**; **`pivot_y` (number)**.

## sprite

### `create_sprite`

Create a new sprite with specified dimensions and color mode

Parameters: `width` (number); `height` (number); `color_mode` (string): rgba, indexed, grayscale.

### `open_sprite`

Open a sprite file (.ase, .aseprite, .png, .gif, etc.)

Parameters: **`path` (string)**.

### `save_sprite`

Save the active sprite. Optionally save as a new file path

Parameters: `path` (string); `rename` (boolean).

### `close_sprite`

Close the active sprite or a specific sprite by filename

Parameters: `filename` (string).

### `get_sprite_info`

Get detailed information about the active sprite (dimensions, color mode, frame count, layer count, etc.)

Parameters: `filename` (string).

### `list_open_sprites`

List all currently open sprites in Aseprite

Parameters: none.

### `set_active_sprite`

Set the active sprite by stable id (from list_open_sprites, unambiguous) or filename

Parameters: `id` (number); `filename` (string).

### `resize_sprite`

Resize the sprite canvas or scale the sprite content

Parameters: **`width` (number)**; **`height` (number)**; `method` (string): nearest, bilinear, rotsprite; `scale_content` (boolean).

### `crop_sprite`

Crop the sprite to the selection bounds or to visible content

Parameters: `mode` (string): selection, content.

### `flatten_sprite`

Flatten all visible layers into a single layer

Parameters: none.

### `get_sprite_screenshot`

Get a PNG screenshot of the current sprite state (all visible layers flattened). Returns both image and metadata.

Parameters: `frame` (number).

### `set_sprite_grid`

Set the sprite grid size and offset (useful for tileset and sprite sheet workflows)

Parameters: **`grid_width` (number)**; **`grid_height` (number)**; `origin_x` (number); `origin_y` (number).

## tag

### `get_tags`

Get all animation tags in the active sprite

Parameters: none.

### `create_tag`

Create a new animation tag

Parameters: **`name` (string)**; **`from_frame` (number)**; **`to_frame` (number)**; `color` (string); `ani_dir` (string): forward, reverse, ping_pong, ping_pong_reverse; `repeat` (number).

### `delete_tag`

Delete an animation tag by name

Parameters: **`name` (string)**.

### `rename_tag`

Rename an animation tag

Parameters: **`name` (string)**; **`new_name` (string)**.

### `set_tag_color`

Set the color of an animation tag

Parameters: **`name` (string)**; **`color` (string)**.

### `set_tag_range`

Change the frame range of an animation tag

Parameters: **`name` (string)**; **`from_frame` (number)**; **`to_frame` (number)**.

## template

### `create_character_template`

Create a new sprite pre-configured for character animation with layers (shadow, outline, body, effects) and animation tags

Parameters: `size` (number); `animations` (array).

### `create_tileset_template`

Create a new sprite configured as a tileset grid with guide lines and proper grid settings

Parameters: `tile_size` (number); `columns` (number); `rows` (number).

## tilemap

### `create_tilemap_layer`

Create a new tilemap layer with the specified tile size

Parameters: `name` (string); **`tile_width` (number)**; **`tile_height` (number)**.

### `get_tileset`

Get tileset information for a tilemap layer

Parameters: **`layer` (string)**.

### `set_tile`

Set a tile at a grid position in a tilemap layer

Parameters: **`layer` (string)**; **`col` (number)**; **`row` (number)**; **`tile_index` (number)**; `frame` (number).

### `get_tile`

Get the tile index at a grid position in a tilemap layer

Parameters: **`layer` (string)**; **`col` (number)**; **`row` (number)**; `frame` (number).

### `get_tilemap_info`

Get tilemap grid dimensions, tile size, and used tile indices

Parameters: **`layer` (string)**; `frame` (number).

