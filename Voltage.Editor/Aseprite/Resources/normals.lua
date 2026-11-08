-- Voltage Normals: flat faceted normal maps, glow and rim for Voltage sprites.
-- Every panel button calls the same handlers the bridge exposes as aseprite.normals_* tools.
VoltageNormals = VoltageNormals or {}
local N = VoltageNormals
local pc = app.pixelColor

local NORMAL_PREFIX, GLOW_PREFIX, GENERATED_SUFFIX = "Normals-", "Glow-", "-gen"
local GLOW_TINT = { 255, 170, 40 }
local PANEL_ROWS_HEIGHT = 340
local FACETS = {
  front = { 128, 128, 255 }, left = { 58, 128, 234 }, right = { 198, 128, 234 },
  top = { 128, 198, 234 }, bottom = { 128, 58, 234 },
}
local DIAGONALS = {
  top_left = { 79, 177, 234 }, top_right = { 177, 177, 234 },
  bottom_left = { 79, 79, 234 }, bottom_right = { 177, 79, 234 },
}
local FACET_ORDER = { "front", "left", "right", "top", "bottom", "top_left", "top_right", "bottom_left", "bottom_right" }
local PRESETS = {
  ["Character"] = { width = 1, merge_outlines = false, split = false, split_min = 24, split_band = 0.2 },
  ["Prop"] = { width = 1, merge_outlines = false, split = true, split_min = 48, split_band = 0.15 },
  ["Wall-tile"] = { width = 1, merge_outlines = false, split = false, split_min = 0, split_band = 0 },
}
N.PRESET_NAMES = { "Character", "Prop", "Wall-tile" }
N.LIGHT_PRESET_NAMES = { "Fluorescent bar above", "Warm room fill", "Cable spark" }

local function bridge() return VOLTAGE_BRIDGE end
local function ok(data) return { result = data } end
local function fail(message) return bridge().base.error_invalid_params(message) end

local function pixel_of(rgb, alpha) return pc.rgba(rgb[1], rgb[2], rgb[3], alpha or 255) end
local function alpha_of(v) return pc.rgbaA(v) end
local function rgb_of(v) return pc.rgbaR(v), pc.rgbaG(v), pc.rgbaB(v) end
local function luma(v) local r, g, b = rgb_of(v); return 0.299 * r + 0.587 * g + 0.114 * b end
local function glow_strength(v)
  local r, g, b = rgb_of(v)
  return math.floor(math.max(r, g, b) * alpha_of(v) / 255)
end
local function glow_pixel(strength)
  local s = math.max(0, math.min(255, strength)) / 255
  return pc.rgba(math.floor(GLOW_TINT[1] * s + 0.5), math.floor(GLOW_TINT[2] * s + 0.5), math.floor(GLOW_TINT[3] * s + 0.5), 255)
end

local function list_of(value)
  local out = {}
  if value == nil then return out end
  if type(value) ~= "table" and type(value) ~= "userdata" then return { value } end
  for i = 1, #value do out[i] = value[i] end
  return out
end

local function palette(diagonals)
  local out = {}
  for name, rgb in pairs(FACETS) do out[name] = rgb end
  if diagonals then for name, rgb in pairs(DIAGONALS) do out[name] = rgb end end
  return out
end

local function nearest_facet(r, g, b, diagonals)
  local best, best_distance
  local facets = palette(diagonals)
  for _, name in ipairs(FACET_ORDER) do
    local rgb = facets[name]
    if rgb then
      local d = (rgb[1] - r) ^ 2 + (rgb[2] - g) ^ 2 + (rgb[3] - b) ^ 2
      if not best or d < best_distance then best, best_distance = rgb, d end
    end
  end
  return best
end
N.nearest_facet = nearest_facet

local function each_layer(layers, out)
  for _, layer in ipairs(layers) do
    if layer.isGroup then each_layer(layer.layers, out) else out[#out + 1] = layer end
  end
  return out
end
local function all_layers(sprite) return each_layer(sprite.layers, {}) end
local function find_layer(sprite, name)
  for _, layer in ipairs(all_layers(sprite)) do if layer.name == name then return layer end end
end
local function has_prefix(name, prefix) return name:sub(1, #prefix) == prefix end
local function is_paint_layer(layer) return layer.isImage and not layer.isReference and not layer.isTilemap end
local function is_diffuse(layer)
  return is_paint_layer(layer) and not has_prefix(layer.name, NORMAL_PREFIX) and not has_prefix(layer.name, GLOW_PREFIX)
end

local function diffuse_of(sprite, name)
  local base_name = name:gsub("^" .. NORMAL_PREFIX:gsub("%-", "%%-"), ""):gsub("^" .. GLOW_PREFIX:gsub("%-", "%%-"), "")
  return find_layer(sprite, base_name) or find_layer(sprite, (base_name:gsub(GENERATED_SUFFIX .. "%d*$", "")))
end

local function target_layers(sprite, names)
  local wanted, out = list_of(names), {}
  for _, name in ipairs(wanted) do
    local layer = find_layer(sprite, name)
    if not layer then return nil, fail("Layer not found: " .. tostring(name)) end
    if has_prefix(name, NORMAL_PREFIX) or has_prefix(name, GLOW_PREFIX) then layer = diffuse_of(sprite, name) end
    if not layer or not is_diffuse(layer) then return nil, fail("Not a diffuse image layer: " .. tostring(name)) end
    out[#out + 1] = layer
  end
  if #wanted == 0 then
    for _, layer in ipairs(all_layers(sprite)) do
      if is_diffuse(layer) and layer.isVisible then out[#out + 1] = layer end
    end
  end
  return out
end

local function target_frames(sprite, params)
  local out, wanted = {}, list_of(params.frames)
  if #wanted > 0 then
    for _, f in ipairs(wanted) do
      local n = bridge().base.to_int(f)
      if n < 1 or n > #sprite.frames then return nil, fail("Frame out of range: " .. tostring(f)) end
      out[#out + 1] = n
    end
  elseif params.tag then
    local tag = bridge().base.find_tag(sprite, params.tag)
    if not tag then return nil, bridge().base.error_tag_not_found(params.tag) end
    for n = tag.fromFrame.frameNumber, tag.toFrame.frameNumber do out[#out + 1] = n end
  else
    for n = 1, #sprite.frames do out[n] = n end
  end
  return out
end

local function require_rgb_sprite()
  local sprite = app.sprite
  if not sprite then return nil, bridge().base.error_no_sprite() end
  if sprite.colorMode ~= ColorMode.RGB then return nil, fail("Voltage Normals needs an RGB sprite") end
  return sprite
end

local function new_grid(w, h)
  local grid = {}
  for i = 1, w * h do grid[i] = 0 end
  return grid
end

-- Canvas-sized pixel grid of one layer's cel, zero where empty.
local function read_cel(sprite, layer, frame)
  local w, h = sprite.width, sprite.height
  local grid = new_grid(w, h)
  local cel = layer and layer:cel(frame)
  if not cel then return grid, nil end
  local ox, oy = cel.position.x, cel.position.y
  for it in cel.image:pixels() do
    local v = it()
    if alpha_of(v) > 0 then
      local x, y = it.x + ox, it.y + oy
      if x >= 0 and y >= 0 and x < w and y < h then grid[y * w + x + 1] = v end
    end
  end
  return grid, cel
end

local function overlay(into, grid)
  for i = 1, #into do if grid[i] ~= 0 then into[i] = grid[i] end end
end

local function grid_bounds(grid, w, h)
  local x0, y0, x1, y1 = w, h, -1, -1
  for y = 0, h - 1 do
    local row = y * w
    for x = 0, w - 1 do
      if grid[row + x + 1] ~= 0 then
        if x < x0 then x0 = x end
        if x > x1 then x1 = x end
        if y < y0 then y0 = y end
        if y > y1 then y1 = y end
      end
    end
  end
  if x1 < 0 then return nil end
  return x0, y0, x1 - x0 + 1, y1 - y0 + 1
end

local function grid_image(sprite, grid)
  local w, h = sprite.width, sprite.height
  local x0, y0, cw, ch = grid_bounds(grid, w, h)
  if not x0 then return nil end
  local image = Image(cw, ch, ColorMode.RGB)
  for y = 0, ch - 1 do
    for x = 0, cw - 1 do image:drawPixel(x, y, grid[(y + y0) * w + x + x0 + 1]) end
  end
  return image, Point(x0, y0)
end

local function write_cel(sprite, layer, frame, grid)
  local old = layer:cel(frame)
  if old then sprite:deleteCel(old) end
  local image, position = grid_image(sprite, grid)
  if image then sprite:newCel(layer, frame, image, position) end
end

local function ensure_layer(sprite, name, beside)
  local layer = find_layer(sprite, name)
  if layer then return layer end
  local active = app.layer
  layer = sprite:newLayer()
  layer.name = name
  layer.isVisible = false
  layer.parent = beside.parent
  layer.stackIndex = beside.stackIndex + 1
  if active then app.layer = active end
  return layer
end

local function free_generated_name(sprite, base)
  local name, n = base .. GENERATED_SUFFIX, 1
  while find_layer(sprite, name) do
    n = n + 1
    name = base .. GENERATED_SUFFIX .. n
  end
  return name
end

local function find_clusters(grid, w, h)
  local labels, clusters = {}, {}
  for start = 1, #grid do
    local color = grid[start]
    if color ~= 0 and not labels[start] then
      local cluster = { id = #clusters + 1, color = color, pixels = {}, min_x = w, min_y = h, max_x = -1, max_y = -1 }
      clusters[#clusters + 1] = cluster
      local stack, sum_x, sum_y = { start }, 0, 0
      labels[start] = cluster.id
      while #stack > 0 do
        local i = table.remove(stack)
        cluster.pixels[#cluster.pixels + 1] = i
        local x, y = (i - 1) % w, (i - 1) // w
        sum_x, sum_y = sum_x + x, sum_y + y
        cluster.min_x, cluster.max_x = math.min(cluster.min_x, x), math.max(cluster.max_x, x)
        cluster.min_y, cluster.max_y = math.min(cluster.min_y, y), math.max(cluster.max_y, y)
        local function visit(j) if grid[j] == color and not labels[j] then labels[j] = cluster.id; stack[#stack + 1] = j end end
        if x > 0 then visit(i - 1) end
        if x < w - 1 then visit(i + 1) end
        if y > 0 then visit(i - w) end
        if y < h - 1 then visit(i + w) end
      end
      cluster.count = #cluster.pixels
      cluster.cx, cluster.cy = sum_x / cluster.count, sum_y / cluster.count
    end
  end
  return clusters, labels
end
N.find_clusters = find_clusters

local function solid_test(mask, w, h)
  return function(x, y) return x >= 0 and y >= 0 and x < w and y < h and mask[y * w + x + 1] end
end

local function open_within(solid, x, y, dx, dy, width)
  for d = 1, width do if not solid(x + dx * d, y + dy * d) then return true end end
  return false
end

local function side_facet(solid, x, y, width, diagonals)
  local up, down = open_within(solid, x, y, 0, -1, width), open_within(solid, x, y, 0, 1, width)
  local left, right = open_within(solid, x, y, -1, 0, width), open_within(solid, x, y, 1, 0, width)
  if left and right then left, right = false, false end
  if up and down then up, down = up, false end
  if diagonals then
    if up and left then return DIAGONALS.top_left end
    if up and right then return DIAGONALS.top_right end
    if down and left then return DIAGONALS.bottom_left end
    if down and right then return DIAGONALS.bottom_right end
  end
  if left then return FACETS.left end
  if right then return FACETS.right end
  if up then return FACETS.top end
  if down then return FACETS.bottom end
  return FACETS.front
end

-- Dark edge clusters are outlines: they take the facet of the surface they wrap instead of bevelling on their own.
local function outline_pixels(grid, clusters, w, h)
  local outline, sum, count = {}, 0, 0
  for _, cluster in ipairs(clusters) do sum, count = sum + luma(cluster.color) * cluster.count, count + cluster.count end
  local limit = count > 0 and sum / count * 0.55 or 0
  local alpha = {}
  for i = 1, #grid do alpha[i] = grid[i] ~= 0 end
  local solid = solid_test(alpha, w, h)
  for _, cluster in ipairs(clusters) do
    if luma(cluster.color) < limit then
      local edge, edge_count = {}, 0
      for _, i in ipairs(cluster.pixels) do
        local x, y = (i - 1) % w, (i - 1) // w
        if not (solid(x - 1, y) and solid(x + 1, y) and solid(x, y - 1) and solid(x, y + 1)) then
          edge[#edge + 1] = i
          edge_count = edge_count + 1
        end
      end
      if edge_count * 2 >= cluster.count then
        for _, i in ipairs(edge) do outline[i] = true end
      end
    end
  end
  return outline
end

local function split_large_clusters(result, clusters, w, options)
  if not options.split then return end
  for _, cluster in ipairs(clusters) do
    if cluster.count >= options.split_min and cluster.max_x - cluster.min_x >= 5 then
      local rows = {}
      for _, i in ipairs(cluster.pixels) do
        local y = (i - 1) // w
        rows[y] = rows[y] or {}
        table.insert(rows[y], (i - 1) % w)
      end
      for y, xs in pairs(rows) do
        table.sort(xs)
        local run = 1
        for k = 2, #xs + 1 do
          if k > #xs or xs[k] ~= xs[k - 1] + 1 then
            local length = k - run
            if length >= 5 then
              local band = math.max(1, math.floor(length * options.split_band + 0.5))
              for b = 0, band - 1 do
                local a, c = y * w + xs[run + b] + 1, y * w + xs[k - 1 - b] + 1
                if result[a] == FACETS.front then result[a] = FACETS.left end
                if result[c] == FACETS.front then result[c] = FACETS.right end
              end
            end
            run = k
          end
        end
      end
    end
  end
end

-- One frame of one layer: a facet per opaque pixel, returned as a pixel grid.
local function generate_grid(grid, w, h, options)
  local clusters = find_clusters(grid, w, h)
  local outline = options.merge_outlines and outline_pixels(grid, clusters, w, h) or {}
  local mask = {}
  for i = 1, #grid do mask[i] = grid[i] ~= 0 and not outline[i] end
  local solid = solid_test(mask, w, h)
  local alpha = {}
  for i = 1, #grid do alpha[i] = grid[i] ~= 0 end
  local shape = solid_test(alpha, w, h)
  local result = {}
  for y = 0, h - 1 do
    for x = 0, w - 1 do
      local i = y * w + x + 1
      if mask[i] then result[i] = side_facet(solid, x, y, options.width, options.diagonals) end
    end
  end
  split_large_clusters(result, clusters, w, options)
  local steps = { { 0, 1 }, { 0, -1 }, { 1, 0 }, { -1, 0 } }
  for y = 0, h - 1 do
    for x = 0, w - 1 do
      local i = y * w + x + 1
      if outline[i] then
        for _, step in ipairs(steps) do
          local j = (y + step[2]) * w + x + step[1] + 1
          if x + step[1] >= 0 and x + step[1] < w and y + step[2] >= 0 and y + step[2] < h and mask[j] then
            result[i] = result[j]
            break
          end
        end
        result[i] = result[i] or side_facet(shape, x, y, 1, options.diagonals)
      end
    end
  end
  local out = new_grid(w, h)
  for i, rgb in pairs(result) do out[i] = pixel_of(rgb) end
  return out
end
N.generate_grid = generate_grid

local function generator_options(params)
  local preset = PRESETS[params.preset or "Character"]
  if not preset then return nil, fail("Unknown preset: " .. tostring(params.preset)) end
  local b = function(key) if params[key] == nil then return preset[key] end return params[key] end
  return {
    width = math.max(1, bridge().base.to_int(b("width"), 1)), merge_outlines = b("merge_outlines"), split = b("split"),
    split_min = b("split_min"), split_band = preset.split_band > 0 and preset.split_band or 0.2,
    diagonals = params.diagonals == true,
  }
end

function N.generate(params)
  local sprite, err = require_rgb_sprite()
  if err then return err end
  local options, option_err = generator_options(params)
  if option_err then return option_err end
  local layers, layer_err = target_layers(sprite, params.layers)
  if layer_err then return layer_err end
  local frames, frame_err = target_frames(sprite, params)
  if frame_err then return frame_err end
  if #layers == 0 then return fail("No diffuse layers to generate from") end
  local written = {}
  bridge().base.transaction("Voltage Normals: generate", function()
    for _, layer in ipairs(layers) do
      local name = NORMAL_PREFIX .. layer.name
      if find_layer(sprite, name) and params.overwrite ~= true then name = free_generated_name(sprite, name) end
      local target = ensure_layer(sprite, name, layer)
      local count = 0
      for _, frame in ipairs(frames) do
        local grid = read_cel(sprite, layer, frame)
        write_cel(sprite, target, frame, generate_grid(grid, sprite.width, sprite.height, options))
        count = count + 1
      end
      written[#written + 1] = { layer = layer.name, normals = name, frames = count }
    end
  end)
  app.refresh()
  return ok({ written = written })
end

local function normal_layer_for(sprite, name)
  if has_prefix(name, NORMAL_PREFIX) then return find_layer(sprite, name) end
  return find_layer(sprite, NORMAL_PREFIX .. name)
end

local function resolve_paint_target(sprite, params)
  local name = params.layer
  if not name and app.layer then name = app.layer.name end
  if not name then return nil, nil, fail("No layer given") end
  if has_prefix(name, GLOW_PREFIX) then name = name:sub(#GLOW_PREFIX + 1) end
  local normals = normal_layer_for(sprite, name)
  local diffuse = diffuse_of(sprite, name)
  if not diffuse then return nil, nil, fail("No diffuse layer for " .. name) end
  return diffuse, normals
end

local function selected_indices(sprite)
  local selection = sprite.selection
  if selection.isEmpty then return nil end
  local out, b, w = {}, selection.bounds, sprite.width
  for y = math.max(0, b.y), math.min(sprite.height - 1, b.y + b.height - 1) do
    for x = math.max(0, b.x), math.min(w - 1, b.x + b.width - 1) do
      if selection:contains(x, y) then out[#out + 1] = y * w + x + 1 end
    end
  end
  return out
end

local function frame_param(sprite, params)
  return params.frame and bridge().base.to_int(params.frame) or app.frame.frameNumber
end

function N.apply(params)
  local sprite, err = require_rgb_sprite()
  if err then return err end
  local diffuse, normals, target_err = resolve_paint_target(sprite, params)
  if target_err then return target_err end
  local rgb = palette(true)[params.facet or "front"]
  if not rgb then return fail("Unknown facet: " .. tostring(params.facet)) end
  local indices = selected_indices(sprite)
  if not indices then return fail("Select pixels first") end
  normals = normals or ensure_layer(sprite, NORMAL_PREFIX .. diffuse.name, diffuse)
  local frame = frame_param(sprite, params)
  local shape = read_cel(sprite, diffuse, frame)
  local grid = read_cel(sprite, normals, frame)
  local changed = 0
  for _, i in ipairs(indices) do
    if shape[i] ~= 0 then grid[i] = pixel_of(rgb); changed = changed + 1 end
  end
  bridge().base.transaction("Voltage Normals: apply facet", function() write_cel(sprite, normals, frame, grid) end)
  app.refresh()
  return ok({ layer = normals.name, frame = frame, facet = params.facet or "front", pixels = changed })
end

function N.average(params)
  local sprite, err = require_rgb_sprite()
  if err then return err end
  local diffuse, normals, target_err = resolve_paint_target(sprite, params)
  if target_err then return target_err end
  if not normals then return fail("No Normals layer for " .. diffuse.name) end
  local indices = selected_indices(sprite)
  if not indices then return fail("Select pixels first") end
  local frame = frame_param(sprite, params)
  local shape = read_cel(sprite, diffuse, frame)
  local grid = read_cel(sprite, normals, frame)
  local r, g, b, n = 0, 0, 0, 0
  for _, i in ipairs(indices) do
    if grid[i] ~= 0 then
      local cr, cg, cb = rgb_of(grid[i])
      r, g, b, n = r + cr, g + cg, b + cb, n + 1
    end
  end
  if n == 0 then return fail("No normal pixels in the selection") end
  local rgb = nearest_facet(r / n, g / n, b / n, params.diagonals == true)
  local changed = 0
  for _, i in ipairs(indices) do
    if shape[i] ~= 0 then grid[i] = pixel_of(rgb); changed = changed + 1 end
  end
  bridge().base.transaction("Voltage Normals: average", function() write_cel(sprite, normals, frame, grid) end)
  app.refresh()
  return ok({ layer = normals.name, frame = frame, color = rgb, pixels = changed })
end

function N.select_cluster(params)
  local sprite, err = require_rgb_sprite()
  if err then return err end
  local diffuse, _, target_err = resolve_paint_target(sprite, params)
  if target_err then return target_err end
  local w, h = sprite.width, sprite.height
  local x, y = bridge().base.to_int(params.x), bridge().base.to_int(params.y)
  if not x or not y or x < 0 or y < 0 or x >= w or y >= h then return fail("Point is off the canvas") end
  local grid = read_cel(sprite, diffuse, frame_param(sprite, params))
  local clusters, labels = find_clusters(grid, w, h)
  local id = labels[y * w + x + 1]
  if not id then return fail("No diffuse pixel at that point") end
  local picked = Selection()
  local rows = {}
  for _, i in ipairs(clusters[id].pixels) do
    local row = (i - 1) // w
    rows[row] = rows[row] or {}
    rows[row][#rows[row] + 1] = (i - 1) % w
  end
  for row, xs in pairs(rows) do
    table.sort(xs)
    local first = 1
    for k = 2, #xs + 1 do
      if k > #xs or xs[k] ~= xs[k - 1] + 1 then
        picked:add(Rectangle(xs[first], row, xs[k - 1] - xs[first] + 1, 1))
        first = k
      end
    end
  end
  local mode = params.mode or "replace"
  local selection = Selection()
  selection:add(sprite.selection)
  if mode == "add" then selection:add(picked)
  elseif mode == "remove" then selection:subtract(picked)
  else selection = picked end
  sprite.selection = selection
  app.refresh()
  return ok({ cluster = id, pixels = clusters[id].count, mode = mode })
end

function N.validate(params)
  local sprite, err = require_rgb_sprite()
  if err then return err end
  local w, h = sprite.width, sprite.height
  local diagonals = params.diagonals == true
  local allowed = {}
  for _, rgb in pairs(palette(diagonals)) do allowed[pixel_of(rgb)] = true end
  local errors, warnings = {}, {}
  local function note(list, text) if #list < 200 then list[#list + 1] = text end end
  local summary = {}
  local wanted = {}
  for _, name in ipairs(list_of(params.layers)) do wanted[name] = true end
  for _, layer in ipairs(all_layers(sprite)) do
    local is_normal, is_glow = has_prefix(layer.name, NORMAL_PREFIX), has_prefix(layer.name, GLOW_PREFIX)
    if (is_normal or is_glow) and (next(wanted) == nil or wanted[layer.name]) then
      local info = { layer = layer.name, frames = 0, diffuse_frames = 0, alpha_errors = 0, palette_errors = 0, unbevelled = 0 }
      summary[#summary + 1] = info
      local diffuse = diffuse_of(sprite, layer.name)
      if layer.isVisible then note(warnings, layer.name .. ": visible, it would enter the albedo") end
      if not diffuse then
        note(errors, layer.name .. ": no matching diffuse layer")
      else
        for frame = 1, #sprite.frames do
          local shape, shape_cel = read_cel(sprite, diffuse, frame)
          local grid, cel = read_cel(sprite, layer, frame)
          if shape_cel then info.diffuse_frames = info.diffuse_frames + 1 end
          if cel then info.frames = info.frames + 1 end
          if is_normal then
            if shape_cel and not cel then note(errors, string.format("%s frame %d: missing cel", layer.name, frame)) end
            if cel and not shape_cel then note(errors, string.format("%s frame %d: cel without diffuse", layer.name, frame)) end
          end
          local mismatched, outside, bad, unbevelled = 0, 0, 0, 0
          local mask = {}
          for i = 1, #shape do mask[i] = shape[i] ~= 0 end
          local solid = solid_test(mask, w, h)
          for y = 0, h - 1 do
            for x = 0, w - 1 do
              local i = y * w + x + 1
              local has_shape, value = mask[i], grid[i]
              if is_normal then
                if has_shape ~= (value ~= 0) then mismatched = mismatched + 1 end
                if value ~= 0 then
                  if not allowed[value] then bad = bad + 1 end
                  if value == pixel_of(FACETS.front) and has_shape and
                    (not solid(x, y - 1) or not solid(x - 1, y) or not solid(x + 1, y)) then
                    unbevelled = unbevelled + 1
                  end
                end
              elseif value ~= 0 and not has_shape then
                outside = outside + 1
              end
            end
          end
          if is_normal and cel and shape_cel then
            local sx, sy, sw, sh = grid_bounds(shape, w, h)
            local nx, ny, nw, nh = grid_bounds(grid, w, h)
            if sx and (sx ~= nx or sy ~= ny or sw ~= nw or sh ~= nh) then
              note(errors, string.format("%s frame %d: bounds %s,%s %sx%s differ from diffuse %d,%d %dx%d",
                layer.name, frame, tostring(nx), tostring(ny), tostring(nw), tostring(nh), sx, sy, sw, sh))
            end
          end
          if mismatched > 0 then
            info.alpha_errors = info.alpha_errors + mismatched
            note(errors, string.format("%s frame %d: %d pixels differ from the diffuse alpha", layer.name, frame, mismatched))
          end
          if outside > 0 then
            info.alpha_errors = info.alpha_errors + outside
            note(errors, string.format("%s frame %d: %d glow pixels outside the diffuse alpha", layer.name, frame, outside))
          end
          if bad > 0 then
            info.palette_errors = info.palette_errors + bad
            note(errors, string.format("%s frame %d: %d pixels outside the palette", layer.name, frame, bad))
          end
          info.unbevelled = info.unbevelled + unbevelled
        end
        if info.unbevelled > 0 then note(warnings, string.format("%s: %d unbevelled silhouette pixels", layer.name, info.unbevelled)) end
      end
    end
  end
  return ok({ valid = #errors == 0, errors = errors, warnings = warnings, layers = summary })
end

local function saturate(v) if v < 0 then return 0 elseif v > 1 then return 1 end return v end
local function parse_color(value, fallback)
  if type(value) == "string" then
    local c = bridge().color_util.from_hex(value)
    return { c.red / 255, c.green / 255, c.blue / 255 }
  end
  return fallback or { 1, 1, 1 }
end

-- DeferredLighting.fx pointLightPixel: linear falloff over the 3D distance, no normalisation of the decoded normal.
local function point_contribution(light, x, y, nx, ny, nz)
  local dx, dy, dz = light.x - x, light.y - y, light.z
  local length = math.sqrt(dx * dx + dy * dy + dz * dz)
  local attenuation = saturate(1 - length / light.radius)
  if attenuation <= 0 or length == 0 then return 0 end
  return attenuation * light.intensity * math.max(0, (nx * dx + ny * dy + nz * dz) / length)
end

-- DeferredLighting.fx fluorescentLightPixel for an unrotated bar shining down.
local function bar_contribution(light, x, y, nx, ny, nz)
  local half, range = light.length * 0.5, light.range
  local radius = half + range * light.spread + light.softness
  local depth = y - light.y
  if depth < 0 then return 0 end
  local along = x - light.x
  local remaining = saturate(1 - math.sqrt((along / radius) ^ 2 + (depth / range) ^ 2))
  local edge = saturate(remaining * radius / light.softness)
  edge = edge * edge * (3 - 2 * edge)
  local attenuation = edge * remaining ^ light.falloff
  if attenuation <= 0 then return 0 end
  local sx = math.max(light.x - half, math.min(light.x + half, x))
  local dx, dy, dz = sx - x, light.y - y, light.z
  local length = math.sqrt(dx * dx + dy * dy + dz * dz)
  return attenuation * light.intensity * math.max(0, (nx * dx + ny * dy + nz * dz) / length)
end

local function light_contribution(light, x, y, nx, ny, nz)
  if light.kind == "fill" then return light.intensity end
  if light.kind == "bar" then return bar_contribution(light, x, y, nx, ny, nz) end
  return point_contribution(light, x, y, nx, ny, nz)
end

-- One pixel, as the engine's light pass plus FinalCombine compute it. Inputs in 0..1, normal in 0..255.
function N.shade_pixel(diffuse, normal, glow, x, y, lights, ambient)
  local nx, ny, nz = 2 * normal[1] / 255 - 1, -(2 * normal[2] / 255 - 1), 2 * normal[3] / 255 - 1
  local r, g, b = 0, 0, 0
  for _, light in ipairs(lights) do
    local amount = light_contribution(light, x, y, nx, ny, nz)
    r, g, b = r + amount * light.color[1], g + amount * light.color[2], b + amount * light.color[3]
  end
  r, g, b = saturate(r), saturate(g), saturate(b)
  return saturate(diffuse[1] * (r + (1 - r) * glow) + diffuse[1] * ambient[1]),
    saturate(diffuse[2] * (g + (1 - g) * glow) + diffuse[2] * ambient[2]),
    saturate(diffuse[3] * (b + (1 - b) * glow) + diffuse[3] * ambient[3])
end

local function light_preset(name, w, h)
  if name == "Warm room fill" then
    return { kind = "fill", color = { 1, 0.74, 0.5 }, intensity = 0.35 }
  elseif name == "Cable spark" then
    return { kind = "point", x = w * 0.8, y = h * 0.45, z = 18, radius = math.max(w, h) * 0.9, intensity = 2.2, color = { 0.45, 0.8, 1 } }
  end
  return { kind = "bar", x = w * 0.5, y = -8, z = 48, length = math.max(32, w * 0.6), range = h * 1.8, spread = 0.2,
    softness = 8, falloff = 1.5, intensity = 1.2, color = { 0.86, 0.92, 1 } }
end

local function normalize_light(source, w, h)
  local preset = source.preset and light_preset(source.preset, w, h) or {}
  local kind = source.kind or preset.kind or "point"
  local pick = function(key, default)
    if source[key] ~= nil then return source[key] end
    if preset[key] ~= nil then return preset[key] end
    return default
  end
  return {
    kind = kind, x = pick("x", w / 2), y = pick("y", -20), z = math.max(0.1, pick("z", 48)),
    radius = math.max(0.1, pick("radius", 200)), intensity = pick("intensity", 1.5),
    color = type(source.color) == "string" and parse_color(source.color) or source.color and { source.color[1], source.color[2], source.color[3] } or preset.color or { 1, 1, 1 },
    length = math.max(1, pick("length", 96)), range = math.max(1, pick("range", 140)), spread = pick("spread", 0.2),
    softness = math.max(0.1, pick("softness", 8)), falloff = math.max(0.1, pick("falloff", 1.5)),
  }
end

-- Which Normals layer stands for each diffuse layer: the base one, or its newest generated take when asked.
local function chosen_normal_layers(sprite, explicit, use_generated)
  local names, out = list_of(explicit), {}
  if #names > 0 then
    for _, name in ipairs(names) do
      local layer = find_layer(sprite, name)
      if not layer then return nil, bridge().base.error_layer_not_found(name) end
      out[#out + 1] = layer
    end
    return out
  end
  local best = {}
  for _, layer in ipairs(all_layers(sprite)) do
    if has_prefix(layer.name, NORMAL_PREFIX) then
      local base_name = layer.name:gsub(GENERATED_SUFFIX .. "%d*$", "")
      if base_name == layer.name then
        if not best[base_name] then best[base_name] = layer end
      elseif use_generated then
        best[base_name] = layer
      end
    end
  end
  for _, layer in pairs(best) do out[#out + 1] = layer end
  table.sort(out, function(a, b) return a.stackIndex < b.stackIndex end)
  return out
end

local function composite_layers(sprite, layers, frame)
  local grid = new_grid(sprite.width, sprite.height)
  for _, layer in ipairs(layers) do overlay(grid, (read_cel(sprite, layer, frame))) end
  return grid
end

local function glow_grid(sprite, frame)
  local grid = new_grid(sprite.width, sprite.height)
  for _, layer in ipairs(all_layers(sprite)) do
    if has_prefix(layer.name, GLOW_PREFIX) then
      local cel = read_cel(sprite, layer, frame)
      for i = 1, #grid do
        if cel[i] ~= 0 then grid[i] = math.max(grid[i], glow_strength(cel[i])) end
      end
    end
  end
  return grid
end

local function rim_grid(shape, normals, w, h, settings)
  local mask = {}
  for i = 1, #shape do mask[i] = shape[i] ~= 0 end
  local solid = solid_test(mask, w, h)
  local side = math.floor(saturate(settings.side_strength) * 255)
  local out = {}
  for i = 1, #shape do out[i] = 0 end
  for y = 0, h - 1 do
    for x = 0, w - 1 do
      local i = y * w + x + 1
      if mask[i] then
        local flat = normals[i] == 0
        local nx, ny = 0, 0
        if not flat then
          local r, g = rgb_of(normals[i])
          nx, ny = (r - 128) / 127, (g - 128) / 127
        end
        local up = open_within(solid, x, y, 0, -1, settings.width) and (flat or ny > settings.facing)
        local left = open_within(solid, x, y, -1, 0, settings.width) and (flat or -nx > settings.facing)
        local right = open_within(solid, x, y, 1, 0, settings.width) and (flat or nx > settings.facing)
        out[i] = up and 255 or (left or right) and side or 0
      end
    end
  end
  return out
end
N.rim_grid = rim_grid

local function rim_settings(params)
  return {
    width = math.max(1, bridge().base.to_int(params.width, 1)),
    side_strength = params.side_strength or 0.6, facing = params.facing or 0.15,
  }
end

-- Renders the chosen view of a frame into a canvas-sized image.
function N.render(sprite, frame, params)
  local w, h = sprite.width, sprite.height
  local mode = params.mode or "final"
  local image = Image(w, h, ColorMode.RGB)
  local layers, err = chosen_normal_layers(sprite, params.normal_layers, params.use_generated == true)
  if err then return nil, err end
  local normals = composite_layers(sprite, layers, frame)
  local glow = glow_grid(sprite, frame)
  if mode == "normals" or mode == "glow" then
    for y = 0, h - 1 do
      for x = 0, w - 1 do
        local i = y * w + x + 1
        if mode == "normals" then
          if normals[i] ~= 0 then image:drawPixel(x, y, normals[i]) end
        elseif glow[i] > 0 then
          image:drawPixel(x, y, glow_pixel(glow[i]))
        end
      end
    end
    return image
  end
  local flat = Image(w, h, ColorMode.RGB)
  flat:drawSprite(sprite, frame)
  local lights = {}
  local sources = list_of(params.lights)
  if #sources == 0 then sources = { { preset = params.preset or N.LIGHT_PRESET_NAMES[1] } } end
  for _, light in ipairs(sources) do lights[#lights + 1] = normalize_light(light, w, h) end
  local ambient = parse_color(params.ambient, { 0.2, 0.2, 0.2 })
  local front, rim = FACETS.front, nil
  if params.rim then rim = rim_grid(composite_layers(sprite, target_layers(sprite) or {}, frame), normals, w, h, rim_settings(params.rim)) end
  for it in flat:pixels() do
    local v = it()
    if alpha_of(v) > 0 then
      local x, y = it.x, it.y
      local i = y * w + x + 1
      local r, g, b = rgb_of(v)
      local normal = front
      if normals[i] ~= 0 then normal = { rgb_of(normals[i]) } end
      local glow_value = glow[i] / 255
      if rim and rim[i] > 0 then glow_value = math.max(glow_value, rim[i] / 255 * (params.rim.intensity or 0.6)) end
      local fr, fg, fb = N.shade_pixel({ r / 255, g / 255, b / 255 }, normal, glow_value, x + 0.5, y + 0.5, lights, ambient)
      it(pc.rgba(math.floor(fr * 255 + 0.5), math.floor(fg * 255 + 0.5), math.floor(fb * 255 + 0.5), alpha_of(v)))
    end
  end
  return flat
end

function N.preview(params)
  local sprite, err = require_rgb_sprite()
  if err then return err end
  local frame = frame_param(sprite, params)
  if frame < 1 or frame > #sprite.frames then return fail("Frame out of range") end
  local image, render_err = N.render(sprite, frame, params)
  if render_err then return render_err end
  local scale = math.max(1, bridge().base.to_int(params.scale, 1))
  if scale > 1 then image:resize { width = image.width * scale, height = image.height * scale, method = "nearest" } end
  return ok({ image = bridge().base.image_png(image), width = image.width, height = image.height, frame = frame, mode = params.mode or "final" })
end

local function glow_layer_for(sprite, diffuse)
  return ensure_layer(sprite, GLOW_PREFIX .. diffuse.name, normal_layer_for(sprite, diffuse.name) or diffuse)
end

local edit_state = {}

function N.glow(params)
  local sprite, err = require_rgb_sprite()
  if err then return err end
  local action = params.action or "strength"
  if action == "strength" then
    local v = glow_pixel((params.strength or 1) * 255)
    app.fgColor = Color { red = pc.rgbaR(v), green = pc.rgbaG(v), blue = pc.rgbaB(v), alpha = 255 }
    return ok({ strength = params.strength or 1 })
  end
  local diffuse, _, target_err = resolve_paint_target(sprite, params)
  if target_err then return target_err end
  if action == "edit" then
    if params.on == false then
      local layer = edit_state.layer
      if layer then
        pcall(function()
          layer.isVisible = false
          if edit_state.previous then app.layer = edit_state.previous end
        end)
      end
      edit_state = {}
      app.refresh()
      return ok({ editing = false })
    end
    local layer
    bridge().base.transaction("Voltage Normals: edit glow", function() layer = glow_layer_for(sprite, diffuse) end)
    edit_state = { layer = layer, previous = app.layer }
    layer.isVisible = true
    app.layer = layer
    app.refresh()
    N.glow({ action = "strength", strength = params.strength or 1 })
    return ok({ editing = true, layer = layer.name })
  end
  local indices = selected_indices(sprite)
  if not indices then return fail("Select pixels first") end
  local frame = frame_param(sprite, params)
  local shape = read_cel(sprite, diffuse, frame)
  local value = action == "clear" and 0 or glow_pixel((params.strength or 1) * 255)
  local layer
  local changed = 0
  bridge().base.transaction("Voltage Normals: glow " .. action, function()
    layer = glow_layer_for(sprite, diffuse)
    local grid = read_cel(sprite, layer, frame)
    for _, i in ipairs(indices) do
      if shape[i] ~= 0 then grid[i] = value; changed = changed + 1 end
    end
    write_cel(sprite, layer, frame, grid)
  end)
  app.refresh()
  return ok({ layer = layer.name, frame = frame, pixels = changed })
end

function N.rim(params)
  local sprite, err = require_rgb_sprite()
  if err then return err end
  local layers, layer_err = target_layers(sprite, params.layers)
  if layer_err then return layer_err end
  local frames, frame_err = target_frames(sprite, params)
  if frame_err then return frame_err end
  if #layers == 0 then return fail("No diffuse layers") end
  local w, h = sprite.width, sprite.height
  local settings = rim_settings(params)
  local skip = {}
  for _, name in ipairs(list_of(params.skip_layers)) do
    local layer = find_layer(sprite, name)
    if not layer then return bridge().base.error_layer_not_found(name) end
    skip[#skip + 1] = layer
  end
  local normal_layers, normal_err = chosen_normal_layers(sprite, nil, params.use_generated == true)
  if normal_err then return normal_err end
  local pixels = 0
  local function bake()
    for _, frame in ipairs(frames) do
      local shape = composite_layers(sprite, layers, frame)
      local rim = rim_grid(shape, composite_layers(sprite, normal_layers, frame), w, h, settings)
      local blocked = composite_layers(sprite, skip, frame)
      for _, layer in ipairs(layers) do
        local own = read_cel(sprite, layer, frame)
        local glow = glow_layer_for(sprite, layer)
        local grid = params.replace == true and new_grid(w, h) or read_cel(sprite, glow, frame)
        for i = 1, #grid do
          if own[i] ~= 0 and blocked[i] == 0 and rim[i] > 0 then
            pixels = pixels + 1
            if grid[i] == 0 or glow_strength(grid[i]) < rim[i] then grid[i] = glow_pixel(rim[i]) end
          end
        end
        write_cel(sprite, glow, frame, grid)
      end
    end
  end
  if params.bake then
    bridge().base.transaction("Voltage Normals: bake rim", bake)
    app.refresh()
    return ok({ baked = true, frames = #frames, pixels = pixels })
  end
  local frame = frames[1]
  local rim = rim_grid(composite_layers(sprite, layers, frame), composite_layers(sprite, normal_layers, frame), w, h, settings)
  local image = Image(w, h, ColorMode.RGB)
  for y = 0, h - 1 do
    for x = 0, w - 1 do
      local value = rim[y * w + x + 1]
      if value > 0 then image:drawPixel(x, y, glow_pixel(value)); pixels = pixels + 1 end
    end
  end
  return ok({ baked = false, frame = frame, pixels = pixels, image = bridge().base.image_png(image), width = w, height = h })
end

local function record_clusters(sprite, diffuse, normals, glow, frame)
  local w, h = sprite.width, sprite.height
  local shape = read_cel(sprite, diffuse, frame)
  local normal_grid = read_cel(sprite, normals, frame)
  local glow_cels = glow and read_cel(sprite, glow, frame) or nil
  local clusters = find_clusters(shape, w, h)
  for _, cluster in ipairs(clusters) do
    cluster.normal, cluster.glow = {}, {}
    local votes = {}
    for _, i in ipairs(cluster.pixels) do
      local x, y = (i - 1) % w - cluster.min_x, (i - 1) // w - cluster.min_y
      cluster.normal[y * 100000 + x] = normal_grid[i]
      if glow_cels then cluster.glow[y * 100000 + x] = glow_cels[i] end
      votes[normal_grid[i]] = (votes[normal_grid[i]] or 0) + 1
    end
    local best, best_votes = pixel_of(FACETS.front), 0
    for value, n in pairs(votes) do if value ~= 0 and n > best_votes then best, best_votes = value, n end end
    cluster.dominant = best
  end
  return clusters
end

local function match_clusters(sources, targets, w, h, threshold)
  local reach = math.sqrt(w * w + h * h)
  local pairs_found, used = {}, {}
  local candidates = {}
  for _, target in ipairs(targets) do
    for _, source in ipairs(sources) do
      if source.color == target.color then
        local size = math.abs(source.count - target.count) / math.max(source.count, target.count)
        local distance = math.sqrt((source.cx - target.cx) ^ 2 + (source.cy - target.cy) ^ 2) / reach
        local score = size + distance * 4
        if score <= threshold then candidates[#candidates + 1] = { source = source, target = target, score = score } end
      end
    end
  end
  table.sort(candidates, function(a, b) return a.score < b.score end)
  for _, candidate in ipairs(candidates) do
    if not used[candidate.source] and not used[candidate.target] then
      used[candidate.source], used[candidate.target] = true, true
      pairs_found[#pairs_found + 1] = candidate
    end
  end
  return pairs_found, used
end

local function carried_value(source, target, map, fallback, i, w)
  local x, y = (i - 1) % w - target.min_x, (i - 1) // w - target.min_y
  local sw, tw = source.max_x - source.min_x, target.max_x - target.min_x
  local sh, th = source.max_y - source.min_y, target.max_y - target.min_y
  local sx = tw > 0 and math.floor(x * sw / tw + 0.5) or 0
  local sy = th > 0 and math.floor(y * sh / th + 0.5) or 0
  local value = map[sy * 100000 + sx]
  if value ~= nil and value ~= 0 then return value end
  return fallback
end

function N.carry(params)
  local sprite, err = require_rgb_sprite()
  if err then return err end
  local diffuse, normals, target_err = resolve_paint_target(sprite, params)
  if target_err then return target_err end
  if not normals then return fail("No Normals layer for " .. diffuse.name) end
  local frames, frame_err = target_frames(sprite, params)
  if frame_err then return frame_err end
  local source_frame = params.source_frame and bridge().base.to_int(params.source_frame) or app.frame.frameNumber
  if source_frame < 1 or source_frame > #sprite.frames then return fail("Source frame out of range") end
  local w, h = sprite.width, sprite.height
  local glow = find_layer(sprite, GLOW_PREFIX .. diffuse.name)
  local sources = record_clusters(sprite, diffuse, normals, glow, source_frame)
  local report, plans = {}, {}
  for _, frame in ipairs(frames) do
    if frame ~= source_frame then
      local shape = read_cel(sprite, diffuse, frame)
      local current = read_cel(sprite, normals, frame)
      local current_glow = glow and read_cel(sprite, glow, frame) or nil
      local targets = find_clusters(shape, w, h)
      local matched, used = match_clusters(sources, targets, w, h, params.threshold or 0.75)
      local result = {}
      for i = 1, #current do result[i] = current[i] end
      local glow_result = {}
      if current_glow then for i = 1, #current_glow do glow_result[i] = current_glow[i] end end
      local changed = 0
      for _, pair in ipairs(matched) do
        for _, i in ipairs(pair.target.pixels) do
          local value = carried_value(pair.source, pair.target, pair.source.normal, pair.source.dominant, i, w)
          if value ~= result[i] then changed = changed + 1; result[i] = value end
          if current_glow then glow_result[i] = carried_value(pair.source, pair.target, pair.source.glow, 0, i, w) end
        end
      end
      local unmatched = 0
      for _, target in ipairs(targets) do if not used[target] then unmatched = unmatched + 1 end end
      report[#report + 1] = { frame = frame, clusters = #targets, matched = #matched, unmatched = unmatched, changed_pixels = changed }
      plans[#plans + 1] = { frame = frame, normals = result, glow = current_glow and glow_result or nil, current = current }
    end
  end
  N.last_carry = { sprite = sprite, plans = plans }
  if params.apply then
    bridge().base.transaction("Voltage Normals: carry", function()
      for _, plan in ipairs(plans) do
        write_cel(sprite, normals, plan.frame, plan.normals)
        if plan.glow then write_cel(sprite, glow, plan.frame, plan.glow) end
      end
    end)
    app.refresh()
    return ok({ applied = true, source_frame = source_frame, frames = report })
  end
  local count = math.min(#plans, 8)
  local strip = Image(math.max(1, w * count), h, ColorMode.RGB)
  for k = 1, count do
    local plan = plans[k]
    for y = 0, h - 1 do
      for x = 0, w - 1 do
        local i = y * w + x + 1
        local value = plan.normals[i]
        if value ~= 0 then
          strip:drawPixel((k - 1) * w + x, y, value ~= plan.current[i] and pc.rgba(255, 0, 255, 255) or value)
        end
      end
    end
  end
  return ok({ applied = false, source_frame = source_frame, frames = report, image = bridge().base.image_png(strip), width = strip.width, height = h })
end

N.commands = {
  normals_generate = N.generate, normals_apply = N.apply, normals_average = N.average, normals_select_cluster = N.select_cluster,
  normals_validate = N.validate, normals_glow = N.glow, normals_rim = N.rim, normals_carry = N.carry, normals_preview = N.preview,
}

function N.register()
  for name, handler in pairs(N.commands) do bridge().handlers[name] = handler end
end

local panel = {
  dlg = nil, image = nil, overlay = nil, status = "Ready", rim_overlay = false, pick = false, scale = 2,
  light = nil, listeners = {}, timer = nil, dirty = true, facet = "front", dragging = false,
}

local function call(name, params)
  local response = N.commands[name](params)
  if response.error then
    panel.status = response.error.message
    if panel.dlg then panel.dlg:modify { id = "status", text = panel.status } end
    return nil
  end
  return response.result
end

local function status(text)
  panel.status = text
  panel.dlg:modify { id = "status", text = text }
end

local function panel_layer()
  local name = panel.dlg.data.layer
  if name and name ~= "All visible" then return name end
  local layer = app.layer
  if layer then return layer.name end
end

local function layer_options()
  local options = { "All visible" }
  local sprite = app.sprite
  if sprite then
    for _, layer in ipairs(all_layers(sprite)) do
      if is_diffuse(layer) then options[#options + 1] = layer.name end
    end
  end
  return options
end

local function scope_params(params)
  local data = panel.dlg.data
  if data.scope == "This frame" then params.frames = { app.frame.frameNumber }
  elseif data.scope == "This tag" and app.tag then params.tag = app.tag.name end
  return params
end

local function chosen_light()
  local sprite, data = app.sprite, panel.dlg.data
  local base_light = { preset = data.light_preset }
  if data.light_color then
    local c = data.light_color
    base_light.color = { c.red / 255, c.green / 255, c.blue / 255 }
  end
  base_light.z, base_light.radius, base_light.intensity = data.light_z, data.light_radius, data.light_intensity / 100
  if data.light_preset == "Warm room fill" then base_light.radius = nil; base_light.z = nil end
  if panel.light then base_light.x, base_light.y = panel.light.x, panel.light.y end
  return base_light
end

local function refresh_preview()
  local sprite = app.sprite
  if not panel.dlg or not sprite or sprite.colorMode ~= ColorMode.RGB then return end
  local data = panel.dlg.data
  local params = {
    frame = app.frame.frameNumber, mode = data.view_mode == "Normals" and "normals" or data.view_mode == "Glow" and "glow" or "final",
    lights = { chosen_light() }, use_generated = data.use_generated,
  }
  local c = data.ambient
  params.ambient = string.format("#%02X%02X%02X", c.red, c.green, c.blue)
  if panel.rim_overlay then
    params.rim = { width = data.rim_width, side_strength = data.rim_side / 100, facing = data.rim_facing / 100, intensity = data.rim_intensity / 100 }
  end
  local ok_render, image, err = pcall(N.render, sprite, app.frame.frameNumber, params)
  if not ok_render then status(tostring(image)); return end
  if err then status(err.error.message); return end
  panel.image = image
  panel.overlay = nil
  panel.dlg:repaint()
end

local function canvas_transform()
  local image = panel.image
  if not image then return 1, 0, 0 end
  local width, height = panel.canvas_width or image.width, panel.canvas_height or image.height
  local scale = math.max(1, math.min(width // image.width, height // image.height))
  return scale, (width - image.width * scale) // 2, (height - image.height * scale) // 2
end

local function on_paint(ev)
  local gc = ev.context
  panel.canvas_width, panel.canvas_height = gc.width, gc.height
  gc.color = Color { red = 24, green = 26, blue = 32 }
  gc:fillRect(Rectangle(0, 0, gc.width, gc.height))
  local image = panel.overlay or panel.image
  if not image then return end
  local scale = math.max(1, math.min(gc.width // image.width, gc.height // image.height))
  local x, y = (gc.width - image.width * scale) // 2, (gc.height - image.height * scale) // 2
  gc:drawImage(image, Rectangle(0, 0, image.width, image.height), Rectangle(x, y, image.width * scale, image.height * scale))
end

local function canvas_to_sprite(ev)
  local scale, ox, oy = canvas_transform()
  return (ev.x - ox) / scale, (ev.y - oy) / scale
end

local function on_canvas_mouse(ev)
  if panel.pick and ev.button == MouseButton.LEFT then
    local x, y = canvas_to_sprite(ev)
    local mode = panel.dlg.data.pick_mode
    local result = call("normals_select_cluster", { layer = panel_layer(), x = math.floor(x), y = math.floor(y), mode = mode == "Add" and "add" or mode == "Remove" and "remove" or "replace" })
    if result then status(result.pixels .. " pixels selected") end
    return
  end
  panel.dragging = true
  panel.light = {}
  panel.light.x, panel.light.y = canvas_to_sprite(ev)
  refresh_preview()
end

local function on_canvas_move(ev)
  if panel.dragging or (panel.dlg.data.follow_mouse and not panel.pick) then
    panel.light = {}
    panel.light.x, panel.light.y = canvas_to_sprite(ev)
    refresh_preview()
  end
end

local function facet_picker_paint(ev)
  local gc = ev.context
  local cells = { "top_left", "top", "top_right", "left", "front", "right", "bottom_left", "bottom", "bottom_right" }
  local size = 24
  for k, name in ipairs(cells) do
    local rgb = palette(true)[name]
    local x, y = (k - 1) % 3 * size + 2, (k - 1) // 3 * size + 2
    local lit = { 2 * rgb[1] / 255 - 1, -(2 * rgb[2] / 255 - 1), 2 * rgb[3] / 255 - 1 }
    local amount = math.max(0, -0.5 * lit[1] - 0.5 * lit[2] + 0.7 * lit[3])
    local shade = math.floor(math.min(255, 50 + amount * 200))
    local enabled = FACETS[name] or (panel.dlg and panel.dlg.data.diagonals)
    gc.color = enabled and Color { red = shade, green = shade, blue = shade } or Color { red = 45, green = 45, blue = 45 }
    gc:fillRect(Rectangle(x, y, size - 2, size - 2))
    if name == panel.facet then
      gc.color = Color { red = 255, green = 170, blue = 40 }
      gc:strokeRect(Rectangle(x, y, size - 2, size - 2))
    end
  end
end

local function facet_picker_click(ev)
  local cells = { "top_left", "top", "top_right", "left", "front", "right", "bottom_left", "bottom", "bottom_right" }
  local col, row = (ev.x - 2) // 24, (ev.y - 2) // 24
  if col < 0 or col > 2 or row < 0 or row > 2 then return end
  local name = cells[row * 3 + col + 1]
  if FACETS[name] or panel.dlg.data.diagonals then
    panel.facet = name
    status("Facet: " .. name)
    panel.dlg:repaint()
  end
end

local function on_refresh_request() panel.dirty = true end

local function detach_listeners()
  for _, entry in ipairs(panel.listeners) do pcall(function() entry.events:off(entry.id) end) end
  panel.listeners = {}
end

local function attach_listeners()
  detach_listeners()
  local sprite = app.sprite
  if sprite then
    panel.listeners[#panel.listeners + 1] = { events = sprite.events, id = sprite.events:on("change", on_refresh_request) }
  end
end

local function on_site_change()
  if not panel.dlg then return end
  local sprite = app.sprite
  if sprite ~= panel.sprite then
    panel.sprite = sprite
    attach_listeners()
    panel.dlg:modify { id = "layer", options = layer_options() }
  end
  on_refresh_request()
end

-- Canvas size in UI units: what the main window leaves after the panel's other rows, at the current screen and UI scaling.
local function preview_size()
  local window, ui = app.window, math.max(1, app.preferences.general.ui_scale)
  local width = window and window.width // ui or 640
  local height = window and window.height // ui or 480
  return math.max(120, math.min(360, width * 3 // 10)), math.max(90, math.min(280, height - PANEL_ROWS_HEIGHT))
end

function N.close_panel()
  if panel.timer then panel.timer:stop(); panel.timer = nil end
  detach_listeners()
  if panel.site_listener then app.events:off(panel.site_listener); panel.site_listener = nil end
  local dlg = panel.dlg
  panel.dlg = nil
  if dlg then pcall(function() dlg:close() end) end
end

function N.open_panel(tab)
  N.close_panel()
  local dlg = Dialog { title = "Voltage Normals", onclose = function()
    if panel.dlg then
      panel.dlg = nil
      N.close_panel()
    end
  end }
  panel.dlg = dlg
  panel.light, panel.overlay = nil, nil
  local function preview_changed() refresh_preview() end

  dlg:combobox { id = "layer", label = "Layer", option = "All visible", options = layer_options() }

  dlg:tab { id = "tab_generate", text = "Generate" }
  dlg:combobox { id = "preset", label = "Preset", option = "Character", options = N.PRESET_NAMES, onchange = function()
    local preset = PRESETS[dlg.data.preset]
    dlg:modify { id = "width", value = preset.width }
    dlg:modify { id = "merge", selected = preset.merge_outlines }
    dlg:modify { id = "split", selected = preset.split }
    dlg:modify { id = "split_min", text = tostring(preset.split_min) }
  end }
  dlg:slider { id = "width", label = "Bevel width", min = 1, max = 4, value = 1 }
  dlg:check { id = "merge", label = "Merge Outlines", selected = false }
  dlg:check { id = "split", label = "Split large clusters", selected = false }
  dlg:number { id = "split_min", label = "Split from (px)", text = "24", decimals = 0 }
  dlg:check { id = "diagonals", label = "Diagonal facets", selected = false, onclick = function() dlg:repaint() end }
  dlg:combobox { id = "scope", label = "Frames", option = "All frames", options = { "This frame", "This tag", "All frames" } }
  dlg:check { id = "overwrite", label = "Overwrite Normals", selected = false }
  dlg:button { id = "generate", text = "Generate", onclick = function()
    local data = dlg.data
    local layer = data.layer ~= "All visible" and { data.layer } or nil
    local result = call("normals_generate", scope_params({
      layers = layer, preset = data.preset, width = data.width, merge_outlines = data.merge, split = data.split,
      split_min = data.split_min, diagonals = data.diagonals, overwrite = data.overwrite,
    }))
    if result then
      status("Wrote " .. #result.written .. " layer(s)")
      if not data.overwrite then dlg:modify { id = "use_generated", selected = true } end
      refresh_preview()
    end
  end }

  dlg:tab { id = "tab_paint", text = "Facets" }
  dlg:canvas { id = "facets", label = "Facet", width = 76, height = 76, onpaint = facet_picker_paint, onmousedown = facet_picker_click }
  dlg:button { id = "apply", text = "Apply to selection", onclick = function()
    local result = call("normals_apply", { layer = panel_layer(), facet = panel.facet })
    if result then status(result.pixels .. " pixels set to " .. result.facet) end
  end }
  dlg:button { id = "average", text = "Average selection", onclick = function()
    local result = call("normals_average", { layer = panel_layer(), diagonals = dlg.data.diagonals })
    if result then status(result.pixels .. " pixels averaged") end
  end }
  dlg:newrow()
  dlg:combobox { id = "pick_mode", label = "Cluster", option = "Replace", options = { "Replace", "Add", "Remove" } }
  dlg:button { id = "pick", text = "Select cluster", onclick = function()
    local editor = app.editor
    local mode = dlg.data.pick_mode
    local mode_name = mode == "Add" and "add" or mode == "Remove" and "remove" or "replace"
    if editor and editor.askPoint then
      editor:askPoint { title = "Click a cluster", onclick = function(ev)
        local result = call("normals_select_cluster", { layer = panel_layer(), x = ev.point.x, y = ev.point.y, mode = mode_name })
        if result then status(result.pixels .. " pixels selected") end
      end }
    else
      panel.pick = not panel.pick
      status(panel.pick and "Click a cluster in the preview" or "Ready")
    end
  end }
  dlg:button { id = "validate", text = "Validate", onclick = function()
    local result = call("normals_validate", { diagonals = dlg.data.diagonals })
    if result then
      local text = (result.valid and "Valid" or (#result.errors .. " error(s)")) .. ", " .. #result.warnings .. " warning(s)"
      status(text)
      local lines = {}
      for k = 1, math.min(12, #result.errors) do lines[#lines + 1] = result.errors[k] end
      for k = 1, math.min(6, #result.warnings) do lines[#lines + 1] = result.warnings[k] end
      if #lines > 0 then app.alert { title = "Voltage Normals: " .. text, text = lines } end
    end
  end }

  dlg:tab { id = "tab_glow", text = "Glow" }
  dlg:check { id = "glow_edit", label = "Glow brush", text = "Edit the Glow layer", selected = false, onclick = function()
    local result = call("normals_glow", { action = "edit", layer = panel_layer(), on = dlg.data.glow_edit, strength = dlg.data.glow_strength / 100 })
    if result then status(result.editing and ("Painting on " .. result.layer) or "Glow brush off") end
  end }
  dlg:slider { id = "glow_strength", label = "Strength %", min = 0, max = 100, value = 100, onchange = function()
    call("normals_glow", { action = "strength", strength = dlg.data.glow_strength / 100 })
  end }
  dlg:button { id = "glow_fill", text = "Fill selection", onclick = function()
    local result = call("normals_glow", { action = "fill", layer = panel_layer(), strength = dlg.data.glow_strength / 100 })
    if result then status(result.pixels .. " glow pixels") end
  end }
  dlg:button { id = "glow_clear", text = "Clear selection", onclick = function()
    local result = call("normals_glow", { action = "clear", layer = panel_layer() })
    if result then status(result.pixels .. " glow pixels cleared") end
  end }
  dlg:separator { text = "Rim" }
  dlg:slider { id = "rim_width", label = "Width", min = 1, max = 4, value = 1, onchange = preview_changed }
  dlg:slider { id = "rim_side", label = "SideStrength %", min = 0, max = 100, value = 60, onchange = preview_changed }
  dlg:slider { id = "rim_facing", label = "Facing %", min = 0, max = 100, value = 15, onchange = preview_changed }
  dlg:slider { id = "rim_intensity", label = "Intensity %", min = 0, max = 100, value = 60, onchange = preview_changed }
  dlg:check { id = "rim_overlay", label = "Preview", text = "Rim overlay", selected = false, onclick = function()
    panel.rim_overlay = dlg.data.rim_overlay
    refresh_preview()
  end }
  dlg:button { id = "rim_bake", text = "Bake rim into Glow layer", onclick = function()
    local data = dlg.data
    local result = call("normals_rim", {
      layers = data.layer ~= "All visible" and { data.layer } or nil, width = data.rim_width,
      side_strength = data.rim_side / 100, facing = data.rim_facing / 100, bake = true,
      use_generated = data.use_generated,
    })
    if result then status("Baked " .. result.pixels .. " rim pixels over " .. result.frames .. " frames"); refresh_preview() end
  end }

  dlg:tab { id = "tab_carry", text = "Carry" }
  dlg:label { id = "carry_source", label = "Source", text = "Record a frame first" }
  dlg:button { id = "carry_record", text = "Record this frame", onclick = function()
    panel.carry_source = app.frame.frameNumber
    dlg:modify { id = "carry_source", text = "Frame " .. panel.carry_source }
  end }
  dlg:combobox { id = "carry_scope", label = "Apply to", option = "This tag", options = { "This tag", "All frames" } }
  local function carry_params(apply)
    local params = { layer = panel_layer(), source_frame = panel.carry_source, apply = apply }
    if dlg.data.carry_scope == "This tag" and app.tag then params.tag = app.tag.name end
    return params
  end
  dlg:button { id = "carry_preview", text = "Preview diff", onclick = function()
    if not panel.carry_source then status("Record a frame first"); return end
    local result = call("normals_carry", carry_params(false))
    if result then
      local changed = 0
      for _, report in ipairs(result.frames) do changed = changed + report.changed_pixels end
      status(changed .. " pixels would change, shown in magenta")
      local plans = N.last_carry.plans
      local sprite = app.sprite
      local w, h = sprite.width, sprite.height
      local count = math.min(#plans, 4)
      if count > 0 then
        local strip = Image(w * count, h, ColorMode.RGB)
        for k = 1, count do
          local plan = plans[k]
          for y = 0, h - 1 do
            for x = 0, w - 1 do
              local i = y * w + x + 1
              local value = plan.normals[i]
              if value ~= 0 then
                strip:drawPixel((k - 1) * w + x, y, value ~= plan.current[i] and pc.rgba(255, 0, 255, 255) or value)
              end
            end
          end
        end
        panel.overlay = strip
        dlg:repaint()
      end
    end
  end }
  dlg:button { id = "carry_apply", text = "Apply", onclick = function()
    if not panel.carry_source then status("Record a frame first"); return end
    local result = call("normals_carry", carry_params(true))
    if result then status("Carried into " .. #result.frames .. " frames"); refresh_preview() end
  end }

  dlg:tab { id = "tab_light", text = "Light" }
  dlg:combobox { id = "light_preset", label = "Preset", option = N.LIGHT_PRESET_NAMES[1], options = N.LIGHT_PRESET_NAMES, onchange = function()
    local sprite = app.sprite
    if not sprite then return end
    local preset = light_preset(dlg.data.light_preset, sprite.width, sprite.height)
    panel.light = nil
    dlg:modify { id = "light_color", color = Color { red = math.floor(preset.color[1] * 255), green = math.floor(preset.color[2] * 255), blue = math.floor(preset.color[3] * 255) } }
    dlg:modify { id = "light_intensity", value = math.floor(preset.intensity * 100) }
    if preset.z then dlg:modify { id = "light_z", value = math.floor(preset.z) } end
    if preset.radius then dlg:modify { id = "light_radius", value = math.floor(preset.radius) } end
    refresh_preview()
  end }
  dlg:color { id = "light_color", label = "Colour", color = Color { red = 219, green = 234, blue = 255 }, onchange = preview_changed }
  dlg:slider { id = "light_z", label = "Height", min = 1, max = 256, value = 48, onchange = preview_changed }
  dlg:slider { id = "light_radius", label = "Radius", min = 8, max = 800, value = 200, onchange = preview_changed }
  dlg:slider { id = "light_intensity", label = "Intensity %", min = 0, max = 500, value = 120, onchange = preview_changed }
  dlg:color { id = "ambient", label = "Ambient", color = Color { red = 51, green = 51, blue = 51 }, onchange = preview_changed }
  dlg:check { id = "follow_mouse", label = "Mouse", text = "Light follows the mouse", selected = false }
  dlg:endtabs { id = "tabs", selected = tab or "tab_generate" }

  dlg:combobox { id = "view_mode", label = "View", option = "Final", options = { "Final", "Normals", "Glow" }, onchange = preview_changed }
  dlg:check { id = "use_generated", label = "Layers", text = "Preview generated layers", selected = false, onclick = preview_changed }
  dlg:check { id = "auto_refresh", label = "Refresh", text = "Auto refresh on sprite changes", selected = true }
  local view_width, view_height = preview_size()
  dlg:canvas { id = "view", width = view_width, height = view_height, onpaint = on_paint, onmousedown = on_canvas_mouse, onmousemove = on_canvas_move,
    onmouseup = function() panel.dragging = false end }
  dlg:label { id = "status", text = panel.status }
  dlg:show { wait = false }

  panel.sprite = app.sprite
  attach_listeners()
  panel.site_listener = app.events:on("sitechange", on_site_change)
  panel.timer = Timer { interval = 0.25, ontick = function()
    if panel.dlg and panel.dirty and panel.dlg.data.auto_refresh then
      panel.dirty = false
      refresh_preview()
    end
  end }
  panel.timer:start()
  refresh_preview()
end

function init(plugin)
  N.register()
  plugin:newCommand {
    id = "voltage_normals_panel", title = "Voltage Normals", group = "edit_fill",
    onclick = function() N.open_panel() end,
  }
end

function exit(plugin)
  N.close_panel()
end
