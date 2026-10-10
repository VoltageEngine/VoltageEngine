local native_app = app
local config = assert(app.params.config, "Run with an isolated ASEPRITE_USER_FOLDER and --script-param config=<folder>")
assert(app.fs.normalizePath(app.fs.userConfigPath) == app.fs.normalizePath(config), "Aseprite test config must be isolated")
assert(#app.fs.listFiles(app.fs.joinPath(config, "extensions")) == 0, "Test config must not load installed extensions")

local plugin_path = app.params.plugin or "Voltage.Editor/Aseprite/Resources/plugin.lua"
local file = assert(io.open(plugin_path, "rb"))
local source = file:read("*a")
file:close()
source = source:gsub("__VOLTAGE_PORT__", "0"):gsub("__VOLTAGE_TOKEN__", "test")
local bridge = assert(load(source .. [[
return { base = base, sprite = sprite_cmds, drawing = drawing_cmds, analysis = analysis_cmds, advanced = advanced_cmds,
  load_commands = load_commands, on_message = on_message, set_socket = function(socket) ws = socket end }
]], "Voltage Bridge tests", "t", _G))()

local temp_path = app.fs.joinPath(config, "temp")
assert(app.fs.makeDirectory(temp_path))
local test_fs = setmetatable({ tempPath = temp_path }, { __index = native_app.fs })
app = setmetatable({ fs = test_fs }, { __index = native_app })
local native_remove = os.remove
local remove_calls = 0
os.remove = function()
  remove_calls = remove_calls + 1
  error("Unsupported function 'remove'")
end

local function result(response)
  assert(not response.error, response.error and response.error.message)
  return assert(response.result)
end

local function png(encoded)
  local data = MCP_BASE64.decode(encoded)
  assert(data:sub(1, 8) == "\137PNG\13\10\26\10", "Command must return a valid PNG")
  return data
end

local function empty_temp(name)
  local path = app.fs.joinPath(temp_path, name)
  assert(app.fs.isFile(path) and app.fs.fileSize(path) == 0, "Legacy cleanup must truncate " .. name)
end

local sprite = Sprite(2, 2, ColorMode.RGB)
sprite.cels[1].image:putPixel(0, 0, app.pixelColor.rgba(255, 0, 0, 255))
sprite:newEmptyFrame()
local second = Image(sprite.spec)
second:putPixel(1, 1, app.pixelColor.rgba(0, 255, 0, 255))
sprite:newCel(sprite.layers[1], 2, second, Point(0, 0))

local screenshot = result(bridge.sprite.get_sprite_screenshot({ frame = 1 }))
png(screenshot.image)
assert(screenshot.width == 2 and screenshot.height == 2 and screenshot.frame == 1)
empty_temp("mcp_screenshot.png")

local image = result(bridge.drawing.get_image_data({ frame = 1, format = "base64_png" }))
png(image.image)
empty_temp("mcp_tmp.png")
result(bridge.drawing.set_image_data({ frame = 2, format = "base64_png", data = image.image }))
assert(sprite.layers[1]:cel(2).image:getPixel(0, 0) == sprite.layers[1]:cel(1).image:getPixel(0, 0), "PNG import must preserve pixel data")
empty_temp("mcp_tmp.png")

local comparison = result(bridge.analysis.compare_screenshots({ frame_a = 1, frame_b = 2 }))
png(comparison.diff_image)
assert(comparison.different_pixels == 0)
empty_temp("mcp_diff.png")

local preview = result(bridge.advanced.get_animation_preview({ max_frames = 2 }))
png(preview.image)
assert(preview.frame_count == 2 and preview.strip_width == 4)
empty_temp("mcp_anim_strip.png")
assert(remove_calls == 0, "Old Aseprite must not call unsupported os.remove")

app = setmetatable({ apiVersion = 33, fs = test_fs }, { __index = native_app })
local cleanup_path = app.fs.joinPath(temp_path, "cleanup-test")
local function write_temp()
  local out = assert(io.open(cleanup_path, "wb"))
  out:write("temporary data")
  out:close()
end

write_temp()
bridge.base.remove_temp_file(cleanup_path)
assert(remove_calls == 1 and app.fs.fileSize(cleanup_path) == 0, "Cleanup failure must fall back without failing the command")
os.remove = function(path)
  assert(path == cleanup_path)
  remove_calls = remove_calls + 1
  return true
end
write_temp()
bridge.base.remove_temp_file(cleanup_path)
assert(remove_calls == 2 and app.fs.fileSize(cleanup_path) > 0, "Modern successful removal must not recreate the temp file")

os.remove = native_remove
app = native_app
bridge.load_commands()
local sent = {}
bridge.set_socket({ sendText = function(_, encoded) sent[#sent + 1] = json.decode(encoded) end })
local targeted_sprite = Sprite(2, 2, ColorMode.RGB)
app.sprite = sprite
local function targeted_call(id, method, params)
  params.voltage_sprite_id = targeted_sprite.id
  bridge.on_message(json.encode({ jsonrpc = "2.0", id = id, method = method, params = params }))
  local response = sent[#sent]
  assert(response.id == id, "Targeted command must send its JSON-RPC response")
  assert(not response.error, response.error and response.error.message)
  assert(app.sprite.id == sprite.id, "Targeted command must restore the previous active sprite")
  return response.result
end

targeted_call("draw", "execute_script", { code = "app.sprite.cels[1].image:putPixel(0,0,app.pixelColor.rgba(0,0,255,255))" })
assert(targeted_sprite.cels[1].image:getPixel(0, 0) == app.pixelColor.rgba(0, 0, 255, 255))
local exported = targeted_call("export", "export_png", { path = app.fs.joinPath(temp_path, "target.png") })
assert(exported.exists)
targeted_call("save", "save_sprite", { path = app.fs.joinPath(temp_path, "target.aseprite"), rename = true })
targeted_call("close", "close_sprite", {})
assert(#app.sprites == 1, "Closing a targeted sprite must preserve other open sprites")

local closing_sprite = Sprite(1, 1, ColorMode.RGB)
app.sprite = closing_sprite
local closing_id = closing_sprite.id
bridge.on_message(json.encode({ jsonrpc = "2.0", id = "close-active", method = "close_sprite", params = { voltage_sprite_id = closing_id } }))
assert(sent[#sent].id == "close-active" and not sent[#sent].error, "Closing the previous active sprite must still send a response")
sprite:close()
print("PASS: Aseprite bridge PNG operations, versioned temp cleanup, and targeted draw/export/save/close JSON-RPC responses")
