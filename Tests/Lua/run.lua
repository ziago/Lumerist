-- LuaJIT test suite for the Lumerist Lightroom plug-in (protocol 4).
-- Run from the repository root: luajit Tests/Lua/run.lua
-- (or zsh Scripts/test-lua.sh). Exits non-zero when a test fails.

local scriptPath = debug.getinfo(1, "S").source:gsub("^@", "")
local testsDir = scriptPath:match("^(.*)/[^/]*$") or "."
local repoRoot = testsDir .. "/../.."
local pluginDir = repoRoot .. "/Lightroom/Lumerist.lrplugin"
package.path = pluginDir .. "/?.lua;" .. testsDir .. "/?.lua;" .. package.path

local Stubs = require "stubs"

-- The bridge token the app "wrote" for every test (BridgeToken.lua).
local TEST_TOKEN = string.rep("0123456789abcdef", 4)
local MAC_TOKEN_FILE = "/Users/test/Library/Application Support/Lumerist/bridge-token"

-- Minimal test framework -----------------------------------------------------

local tests = {}
local function test(name, fn)
    tests[#tests + 1] = { name = name, fn = fn }
end

local function fail(message, level)
    error(message, (level or 1) + 1)
end

local function describe(value)
    if type(value) == "string" then return string.format("%q", value) end
    return tostring(value)
end

local function eq(actual, expected, label)
    if actual ~= expected then
        fail((label or "value") .. ": expected " .. describe(expected) .. ", got " .. describe(actual), 2)
    end
end

local function near(actual, expected, label)
    if type(actual) ~= "number" or math.abs(actual - expected) > 1e-6 then
        fail((label or "value") .. ": expected ~" .. describe(expected) .. ", got " .. describe(actual), 2)
    end
end

local function truthy(value, label)
    if not value then fail((label or "condition") .. " should be true", 2) end
end

local function falsy(value, label)
    if value then fail((label or "condition") .. " should be false, got " .. describe(value), 2) end
end

local function contains(text, fragment, label)
    if type(text) ~= "string" or not text:find(fragment, 1, true) then
        fail((label or "text") .. ": expected " .. describe(text) .. " to contain " .. describe(fragment), 2)
    end
end

local PLUGIN_MODULES = { "Actions", "SocketBridge", "BridgeToken", "Json", "Catalog", "Develop", "Library", "Commands", "Parameters", "OpenApp" }

-- Fresh plug-in modules and globals for every test.
local function fresh()
    for _, name in ipairs(PLUGIN_MODULES) do
        package.loaded[name] = nil
    end
    for key in pairs(_G) do
        if type(key) == "string" and key:match("^lumerist") then
            _G[key] = nil
        end
    end
    local env = Stubs.new()
    env.files[MAC_TOKEN_FILE] = TEST_TOKEN .. "\n"
    _G.import = env.import
    _G._PLUGIN = { id = "io.github.ziago.lumerist.lightroom" }
    _G.WIN_ENV, _G.MAC_ENV = nil, true
    local Actions = require "Actions"
    Actions._resetForTests()
    local Bridge = require "SocketBridge"
    require("BridgeToken").refresh(env.clock)
    return env, Actions, Bridge
end

-- Independent strict JSON decoder (objects, arrays, strings with \u, numbers,
-- booleans, null) used as an oracle for the plug-in's encoder: if it parses
-- a line, Swift's JSONDecoder will too.
local function decodeJSON(text)
    local position = 1
    local function skip() position = text:find("[^ \t\r\n]", position) or (#text + 1) end
    local function err(message) error("JSON: " .. message .. " at " .. position, 0) end
    local parseValue
    local function parseString()
        if text:sub(position, position) ~= '"' then err("expected string") end
        position = position + 1
        local parts = {}
        while true do
            local char = text:sub(position, position)
            if char == "" then err("unterminated string") end
            if char == '"' then position = position + 1; break end
            if char:byte() < 0x20 then err("raw control character in string") end
            if char == "\\" then
                local escape = text:sub(position + 1, position + 1)
                local map = { ['"'] = '"', ["\\"] = "\\", ["/"] = "/", b = "\b", f = "\f", n = "\n", r = "\r", t = "\t" }
                if map[escape] then
                    parts[#parts + 1] = map[escape]
                    position = position + 2
                elseif escape == "u" then
                    local hex = text:sub(position + 2, position + 5)
                    if not hex:match("^%x%x%x%x$") then err("bad \\u escape") end
                    parts[#parts + 1] = string.char(tonumber(hex, 16) % 256)
                    position = position + 6
                else
                    err("unsupported escape \\" .. escape)
                end
            else
                parts[#parts + 1] = char
                position = position + 1
            end
        end
        return table.concat(parts)
    end
    function parseValue()
        skip()
        local char = text:sub(position, position)
        if char == "{" then
            position = position + 1
            local object = {}
            skip()
            if text:sub(position, position) == "}" then position = position + 1; return object end
            while true do
                skip()
                local key = parseString()
                skip()
                if text:sub(position, position) ~= ":" then err("expected ':'") end
                position = position + 1
                object[key] = parseValue()
                skip()
                local separator = text:sub(position, position)
                position = position + 1
                if separator == "}" then return object end
                if separator ~= "," then err("expected ',' or '}'") end
            end
        elseif char == "[" then
            position = position + 1
            local array = {}
            skip()
            if text:sub(position, position) == "]" then position = position + 1; return array end
            while true do
                array[#array + 1] = parseValue()
                skip()
                local separator = text:sub(position, position)
                position = position + 1
                if separator == "]" then return array end
                if separator ~= "," then err("expected ',' or ']'") end
            end
        elseif char == '"' then
            return parseString()
        elseif text:sub(position, position + 3) == "true" then
            position = position + 4; return true
        elseif text:sub(position, position + 4) == "false" then
            position = position + 5; return false
        elseif text:sub(position, position + 3) == "null" then
            position = position + 4; return nil
        else
            local number = text:match("^%-?%d+%.?%d*[eE]?[%+%-]?%d*", position)
            if not number or not tonumber(number) then err("unexpected character") end
            position = position + #number
            return tonumber(number)
        end
    end
    local value = parseValue()
    skip()
    if position <= #text then err("trailing data") end
    return value
end

-- Decodes the n-th line the bridge sent (without its trailing newline).
local function sent(env, index)
    local payload = env.responses[index or #env.responses]
    if not payload then fail("no line number " .. tostring(index), 2) end
    if payload:sub(-1) ~= "\n" then fail("line must end with a newline", 2) end
    return decodeJSON(payload:sub(1, -2))
end

-- The last line the plug-in logged: the bridge logs each change of state.
local function lastLog(env)
    local entry = env.log[#env.log]
    return entry and entry.message
end

-- Strict UTF-8 check (Swift's JSONDecoder rejects invalid UTF-8).
local function isValidUTF8(text)
    local index, length = 1, #text
    while index <= length do
        local byte = text:byte(index)
        local extra
        if byte < 0x80 then extra = 0
        elseif byte >= 0xC2 and byte <= 0xDF then extra = 1
        elseif byte >= 0xE0 and byte <= 0xEF then extra = 2
        elseif byte >= 0xF0 and byte <= 0xF4 then extra = 3
        else return false end
        for offset = 1, extra do
            local continuation = text:byte(index + offset)
            if not continuation or continuation < 0x80 or continuation > 0xBF then return false end
        end
        index = index + extra + 1
    end
    return true
end

local nextId = 0
local function request(fields)
    nextId = nextId + 1
    local object = { version = 4, id = nextId }
    for key, value in pairs(fields) do object[key] = value end
    return object
end

local function adjust(parameter, delta, scope)
    return request({ kind = "adjust", scope = scope or "develop", parameter = parameter, delta = delta })
end

local function run(Actions, object)
    local valid, reason = Actions.validate(object)
    if not valid then return false, reason end
    return Actions.run(object)
end

-- Json ------------------------------------------------------------------------

test("Json decodes objects, arrays, escapes and numbers", function()
    fresh()
    local Json = require "Json"
    local value = Json.decode('{"a":[1,-2.5,1e-05,true,false,null],"b":"x\\"y\\\\z\\u00e8","c":{}}')
    eq(value.a[1], 1, "integer")
    eq(value.a[2], -2.5, "decimal")
    near(value.a[3], 0.00001, "exponent")
    eq(value.a[4], true, "true")
    eq(value.a[5], false, "false")
    eq(value.b, 'x"y\\zè', "escapes")
    truthy(Json.isArray(value.a), "array marked")
end)

test("Json rejects malformed and trailing input", function()
    fresh()
    local Json = require "Json"
    for _, text in ipairs({ "", "{", '{"a":}', '{"a":1}x', "[1,]", '{"a":"\1"}', '{"a":1e}' }) do
        local value, reason = Json.decode(text)
        eq(value, nil, "decoded " .. text)
        truthy(reason, "reason for " .. text)
    end
    eq(Json.decode(42), nil, "non-string")
end)

test("Json encodes sorted keys, arrays and escaped strings", function()
    fresh()
    local Json = require "Json"
    local text = Json.encode({ z = 1, a = Json.array({ "x", 2.5 }), m = 'q"\n\1', e = Json.array({}) })
    eq(text, '{"a":["x",2.5],"e":[],"m":"q\\"\\n\\u0001","z":1}', "encoded")
    truthy(decodeJSON(text), "oracle parses it")
end)

test("Json leaves out non-finite numbers and trims decimals", function()
    fresh()
    local Json = require "Json"
    eq(Json.encode({ a = 0 / 0, b = math.huge, c = 0.05, d = -3 }), '{"c":0.05,"d":-3}', "numbers")
    eq(Json.encodeNumber(0.1 + 0.2), "0.3", "binary noise rounded")
end)

-- Validation ------------------------------------------------------------------

test("validate rejects other protocol versions and names protocol 4", function()
    local _, Actions = fresh()
    local ok, reason = Actions.validate({ version = 3, id = 1, kind = "ping" })
    falsy(ok, "v3 accepted")
    contains(reason, "protocol 4 required (got 3)", "reason")
    _, reason = Actions.validate({ version = string.rep("9", 500), id = 1, kind = "ping" })
    truthy(#reason < 80, "a long version is shortened in the reason")
end)

test("validate needs an integer id and a known kind", function()
    local _, Actions = fresh()
    falsy((Actions.validate({ version = 4, kind = "ping" })), "missing id")
    falsy((Actions.validate({ version = 4, id = 1.5, kind = "ping" })), "fractional id")
    falsy((Actions.validate({ version = 4, id = 1, kind = "explode" })), "unknown kind")
    truthy((Actions.validate({ version = 4, id = 1, kind = "ping" })), "ping")
    falsy((Actions.validate("ping")), "non-table")
end)

test("validate checks scope, parameter and the catalog limit", function()
    local _, Actions = fresh()
    truthy((Actions.validate(adjust("Exposure", 1))), "exposure 1 EV")
    local ok, reason = Actions.validate(adjust("Exposure", 1.01))
    falsy(ok, "exposure over limit")
    contains(reason, "between -1 and 1", "limit message")
    truthy((Actions.validate(adjust("SplitToningShadowHue", 20))), "catalog parameter")
    falsy((Actions.validate(adjust("Exposure", 0.1, "map"))), "unknown scope")
    falsy((Actions.validate(adjust("NotAParameter", 1))), "unknown parameter")
    falsy((Actions.validate(adjust("Dehaze", 1, "library"))), "not a Quick Develop control")
    truthy((Actions.validate(adjust("Exposure", 0.5, "library"))), "library exposure")
    for _, delta in ipairs({ 0 / 0, math.huge, -math.huge, "1" }) do
        falsy((Actions.validate(adjust("Contrast", delta))), "bad delta " .. tostring(delta))
    end
end)

test("validate allows reset only in Develop", function()
    local _, Actions = fresh()
    truthy((Actions.validate(request({ kind = "reset", scope = "develop", parameter = "Exposure" }))), "develop")
    local ok, reason = Actions.validate(request({ kind = "reset", scope = "library", parameter = "Exposure" }))
    falsy(ok, "library reset")
    contains(reason, "only available in Develop", "reason")
end)

test("validate checks gesture phases", function()
    local _, Actions = fresh()
    truthy((Actions.validate(request({ kind = "gesture", phase = "begin", parameter = "Exposure" }))), "begin")
    truthy((Actions.validate(request({ kind = "gesture", phase = "end" }))), "end without parameter")
    falsy((Actions.validate(request({ kind = "gesture", phase = "begin" }))), "begin needs a parameter")
    falsy((Actions.validate(request({ kind = "gesture", phase = "pause", parameter = "Exposure" }))), "phase")
end)

test("validate checks command names and arguments", function()
    local _, Actions = fresh()
    local good = {
        { "undo" }, { "rating", "0" }, { "rating", "5" }, { "colorLabel", "none" },
        { "switchModule", "library" }, { "selectTool", "masking" }, { "revealPanel", "Exposure" },
        { "createMask", "aiSelection/subject" }, { "addToMask", "brush" }, { "applyPreset", "A1-B2" },
    }
    for _, case in ipairs(good) do
        truthy((Actions.validate(request({ kind = "command", name = case[1], argument = case[2] }))), case[1])
    end
    local bad = {
        { "explode" }, { "rating", "6" }, { "colorLabel", "pink" }, { "switchModule", "photoshop" },
        { "selectTool", "lasso" }, { "revealPanel", "Nope" }, { "createMask", "foo/bar" },
        { "createMask", "brush/sky/x" }, { "applyPreset", "bad uuid!" }, { "undo", "x" },
    }
    for _, case in ipairs(bad) do
        falsy((Actions.validate(request({ kind = "command", name = case[1], argument = case[2] }))),
            case[1] .. " " .. tostring(case[2]))
    end
end)

test("validate checks query names", function()
    local _, Actions = fresh()
    for _, name in ipairs({ "context", "presets", "catalog" }) do
        truthy((Actions.validate(request({ kind = "query", name = name }))), name)
    end
    falsy((Actions.validate(request({ kind = "query", name = "secrets" }))), "unknown query")
end)

-- Develop adjust ----------------------------------------------------------------

test("adjust refuses outside Develop and without a photo", function()
    local env, Actions = fresh()
    env.module = "library"
    local ok, reason = run(Actions, adjust("Exposure", 0.1))
    falsy(ok, "outside Develop")
    contains(reason, "Develop", "reason")
    env.module = "develop"
    env.photo = nil
    ok, reason = run(Actions, adjust("Exposure", 0.1))
    falsy(ok, "no photo")
    contains(reason, "select a photo", "reason")
    eq(#env.develop.sets, 0, "setValue calls")
end)

test("an older Lightroom without a controller function gets a clear rejection", function()
    local env, Actions = fresh()
    env.develop.setValue = nil
    local ok, reason = run(Actions, adjust("Exposure", 0.1))
    falsy(ok, "accepted without setValue")
    contains(reason, "does not provide", "reason")
    contains(reason, "setValue", "names the function")
    env.develop.resetToDefault = nil
    ok, reason = run(Actions, { version = 4, id = 9, kind = "reset", scope = "develop", parameter = "Exposure" })
    falsy(ok, "reset accepted without resetToDefault")
    contains(reason, "resetToDefault", "reset reason")
end)

test("adjust adds the delta, clamps and reports value, range and unit", function()
    local env, Actions = fresh()
    local ok, message, details = run(Actions, adjust("Exposure", 0.35))
    truthy(ok, "accepted")
    eq(message, nil, "no message")
    eq(env.develop.lastSet().parameter, "Exposure", "parameter")
    near(details.value, 0.35, "value")
    eq(details.minimum, -5, "minimum")
    eq(details.maximum, 5, "maximum")
    eq(details.unit, "EV", "unit")
    eq(details.parameter, "Exposure", "ack parameter")
    eq(details.scope, "develop", "ack scope")
    env.develop.values.Contrast = 95
    run(Actions, adjust("Contrast", 20))
    eq(env.develop.lastSet().value, 100, "clamped")
end)

test("adjust at the slider limit is accepted without setValue", function()
    local env, Actions = fresh()
    env.develop.values.Contrast = 100
    local ok, message, details = run(Actions, adjust("Contrast", 5))
    truthy(ok, "accepted")
    contains(message, "slider limit", "message")
    eq(details.value, 100, "value")
    eq(#env.develop.sets, 0, "setValue calls")
end)

test("adjust reports a missing value and SDK errors", function()
    local env, Actions = fresh()
    env.develop.values.Texture = nil
    local ok, reason = run(Actions, adjust("Texture", 1))
    falsy(ok, "missing value")
    contains(reason, "did not report Texture", "reason")
    env.develop.failWith = "boom"
    ok, reason = run(Actions, adjust("Exposure", 0.1))
    falsy(ok, "SDK error")
    contains(reason, "boom", "reason")
end)

test("Temperature points become 50 K on a Kelvin scale", function()
    local env, Actions = fresh()
    env.develop.ranges.Temperature = { 2000, 50000 }
    env.develop.values.Temperature = 5500
    local _, _, details = run(Actions, adjust("Temperature", 2))
    eq(env.develop.lastSet().value, 5600, "kelvin")
    eq(details.unit, "K", "unit")
end)

test("Temperature points stay points on the rendered-file scale", function()
    local env, Actions = fresh()
    local _, _, details = run(Actions, adjust("Temperature", 2))
    eq(env.develop.lastSet().value, 2, "points")
    eq(details.unit, "pt", "unit")
end)

test("hue parameters wrap around their range", function()
    local env, Actions = fresh()
    env.develop.values.SplitToningShadowHue = 350
    run(Actions, adjust("SplitToningShadowHue", 20))
    near(env.develop.lastSet().value, 10, "wrapped up")
    env.develop.flush()
    run(Actions, adjust("SplitToningShadowHue", -20))
    near(env.develop.lastSet().value, 350, "wrapped down")
end)

test("choice parameters step through options numerically", function()
    local env, Actions = fresh()
    local ok, message, details = run(Actions, adjust("PostCropVignetteStyle", 1))
    truthy(ok, "accepted")
    eq(env.develop.lastSet().value, 2, "next option")
    eq(message, "Color Priority", "option name")
    eq(details.unit, "choice", "unit")
    env.develop.values.PostCropVignetteStyle = 3
    ok, message = run(Actions, adjust("PostCropVignetteStyle", 1))
    truthy(ok, "limit accepted")
    contains(message, "last", "limit message")
end)

test("choice parameters reported as names keep Lightroom's spelling", function()
    local env, Actions = fresh()
    env.develop.values.PostCropVignetteStyle = "Highlight Priority"
    run(Actions, adjust("PostCropVignetteStyle", 1))
    eq(env.develop.lastSet().value, "Color Priority", "spaced spelling")
    env.develop.values.PostCropVignetteStyle = "PAINT_OVERLAY"
    run(Actions, adjust("PostCropVignetteStyle", -1))
    eq(env.develop.lastSet().value, "COLOR_PRIORITY", "upper spelling")
    env.develop.values.PostCropVignetteStyle = "Mystery"
    local ok, reason = run(Actions, adjust("PostCropVignetteStyle", 1))
    falsy(ok, "unknown name")
    contains(reason, "unknown", "reason")
end)

test("crop zoom scales the crop around its centre", function()
    local env, Actions = fresh()
    local ok, _, details = run(Actions, adjust("lumerist.cropZoom", 10))
    truthy(ok, "accepted")
    near(env.develop.values.CropLeft, 0, "not applied yet")
    env.develop.flush()
    near(env.develop.values.CropLeft, 0.05, "left")
    near(env.develop.values.CropRight, 0.95, "right")
    near(env.develop.values.CropTop, 0.05, "top")
    near(env.develop.values.CropBottom, 0.95, "bottom")
    near(details.value, 90, "percent")
    eq(details.unit, "%", "unit")
end)

test("crop zoom keeps the aspect ratio and stops at the image", function()
    local env, Actions = fresh()
    env.develop.values.CropLeft, env.develop.values.CropRight = 0.2, 0.6
    env.develop.values.CropTop, env.develop.values.CropBottom = 0.4, 0.6
    run(Actions, adjust("lumerist.cropZoom", -20))
    env.develop.flush()
    local width = env.develop.values.CropRight - env.develop.values.CropLeft
    local height = env.develop.values.CropBottom - env.develop.values.CropTop
    near(width / height, 2, "aspect kept")
    near((env.develop.values.CropLeft + env.develop.values.CropRight) / 2, 0.4, "centre x")
    env.develop.values.CropLeft, env.develop.values.CropRight = 0, 1
    env.develop.values.CropTop, env.develop.values.CropBottom = 0, 1
    Actions._resetForTests()
    local ok, message = run(Actions, adjust("lumerist.cropZoom", -10))
    truthy(ok, "limit accepted")
    contains(message, "limit", "full image cannot zoom out")
end)

test("crop zoom builds on its own queued edges", function()
    local env, Actions = fresh()
    run(Actions, adjust("lumerist.cropZoom", 10))
    run(Actions, adjust("lumerist.cropZoom", 10)) -- getValue still reports the full image
    local lastLeft
    for _, set in ipairs(env.develop.sets) do
        if set.parameter == "CropLeft" then lastLeft = set.value end
    end
    near(lastLeft, 0.095, "second zoom builds on the first")
end)

-- Stale reads -------------------------------------------------------------------

test("quick adjustments build on queued targets while getValue is stale", function()
    local env, Actions = fresh()
    for _ = 1, 3 do run(Actions, adjust("Exposure", 0.05)) end
    near(env.develop.lastSet().value, 0.15, "chained")
end)

test("a partial flush still continues from the latest target", function()
    local env, Actions = fresh()
    run(Actions, adjust("Exposure", 0.05))
    run(Actions, adjust("Exposure", 0.05))
    env.develop.flush(1)
    run(Actions, adjust("Exposure", 0.05))
    near(env.develop.lastSet().value, 0.15, "chain kept")
end)

test("an external change, a different photo or time restart the chain", function()
    local env, Actions = fresh()
    run(Actions, adjust("Exposure", 0.05))
    env.develop.pending = {}
    env.develop.values.Exposure = 1
    run(Actions, adjust("Exposure", 0.05))
    near(env.develop.lastSet().value, 1.05, "external change")
    env.develop.flush()
    env.photo = Stubs.makePhoto(2)
    env.develop.values.Exposure = 0
    run(Actions, adjust("Exposure", 0.05))
    near(env.develop.lastSet().value, 0.05, "new photo")
    env.clock = env.clock + 2
    run(Actions, adjust("Exposure", 0.05))
    near(env.develop.lastSet().value, 0.05, "window expired")
end)

-- Masks (local_*) ---------------------------------------------------------------

test("mask parameters need the Masking panel", function()
    local env, Actions = fresh()
    local ok, reason = run(Actions, adjust("local_Exposure", 0.1))
    falsy(ok, "not masking")
    contains(reason, "Masking panel", "reason")
    eq(#env.develop.sets, 0, "never touches the global slider")
end)

test("mask parameters need a selected mask", function()
    local env, Actions = fresh()
    env.tool = "masking"
    local ok, reason = run(Actions, adjust("local_Exposure", 0.1))
    falsy(ok, "no mask")
    contains(reason, "select a mask first", "reason")
    eq(#env.develop.sets, 0, "setValue calls")
end)

test("mask parameters change the selected mask and report it", function()
    local env, Actions = fresh()
    env.tool, env.selectedMask = "masking", "mask-1"
    local ok, _, details = run(Actions, adjust("local_Exposure", 0.25))
    truthy(ok, "accepted")
    eq(env.develop.lastSet().parameter, "local_Exposure", "local parameter")
    near(details.value, 0.25, "value")
    eq(details.minimum, -4, "minimum")
    eq(details.maximum, 4, "maximum")
    eq(details.parameter, "local_Exposure", "ack parameter")
    eq(details.unit, "EV", "unit")
end)

test("a mask parameter Lightroom does not report is rejected, not redirected", function()
    local env, Actions = fresh()
    env.tool, env.selectedMask = "masking", "mask-1"
    env.develop.values.local_Contrast = nil
    local ok, reason = run(Actions, adjust("local_Contrast", 2))
    falsy(ok, "rejected")
    contains(reason, "select a mask first", "reason")
    eq(#env.develop.sets, 0, "no global fallback")
end)

test("mask hue wraps and mask reset needs a selected mask", function()
    local env, Actions = fresh()
    env.tool, env.selectedMask = "masking", "mask-1"
    env.develop.values.local_Hue = 170
    run(Actions, adjust("local_Hue", 20))
    near(env.develop.lastSet().value, -170, "wrapped")
    local ok = run(Actions, request({ kind = "reset", scope = "develop", parameter = "local_Hue" }))
    truthy(ok, "reset")
    local _, args = env.called("resetToDefault")
    eq(args[1], "local_Hue", "reset parameter")
    env.selectedMask = nil
    local rejected, reason = run(Actions, request({ kind = "reset", scope = "develop", parameter = "local_Hue" }))
    falsy(rejected, "reset without mask")
    contains(reason, "select a mask first", "reason")
end)

test("a mask gesture needs the Masking panel and a selected mask", function()
    local env, Actions = fresh()
    local ok, reason = run(Actions, request({ kind = "gesture", phase = "begin", parameter = "local_Exposure" }))
    falsy(ok, "not masking")
    contains(reason, "Masking panel", "reason")
    env.tool = "masking"
    ok, reason = run(Actions, request({ kind = "gesture", phase = "begin", parameter = "local_Exposure" }))
    falsy(ok, "no mask")
    contains(reason, "select a mask first", "reason")
    eq((env.called("startTracking")), 0, "never starts tracking")
end)

test("a mask gesture ends as a local history step", function()
    local env, Actions = fresh()
    env.tool, env.selectedMask = "masking", "mask-1"
    run(Actions, request({ kind = "gesture", phase = "begin", parameter = "local_Exposure" }))
    run(Actions, request({ kind = "gesture", phase = "end", parameter = "local_Exposure" }))
    local _, args = env.called("stopTracking")
    eq(args[1], true, "stopTracking(true)")
    run(Actions, request({ kind = "gesture", phase = "begin", parameter = "Exposure" }))
    run(Actions, request({ kind = "gesture", phase = "end" }))
    _, args = env.called("stopTracking")
    eq(args[1], false, "global gesture")
end)

-- Reset and gesture ---------------------------------------------------------------

test("reset uses resetToDefault and resetCrop for the crop zoom", function()
    local env, Actions = fresh()
    truthy((run(Actions, request({ kind = "reset", scope = "develop", parameter = "Exposure" }))), "reset")
    local count, args = env.called("resetToDefault")
    eq(count, 1, "resetToDefault calls")
    eq(args[1], "Exposure", "parameter")
    run(Actions, request({ kind = "reset", scope = "develop", parameter = "lumerist.cropZoom" }))
    eq((env.called("resetCrop")), 1, "resetCrop")
    env.module = "library"
    falsy((run(Actions, request({ kind = "reset", scope = "develop", parameter = "Exposure" }))), "outside Develop")
end)

test("gesture begin and end use tracking when available", function()
    local env, Actions = fresh()
    truthy((run(Actions, request({ kind = "gesture", phase = "begin", parameter = "Contrast" }))), "begin")
    local _, args = env.called("startTracking")
    eq(args[1], "Contrast", "tracked parameter")
    truthy((run(Actions, request({ kind = "gesture", phase = "end", parameter = "Contrast" }))), "end")
    eq((env.called("stopTracking")), 1, "stopTracking")
    env.develop.startTracking = nil
    local ok, reason = run(Actions, request({ kind = "gesture", phase = "begin", parameter = "Contrast" }))
    falsy(ok, "missing API")
    contains(reason, "not available", "reason")
end)

-- Library Quick Develop --------------------------------------------------------------

test("library adjust needs the Library module and photos", function()
    local env, Actions = fresh()
    local ok, reason = run(Actions, adjust("Exposure", 0.1, "library"))
    falsy(ok, "in Develop")
    contains(reason, "Library", "reason")
    env.module = "library"
    env.photos = {}
    ok, reason = run(Actions, adjust("Exposure", 0.1, "library"))
    falsy(ok, "no photos")
    contains(reason, "select photos", "reason")
end)

test("library adjust is relative on every selected photo in one transaction", function()
    local env, Actions = fresh()
    env.module = "library"
    local a = Stubs.makePhoto(1, { Exposure2012 = 0.5 })
    local b = Stubs.makePhoto(2, { Exposure2012 = -1 })
    local c = Stubs.makePhoto(3, { Exposure2012 = 4.9 })
    env.photos = { a, b, c }
    local ok, message, details = run(Actions, adjust("Exposure", 0.3, "library"))
    truthy(ok, "accepted")
    eq(#env.writeAccess, 1, "one transaction")
    contains(env.writeAccess[1].name, "Quick Exposure", "history name")
    near(a.settings.Exposure2012, 0.8, "photo a")
    near(b.settings.Exposure2012, -0.7, "photo b")
    near(c.settings.Exposure2012, 5, "photo c clamped")
    contains(message, "3 of 3 photos", "message")
    near(details.value, 0.8, "first photo value")
    eq(details.scope, "library", "scope")
    eq(details.unit, "EV", "unit")
end)

test("library temperature and tint follow raw and rendered keys", function()
    local env, Actions = fresh()
    env.module = "library"
    local raw = Stubs.makePhoto(1, { Temperature = 5000, Tint = 10 })
    local jpeg = Stubs.makePhoto(2, { IncrementalTemperature = 0, IncrementalTint = 0 })
    env.photos = { raw, jpeg }
    local _, _, details = run(Actions, adjust("Temperature", 3, "library"))
    eq(raw.settings.Temperature, 5150, "raw kelvin")
    eq(jpeg.settings.IncrementalTemperature, 3, "jpeg points")
    eq(details.unit, "K", "first photo unit")
    run(Actions, adjust("Tint", -5, "library"))
    eq(raw.settings.Tint, 5, "raw tint")
    eq(jpeg.settings.IncrementalTint, -5, "jpeg tint")
end)

test("library adjust at the limit changes nothing", function()
    local env, Actions = fresh()
    env.module = "library"
    env.photo.settings.Contrast2012 = 100
    local ok, message = run(Actions, adjust("Contrast", 5, "library"))
    truthy(ok, "accepted")
    contains(message, "limit", "message")
    eq(#env.photo.applied, 0, "nothing applied")
end)

-- Commands ----------------------------------------------------------------------

local function command(Actions, name, argument)
    return run(Actions, request({ kind = "command", name = name, argument = argument }))
end

test("undo and redo respect what Lightroom can do", function()
    local env, Actions = fresh()
    truthy((command(Actions, "undo")), "undo")
    eq((env.called("undo")), 1, "undo called")
    env.canRedo = false
    local ok, reason = command(Actions, "redo")
    falsy(ok, "redo")
    contains(reason, "nothing to redo", "reason")
end)

test("Develop-only commands are rejected elsewhere", function()
    local env, Actions = fresh()
    truthy((command(Actions, "autoTone")), "auto tone in Develop")
    eq((env.called("setAutoTone")), 1, "setAutoTone")
    env.module = "library"
    local ok, reason = command(Actions, "autoTone")
    falsy(ok, "auto tone in Library")
    contains(reason, "Develop", "reason")
    falsy((command(Actions, "resetAll")), "reset all in Library")
    falsy((command(Actions, "selectTool", "crop")), "tool in Library")
end)

test("selection commands drive LrSelection", function()
    local env, Actions = fresh()
    command(Actions, "nextPhoto")
    command(Actions, "previousPhoto")
    command(Actions, "flagPick")
    command(Actions, "flagReject")
    command(Actions, "flagRemove")
    command(Actions, "rating", "4")
    command(Actions, "rating", "0")
    command(Actions, "colorLabel", "red")
    eq((env.called("nextPhoto")), 1, "next")
    eq((env.called("previousPhoto")), 1, "previous")
    eq((env.called("flagAsPick")), 1, "pick")
    eq((env.called("flagAsReject")), 1, "reject")
    eq((env.called("removeFlag")), 1, "unflag")
    local _, rating = env.called("setRating")
    eq(rating[1], 4, "rating")
    eq((env.called("clearRating")), 1, "rating 0 clears")
    local _, label = env.called("setColorLabel")
    eq(label[1], "red", "label")
end)

test("view and tool commands", function()
    local env, Actions = fresh()
    command(Actions, "switchModule", "library")
    command(Actions, "selectTool", "masking")
    command(Actions, "revealPanel", "Exposure")
    command(Actions, "zoomToggle")
    command(Actions, "showClipping")
    command(Actions, "autoWhiteBalance")
    local _, moduleArgs = env.called("switchToModule")
    eq(moduleArgs[1], "library", "module")
    local _, toolArgs = env.called("selectTool")
    eq(toolArgs[1], "masking", "tool")
    eq((env.called("revealPanel")), 1, "panel")
    eq((env.called("toggleZoom")), 1, "zoom")
    eq((env.called("showClipping")), 1, "clipping")
    eq((env.called("setAutoWhiteBalance")), 1, "auto WB")
end)

test("mask commands split type and subtype", function()
    local env, Actions = fresh()
    command(Actions, "createMask", "aiSelection/subject")
    local _, args = env.called("createNewMask")
    eq(args[1], "aiSelection", "type")
    eq(args[2], "subject", "subtype")
    command(Actions, "addToMask", "brush")
    _, args = env.called("addToCurrentMask")
    eq(args[1], "brush", "type")
    eq(args[2], nil, "no subtype")
    command(Actions, "subtractFromMask", "rangeMask/luminance")
    command(Actions, "intersectWithMask", "aiSelection/sky")
    command(Actions, "invertMask")
    command(Actions, "toggleMaskOverlay")
    eq((env.called("subtractFromCurrentMask")), 1, "subtract")
    eq((env.called("intersectWithCurrentMask")), 1, "intersect")
    eq((env.called("invertMask")), 1, "invert")
    eq((env.called("toggleOverlay")), 1, "overlay")
end)

test("applyPreset applies to the active photo in Develop and every target in Library", function()
    local env, Actions = fresh()
    local ok, message = command(Actions, "applyPreset", "C3-D4")
    truthy(ok, "develop")
    eq(message, "Neutral", "preset name")
    eq(#env.photo.presets, 1, "applied")
    eq(env.photo.presets[1].plugin, _PLUGIN, "plug-in passed")
    env.module = "library"
    local a, b = Stubs.makePhoto(5), Stubs.makePhoto(6)
    env.photos = { a, b }
    command(Actions, "applyPreset", "E5-F6")
    eq(#a.presets + #b.presets, 2, "both photos")
    eq(#env.writeAccess, 2, "one transaction per command")
    local missing, reason = command(Actions, "applyPreset", "FF-00")
    falsy(missing, "unknown preset")
    contains(reason, "not found", "reason")
end)

test("copy and paste settings", function()
    local env, Actions = fresh()
    truthy((command(Actions, "copySettings")), "copy")
    eq(env.photo.copied, 1, "copied")
    env.module = "library"
    local a, b = Stubs.makePhoto(7), Stubs.makePhoto(8)
    env.photos = { a, b }
    truthy((command(Actions, "pasteSettings")), "paste")
    eq(a.pasted + b.pasted, 2, "pasted to every target")
end)

test("missing SDK functions are rejected, not raised", function()
    local env, Actions = fresh()
    env.develop.setAutoTone = nil
    local ok, reason = command(Actions, "autoTone")
    falsy(ok, "rejected")
    contains(reason, "not available", "reason")
end)

-- Queries ----------------------------------------------------------------------

test("context query reports module and Develop tool", function()
    local env, Actions = fresh()
    env.tool = "masking"
    local ok, _, _, result = run(Actions, request({ kind = "query", name = "context" }))
    truthy(ok, "ok")
    eq(result.module, "develop", "module")
    eq(result.tool, "masking", "tool")
    env.module = "library"
    _, _, _, result = run(Actions, request({ kind = "query", name = "context" }))
    eq(result.tool, "", "no tool outside Develop")
end)

test("presets query lists every develop preset with its folder", function()
    local _, Actions = fresh()
    local _, _, _, result = run(Actions, request({ kind = "query", name = "presets" }))
    eq(result.name, "presets", "name")
    eq(#result.items, 3, "items")
    eq(result.items[1].id, "A1-B2", "uuid")
    eq(result.items[1].group, "Favourites", "group")
    eq(result.items[3].name, "Selenium", "name")
end)

test("catalog query lists parameters this Lightroom does not support", function()
    local env, Actions = fresh()
    env.develop.rangeErrors = true
    local _, _, _, result = run(Actions, request({ kind = "query", name = "catalog" }))
    local unsupported = {}
    for _, id in ipairs(result.unsupported) do unsupported[id] = true end
    truthy(unsupported.Sharpness, "missing range listed")
    falsy(unsupported.Exposure, "known parameter not listed")
    falsy(unsupported["lumerist.cropZoom"], "special handled by the plug-in")
    truthy(#result.skipped > 0, "mask parameters skipped without a mask")
    env.tool, env.selectedMask = "masking", "mask-1"
    _, _, _, result = run(Actions, request({ kind = "query", name = "catalog" }))
    eq(#result.skipped, 0, "checked with a mask")
    unsupported = {}
    for _, id in ipairs(result.unsupported) do unsupported[id] = true end
    truthy(unsupported.local_Dehaze, "unsupported mask parameter listed")
    env.module = "library"
    local ok, reason = run(Actions, request({ kind = "query", name = "catalog" }))
    falsy(ok, "outside Develop")
    contains(reason, "Develop", "reason")
end)

-- Bridge: parsing and encoding --------------------------------------------------------

test("parseRequest decodes a Swift-encoded line", function()
    local _, _, Bridge = fresh()
    local value = Bridge.parseRequest('{"delta":0.05,"id":7,"kind":"adjust","parameter":"Exposure","scope":"develop","token":"' .. TEST_TOKEN .. '","version":4}')
    eq(value.id, 7, "id")
    eq(value.parameter, "Exposure", "parameter")
    near(value.delta, 0.05, "delta")
end)

test("parseRequest rejects malformed and non-object payloads", function()
    local _, _, Bridge = fresh()
    for _, text in ipairs({ "nope", "[1,2]", '"text"', '{"a":', 42 }) do
        local value, reason = Bridge.parseRequest(text)
        eq(value, nil, "parsed " .. tostring(text))
        truthy(reason, "reason")
    end
    local long, reason = Bridge.parseRequest(string.rep(" ", 70000) .. "{}")
    eq(long, nil, "too long")
    contains(reason, "too long", "reason")
end)

test("encodeAck carries id, values, range and only known units", function()
    local _, _, Bridge = fresh()
    local line = Bridge.encodeAck(12, true, nil, { parameter = "Exposure", scope = "develop", value = 0.35,
        minimum = -5, maximum = 5, unit = "EV" })
    eq(line:sub(-1), "\n", "newline")
    local ack = decodeJSON(line:sub(1, -2))
    eq(ack.version, 4, "version")
    eq(ack.kind, "ack", "kind")
    eq(ack.id, 12, "id")
    eq(ack.accepted, true, "accepted")
    eq(ack.message, "accepted", "default message")
    near(ack.value, 0.35, "value")
    eq(ack.unit, "EV", "unit")
    local odd = decodeJSON(Bridge.encodeAck(nil, false, "x", { unit = "parsecs", value = 0 / 0 }):sub(1, -2))
    eq(odd.id, nil, "no id")
    eq(odd.unit, nil, "unknown unit dropped")
    eq(odd.value, nil, "NaN dropped")
end)

test("encodeAck truncates long messages without splitting UTF-8", function()
    local _, _, Bridge = fresh()
    local line = Bridge.encodeAck(1, false, string.rep("a", 1023) .. "è" .. string.rep("b", 10))
    truthy(isValidUTF8(line), "valid UTF-8")
    local ack = decodeJSON(line:sub(1, -2))
    eq(#ack.message, 1023, "partial character dropped")
end)

test("encodeContext and encodeResult produce valid lines", function()
    local _, _, Bridge = fresh()
    local event = decodeJSON(Bridge.encodeContext("develop", "masking"):sub(1, -2))
    eq(event.kind, "event", "kind")
    eq(event.name, "context", "name")
    eq(event.tool, "masking", "tool")
    local Json = require "Json"
    local result = decodeJSON(Bridge.encodeResult(9, { name = "catalog", unsupported = Json.array({ "A", "B" }),
        skipped = Json.array({}) }):sub(1, -2))
    eq(result.kind, "result", "kind")
    eq(result.id, 9, "id")
    eq(result.unsupported[2], "B", "array")
    eq(#result.skipped, 0, "empty array")
end)

-- Bridge: queue ---------------------------------------------------------------------

test("coalesce merges consecutive adjustments of one scope and parameter", function()
    local _, _, Bridge = fresh()
    local batches = Bridge.coalesce({
        { kind = "adjust", scope = "develop", parameter = "Exposure", delta = 0.05, id = 1 },
        { kind = "adjust", scope = "develop", parameter = "Exposure", delta = 0.05, id = 2 },
        { kind = "adjust", scope = "library", parameter = "Exposure", delta = 0.05, id = 3 },
        { kind = "ping", id = 4 },
        { kind = "ping", id = 5 },
        { kind = "adjust", scope = "develop", parameter = "Contrast", delta = 2, id = 6 },
        { kind = "adjust", scope = "develop", parameter = "Exposure", delta = 0.05, id = 7 },
    })
    eq(#batches, 6, "batches")
    near(batches[1].delta, 0.1, "merged delta")
    eq(batches[1].id, 2, "last id")
    eq(batches[1].count, 2, "count")
    eq(batches[2].scope, "library", "library kept apart")
    eq(batches[3].id, 4, "pings not merged")
    eq(batches[6].parameter, "Exposure", "order kept")
end)

test("enqueue answers invalid and malformed requests with a rejected ack", function()
    local env, _, Bridge = fresh()
    _G.lumeristQueue = {}
    _G.lumeristResponseSocket = env.responseSocket
    Bridge.enqueue('{"id":5,"kind":"adjust","parameter":"Exposure","scope":"develop","delta":9,"token":"' .. TEST_TOKEN .. '","version":4}')
    local ack = sent(env)
    eq(ack.id, 5, "id echoed")
    eq(ack.accepted, false, "rejected")
    Bridge.enqueue("garbage")
    ack = sent(env)
    eq(ack.id, nil, "no id")
    eq(ack.accepted, false, "rejected")
    Bridge.enqueue('{"id":6,"kind":"ping","version":2}')
    contains(sent(env).message, "protocol 4 required", "version message")
    eq(#_G.lumeristQueue, 0, "nothing queued")
end)

test("enqueue queues valid requests and reports a full queue", function()
    local env, _, Bridge = fresh()
    _G.lumeristQueue = {}
    _G.lumeristResponseSocket = env.responseSocket
    Bridge.enqueue('{"id":1,"kind":"ping","token":"' .. TEST_TOKEN .. '","version":4}')
    eq(#_G.lumeristQueue, 1, "queued")
    for index = 2, 256 do _G.lumeristQueue[index] = { kind = "ping", id = index } end
    Bridge.enqueue('{"id":300,"kind":"ping","token":"' .. TEST_TOKEN .. '","version":4}')
    local ack = sent(env)
    eq(ack.id, 300, "id")
    contains(ack.message, "busy", "busy")
end)

-- Bridge: the token (protocol 4) ---------------------------------------------------

local function line(fields)
    local object = { version = 4, token = TEST_TOKEN }
    for key, value in pairs(fields) do object[key] = value end
    return (require "Json").encode(object)
end

test("enqueue refuses a request without this computer's token and drops the sender", function()
    local env, _, Bridge = fresh()
    _G.lumeristQueue = {}
    _G.lumeristResponseSocket = env.responseSocket
    eq(Bridge.enqueue('{"id":1,"kind":"command","name":"resetAll","version":4}'), true, "no token: drop")
    local ack = sent(env)
    eq(ack.accepted, false, "rejected")
    eq(ack.id, nil, "no id, so the app never takes it for one of its own answers")
    contains(ack.message, "not authorized", "message")
    eq(Bridge.enqueue(line({ id = 2, kind = "command", name = "resetAll", token = string.rep("f", 64) })), true, "wrong token")
    eq(Bridge.enqueue(line({ id = 3, kind = "command", name = "resetAll", token = 5 })), true, "a number")
    eq(Bridge.enqueue(line({ id = 4, kind = "command", name = "resetAll", token = TEST_TOKEN:upper() })), true, "the app sends lower case")
    eq(#_G.lumeristQueue, 0, "nothing queued")
    eq(lastLog(env), "Rejected request: missing or wrong bridge token", "the token is never logged")
    eq(Bridge.enqueue(line({ id = 5, kind = "command", name = "resetAll" })), false, "the app's token")
    eq(#_G.lumeristQueue, 1, "queued")
end)

test("a web page's HTTP request and an older protocol are refused at their first line", function()
    local env, _, Bridge = fresh()
    _G.lumeristQueue = {}
    _G.lumeristResponseSocket = env.responseSocket
    eq(Bridge.enqueue("POST / HTTP/1.1\r"), true, "HTTP request line: drop")
    eq(sent(env).accepted, false, "rejected")
    eq(Bridge.enqueue('{"id":7,"kind":"ping","version":3}'), true, "protocol 3: drop")
    local ack = sent(env)
    eq(ack.id, 7, "a well-formed request keeps its id")
    contains(ack.message, "protocol 4 required (got 3)", "says which protocol")
    eq(#_G.lumeristQueue, 0, "nothing queued")
end)

test("an invalid request from the app is refused without dropping the app", function()
    local env, _, Bridge = fresh()
    _G.lumeristQueue = {}
    _G.lumeristResponseSocket = env.responseSocket
    eq(Bridge.enqueue(line({ id = 8, kind = "adjust", scope = "develop", parameter = "Exposure", delta = 9 })), false, "kept")
    eq(sent(env).id, 8, "rejected with its id")
end)

test("the token comes from the user's own folder and must be 64 hexadecimal characters", function()
    local env = fresh()
    local BridgeToken = require "BridgeToken"
    eq(BridgeToken.paths()[1], MAC_TOKEN_FILE, "macOS: ~/Library/Application Support/Lumerist")
    eq(BridgeToken.parse("  " .. TEST_TOKEN:upper() .. "\r\n"), TEST_TOKEN, "whitespace and case")
    eq(BridgeToken.parse(TEST_TOKEN:sub(2)), nil, "63 characters")
    eq(BridgeToken.parse(TEST_TOKEN .. "0"), nil, "65 characters")
    eq(BridgeToken.parse(TEST_TOKEN:sub(2) .. "g"), nil, "not hexadecimal")
    eq(BridgeToken.parse(TEST_TOKEN .. " " .. TEST_TOKEN), nil, "two tokens")
    eq(BridgeToken.parse(nil), nil, "no file")
    env.files[MAC_TOKEN_FILE] = "garbage"
    eq(BridgeToken.refresh(env.clock), nil, "an unreadable file gives no token")
    falsy(BridgeToken.accepts(TEST_TOKEN), "so nothing is accepted")
end)

test("a token the app writes after Lightroom started is read again, at most once a second", function()
    local env, _, Bridge = fresh()
    local BridgeToken = require "BridgeToken"
    _G.lumeristQueue = {}
    _G.lumeristResponseSocket = env.responseSocket
    env.files[MAC_TOKEN_FILE] = nil
    BridgeToken.refresh(env.clock)
    eq(Bridge.enqueue(line({ id = 1, kind = "ping" })), true, "no file yet: refused")
    env.files[MAC_TOKEN_FILE] = TEST_TOKEN -- the app starts and writes it
    BridgeToken.refreshIfNeeded(env.clock + 0.5)
    eq(Bridge.enqueue(line({ id = 2, kind = "ping" })), true, "not read again within a second")
    BridgeToken.refreshIfNeeded(env.clock + 1)
    eq(Bridge.enqueue(line({ id = 3, kind = "ping" })), false, "read again")
    -- The app made a new token: the old one is refused, the new one read.
    local newToken = string.rep("a", 64)
    env.files[MAC_TOKEN_FILE] = newToken
    eq(Bridge.enqueue(line({ id = 4, kind = "ping", token = newToken })), true, "unknown until read")
    BridgeToken.refreshIfNeeded(env.clock + 2)
    eq(Bridge.enqueue(line({ id = 5, kind = "ping", token = newToken })), false, "the new token")
    eq(Bridge.enqueue(line({ id = 6, kind = "ping" })), true, "the old one no longer works")
end)

test("peer text is shortened and stripped of control characters before it is logged", function()
    local env, _, Bridge = fresh()
    _G.lumeristQueue = {}
    _G.lumeristResponseSocket = env.responseSocket
    local parameter = "Exposure\nWARN forged line" .. string.rep("x", 1000)
    Bridge.enqueue(line({ id = 1, kind = "adjust", scope = "develop", parameter = parameter, delta = 1 }))
    local logged = lastLog(env)
    falsy(logged:find("\n", 1, true), "no newline")
    truthy(#logged < 260, "shortened: " .. #logged .. " bytes")
    contains(logged, "Exposure?WARN forged line", "control characters replaced")
    eq(Bridge.loggable(nil), "nil", "anything prints")
end)

test("the socket loop disconnects a sender that is not the app and ignores the rest of its lines", function()
    local env, _, Bridge = fresh()
    local receiver, reconnects = nil, 0
    env.bindSocket = function(params)
        local socket = {}
        function socket:reconnect() reconnects = reconnects + 1 end
        function socket:close() end
        function socket:send(payload) env.responses[#env.responses + 1] = payload end
        if params.mode == "receive" then receiver = params end
        return socket
    end
    Bridge.start()
    local step = 0
    env.onSleep = function()
        step = step + 1
        if step == 1 then
            -- A web page: its request line, then a body that would be valid.
            receiver.onConnected()
            receiver.onMessage(nil, "POST / HTTP/1.1\r")
            receiver.onMessage(nil, line({ id = 1, kind = "command", name = "resetAll" }))
        elseif step == 3 then
            -- After the reconnect: the app.
            receiver.onConnected()
            receiver.onMessage(nil, line({ id = 2, kind = "ping" }))
        elseif step >= 4 then
            _G.lumeristStopping = true
        end
    end
    env.tasks[2]() -- the socket loop
    eq(reconnects, 1, "the request socket reconnected once")
    eq(#_G.lumeristQueue, 1, "only the app's request was queued")
    eq(_G.lumeristQueue[1].id, 2, "the app's ping")
end)

-- Bridge: worker ---------------------------------------------------------------------

local function startAndStopAfterFirstIdle(env, Bridge)
    Bridge.start()
    _G.lumeristResponseSocket = env.responseSocket
    env.onSleep = function() _G.lumeristStopping = true end
end

test("the worker merges scroll steps and answers with the last id", function()
    local env, _, Bridge = fresh()
    startAndStopAfterFirstIdle(env, Bridge)
    for id = 1, 3 do
        Bridge.enqueue('{"delta":0.05,"id":' .. id .. ',"kind":"adjust","parameter":"Exposure","scope":"develop","token":"' .. TEST_TOKEN .. '","version":4}')
    end
    Bridge.enqueue('{"delta":2,"id":4,"kind":"adjust","parameter":"Contrast","scope":"develop","token":"' .. TEST_TOKEN .. '","version":4}')
    Bridge.enqueue('{"id":5,"kind":"ping","token":"' .. TEST_TOKEN .. '","version":4}')
    env.tasks[1]()
    eq(#env.develop.sets, 2, "setValue calls")
    near(env.develop.sets[1].value, 0.15, "merged exposure")
    local acks = {}
    for index = 1, #env.responses do
        local message = sent(env, index)
        if message.kind == "ack" then acks[#acks + 1] = message end
    end
    eq(#acks, 3, "acknowledgements")
    eq(acks[1].id, 3, "merged ack carries the last id")
    eq(acks[1].parameter, "Exposure", "parameter")
    near(acks[1].value, 0.15, "value")
    eq(acks[2].id, 4, "contrast ack")
    contains(acks[3].message, "protocol 4", "ping message")
end)

test("the worker reads the token file", function()
    local env, _, Bridge = fresh()
    local BridgeToken = require "BridgeToken"
    BridgeToken._resetForTests()
    startAndStopAfterFirstIdle(env, Bridge)
    env.tasks[1]()
    truthy(BridgeToken.accepts(TEST_TOKEN), "read on the worker's first pass")
end)

test("the worker answers queries with a result instead of an ack", function()
    local env, _, Bridge = fresh()
    startAndStopAfterFirstIdle(env, Bridge)
    Bridge.enqueue('{"id":11,"kind":"query","name":"presets","token":"' .. TEST_TOKEN .. '","version":4}')
    env.tasks[1]()
    local result
    for index = 1, #env.responses do
        local message = sent(env, index)
        if message.kind == "result" then result = message end
    end
    truthy(result, "result sent")
    eq(result.id, 11, "id")
    eq(#result.items, 3, "items")
    eq(result.items[1].name, 'Warm "Film"', "escaped name round-trips")
end)

test("the worker pushes the context right after a client connects", function()
    local env, _, Bridge = fresh()
    startAndStopAfterFirstIdle(env, Bridge)
    env.tool = "masking"
    env.tasks[1]()
    local event = sent(env, 1)
    eq(event.kind, "event", "first line is the context")
    eq(event.module, "develop", "module")
    eq(event.tool, "masking", "tool")
end)

test("the worker pushes the context again within a quarter second of a change", function()
    local env, _, Bridge = fresh()
    Bridge.start()
    local stamps = {}
    _G.lumeristResponseSocket = {
        send = function(_, payload)
            env.responses[#env.responses + 1] = payload
            stamps[#env.responses] = env.clock
        end,
    }
    local changedAt
    local sleeps = 0
    env.onSleep = function(seconds)
        sleeps = sleeps + 1
        env.clock = env.clock + seconds
        if sleeps == 3 then
            env.tool = "masking" -- the user opens Masking during this sleep
            changedAt = env.clock - seconds
        end
        if env.clock > 1001 then _G.lumeristStopping = true end
    end
    env.tasks[1]()
    local events = {}
    for index = 1, #env.responses do
        local message = sent(env, index)
        if message.kind == "event" then events[#events + 1] = { message = message, at = stamps[index] } end
    end
    eq(#events, 2, "initial context and the change")
    eq(events[2].message.tool, "masking", "new tool")
    truthy(events[2].at - changedAt <= 0.25 + 1e-9, "pushed within 0.25 s (took " .. (events[2].at - changedAt) .. ")")
end)

test("the worker does not repeat an unchanged context", function()
    local env, _, Bridge = fresh()
    Bridge.start()
    _G.lumeristResponseSocket = env.responseSocket
    env.onSleep = function(seconds)
        env.clock = env.clock + seconds
        if env.clock > 1002 then _G.lumeristStopping = true end
    end
    env.tasks[1]()
    local events = 0
    for index = 1, #env.responses do
        if sent(env, index).kind == "event" then events = events + 1 end
    end
    eq(events, 1, "one event")
end)

test("the worker survives an SDK error and keeps answering", function()
    local env, _, Bridge = fresh()
    startAndStopAfterFirstIdle(env, Bridge)
    env.moduleError = "catalog is switching"
    Bridge.enqueue('{"delta":0.05,"id":1,"kind":"adjust","parameter":"Exposure","scope":"develop","token":"' .. TEST_TOKEN .. '","version":4}')
    Bridge.enqueue('{"id":2,"kind":"ping","token":"' .. TEST_TOKEN .. '","version":4}')
    env.tasks[1]()
    local acks = {}
    for index = 1, #env.responses do
        local message = sent(env, index)
        if message.kind == "ack" then acks[#acks + 1] = message end
    end
    eq(#acks, 2, "acknowledgements")
    eq(acks[1].accepted, false, "exposure rejected")
    contains(acks[1].message, "catalog is switching", "message")
    eq(acks[2].accepted, true, "ping accepted")
end)

test("the worker polls quickly only after recent activity", function()
    local env, _, Bridge = fresh()
    Bridge.start()
    local sleeps = 0
    env.onSleep = function()
        sleeps = sleeps + 1
        if sleeps == 1 then
            env.clock = env.clock + 10
        else
            _G.lumeristStopping = true
        end
    end
    _G.lumeristLastActivity = env.clock
    env.tasks[1]()
    near(env.sleeps[1], 0.02, "active poll")
    near(env.sleeps[2], 0.1, "idle poll")
end)

-- Bridge: lifecycle ------------------------------------------------------------------

test("start launches one worker and one socket loop", function()
    local env, _, Bridge = fresh()
    local generation = Bridge.start()
    eq(#env.tasks, 2, "async tasks started")
    eq(env.prefs.bridgeGeneration, generation, "generation claimed in shared prefs")
    contains(lastLog(env), "protocol 4", "status")
end)

test("a second start supersedes the first instead of adding a bridge", function()
    local env, _, Bridge = fresh()
    local first = Bridge.start()
    local second = Bridge.start()
    eq(second, first + 1, "new generation")
    eq(#env.tasks, 4, "tasks of both starts")
    env.tasks[1]()
    env.tasks[2]()
    eq(#env.sleeps, 0, "superseded loops did not poll")
end)

test("a generation claimed in another script environment stops the worker", function()
    local env, _, Bridge = fresh()
    local generation = Bridge.start()
    _G.lumeristResponseSocket = env.responseSocket
    Bridge.enqueue('{"id":1,"kind":"ping","token":"' .. TEST_TOKEN .. '","version":4}')
    env.prefs.bridgeGeneration = generation + 1
    env.tasks[1]()
    eq(#env.responses, 0, "no work done by a superseded worker")
    eq(#env.sleeps, 0, "the superseded worker stopped at once")
end)

test("a stuck socket loop is replaced by a fresh bridge", function()
    local env, _, Bridge = fresh()
    local generation = Bridge.start()
    env.clock = env.clock + Bridge.SOCKET_STALL_SECONDS + 1 -- the socket loop never ticked again
    env.tasks[1]() -- the worker notices
    eq(env.prefs.bridgeGeneration, generation + 1, "a new generation took over")
    eq(#env.tasks, 4, "a new worker and socket loop were started")
    contains(lastLog(env), "bridge starting", "status after the restart")
end)

test("a ticking socket loop is left alone", function()
    local env, _, Bridge = fresh()
    local generation = Bridge.start()
    local ticks = 0
    env.onSleep = function()
        ticks = ticks + 1
        env.clock = env.clock + 1
        _G.lumeristSocketHeartbeat = env.clock -- the socket loop is alive
        if ticks > 30 then env.prefs.bridgeGeneration = generation + 5 end -- end the test loop
    end
    env.tasks[1]()
    eq(#env.tasks, 2, "no extra bridge started")
end)

test("the bridge still starts when plug-in prefs are unavailable", function()
    local env, _, Bridge = fresh()
    env.prefsError = "prefs not available here"
    eq(Bridge.start(), 1, "generation kept locally")
    contains(lastLog(env), "bridge starting", "status")
end)

test("stop clears sockets and the queue and claims a generation", function()
    local env, _, Bridge = fresh()
    local closed = 0
    local socket = { close = function() closed = closed + 1 end }
    _G.lumeristRequestSocket = socket
    _G.lumeristResponseSocket = socket
    _G.lumeristQueue = { { kind = "ping" } }
    Bridge.stop()
    eq(closed, 2, "sockets closed")
    eq(_G.lumeristStopping, true, "stopping flag")
    eq(_G.lumeristRequestSocket, nil, "request socket")
    eq(#_G.lumeristQueue, 0, "queue")
    eq(lastLog(env), "bridge stopped", "status")
    eq(env.prefs.bridgeGeneration, 1, "generation claimed")
end)

test("Open Lumerist opens the app by bundle id, in a task", function()
    local env = fresh()
    dofile(pluginDir .. "/OpenAppMenu.lua")
    eq(#env.executed, 0, "nothing runs outside a task")
    eq(#env.tasks, 1, "one task started")
    env.tasks[1]()
    eq(#env.executed, 1, "one command")
    eq(env.executed[1], "open -b io.github.ziago.lumerist --args --show-window", "command")
    eq(#env.dialogs, 0, "no dialog when the app opens")
end)

test("Open Lumerist falls back to the app name, then explains where to get it", function()
    local env = fresh()
    local OpenApp = require "OpenApp"
    env.executeStatus = function(command) return command:find("^open %-a") and 0 or 256 end
    truthy(OpenApp.run(), "opened by name")
    eq(#env.executed, 2, "tried bundle id, then name")
    eq(#env.dialogs, 0, "no dialog")

    env.executed = {}
    env.executeStatus = function() return 256 end
    falsy(OpenApp.run(), "not found")
    eq(#env.dialogs, 1, "one dialog")
    contains(env.dialogs[1].message, "github.com/ziago/Lumerist/releases", "download link")
end)

test("starting with Lightroom opens Lumerist in the background only when needed", function()
    local env = fresh()
    local OpenApp = require "OpenApp"
    OpenApp.launchWithLightroom()
    eq(#env.executed, 0, "nothing runs outside a task")
    env.tasks[1]()
    local command = env.executed[1]
    contains(command, "org.lumerist.open-with-lightroom", "respects the app setting")
    contains(command, "pgrep -xq LumeristApp", "skips a running app (no window pops up)")
    contains(command, "open -g -b io.github.ziago.lumerist --args --with-lightroom", "background launch")
    falsy(command:find("show%-window"), "never asks for the window")
end)

-- Windows: the app writes %APPDATA%\Lumerist\plugin-link.json; the plug-in
-- reads it to find Lumerist.exe (OpenApp.lua).

local WINDOWS_EXE = "C:\\Users\\Test\\AppData\\Local\\Programs\\Lumerist\\Lumerist.exe"
local WINDOWS_LINK = "C:\\Users\\Test\\AppData\\Roaming\\Lumerist\\plugin-link.json"

local function onWindows(linkText, standardPaths)
    local env = fresh()
    _G.WIN_ENV, _G.MAC_ENV = true, nil
    env.standardPaths = standardPaths or { appData = "C:\\Users\\Test\\AppData\\Roaming" }
    env.pathSeparator = "\\"
    env.files[WINDOWS_LINK] = linkText
    env.files[WINDOWS_EXE] = "" -- installed
    return env
end

local function linkJSON(exe, openWithLightroom)
    local text = '{"exe":"' .. exe:gsub("\\", "\\\\"):gsub('"', '\\"') .. '"'
    if openWithLightroom ~= nil then text = text .. ',"openWithLightroom":' .. tostring(openWithLightroom) end
    return text .. "}"
end

test("Windows: Open Lumerist starts the exe named in plugin-link.json", function()
    local env = onWindows(linkJSON(WINDOWS_EXE, true))
    dofile(pluginDir .. "/OpenAppMenu.lua")
    eq(#env.executed, 0, "nothing runs outside a task")
    env.tasks[1]()
    eq(#env.executed, 1, "one command")
    eq(env.executed[1], '"start "" "' .. WINDOWS_EXE .. '" --show-window"', "command")
    eq(#env.dialogs, 0, "no dialog")
end)

test("Windows: without the link file Open Lumerist explains where to get the app", function()
    local env = onWindows(nil)
    local OpenApp = require "OpenApp"
    falsy(OpenApp.run(), "not found")
    eq(#env.executed, 0, "nothing executed")
    eq(#env.dialogs, 1, "one dialog")
    contains(env.dialogs[1].message, "on this PC", "speaks of a PC")
    contains(env.dialogs[1].message, "github.com/ziago/Lumerist/releases", "download link")
end)

test("Windows: starting with Lightroom follows the app's setting", function()
    local env = onWindows(linkJSON(WINDOWS_EXE))
    local OpenApp = require "OpenApp"
    OpenApp.launchWithLightroom()
    eq(#env.executed, 0, "nothing runs outside a task")
    env.tasks[1]()
    eq(env.executed[1], '"start "" "' .. WINDOWS_EXE .. '" --with-lightroom"', "on by default")
    falsy(env.executed[1]:find("show%-window"), "never asks for the window")

    env = onWindows(linkJSON(WINDOWS_EXE, false))
    OpenApp = require "OpenApp"
    OpenApp.launchWithLightroom()
    env.tasks[1]()
    eq(#env.executed, 0, "off in the app's settings")
end)

test("Windows: after the app is uninstalled, Lightroom starts quietly", function()
    -- The uninstaller keeps %APPDATA%\Lumerist, so the link file outlives the exe.
    local env = onWindows(linkJSON(WINDOWS_EXE, true))
    env.files[WINDOWS_EXE] = nil
    local OpenApp = require "OpenApp"
    falsy(OpenApp.readWindowsLink(), "no app to start")
    OpenApp.launchWithLightroom()
    env.tasks[1]()
    eq(#env.executed, 0, "nothing started")
    eq(#env.dialogs, 0, "no dialog at Lightroom's start")

    -- Asked for from the menu, it says where to get the app (and Windows shows no error of its own).
    falsy(OpenApp.run(), "not opened")
    eq(#env.executed, 0, "still nothing started")
    eq(#env.dialogs, 1, "one dialog")
    contains(env.dialogs[1].message, "github.com/ziago/Lumerist/releases", "download link")
end)

test("Windows: the link file is found when Lightroom's appData is its own folder", function()
    -- Lightroom may report …\Roaming\Adobe\Lightroom; the app writes to …\Roaming.
    local env = onWindows(linkJSON(WINDOWS_EXE, true),
        { appData = "C:\\Users\\Test\\AppData\\Roaming\\Adobe\\Lightroom" })
    local OpenApp = require "OpenApp"
    eq(OpenApp.readWindowsLink().exe, WINDOWS_EXE, "climbed out of Adobe\\Lightroom")

    env = onWindows(linkJSON(WINDOWS_EXE, true),
        { appData = "D:\\Elsewhere", home = "C:\\Users\\Test" })
    OpenApp = require "OpenApp"
    eq(OpenApp.readWindowsLink().exe, WINDOWS_EXE, "home\\AppData\\Roaming")
    truthy(OpenApp.run(), "opened")
    eq(#env.dialogs, 0, "no dialog")
end)

test("Windows: %APPDATA% is tried first, and other folders are never climbed out of", function()
    local realGetenv = os.getenv
    os.getenv = function(name)
        if name == "APPDATA" then return "C:\\Users\\Test\\AppData\\Roaming" end
        return realGetenv(name)
    end
    local ok, failure = pcall(function()
        onWindows(linkJSON(WINDOWS_EXE, true), { appData = "D:\\Elsewhere" })
        local OpenApp = require "OpenApp"
        eq(OpenApp.windowsLinkFolders()[1], "C:\\Users\\Test\\AppData\\Roaming\\Lumerist", "from the environment")
        eq(OpenApp.readWindowsLink().exe, WINDOWS_EXE, "found")
    end)
    os.getenv = realGetenv
    if not ok then error(failure, 0) end

    -- Only an appData that ends in Adobe\Lightroom is climbed out of.
    onWindows(nil, { appData = "C:\\Users\\Test\\AppData\\Roaming" })
    local OpenApp = require "OpenApp"
    for _, folder in ipairs(OpenApp.windowsLinkFolders()) do
        falsy(folder == "C:\\Users\\Lumerist", "never C:\\Users")
    end
end)

test("Windows: the token sits next to plugin-link.json", function()
    local env = onWindows(nil)
    env.files["C:\\Users\\Test\\AppData\\Roaming\\Lumerist\\bridge-token"] = TEST_TOKEN
    local BridgeToken = require "BridgeToken"
    eq(BridgeToken.refresh(env.clock), TEST_TOKEN, "read from %APPDATA%\\Lumerist")
end)

test("Windows: a link file that is unreadable or names another program is ignored", function()
    for _, text in ipairs({
        "not json",
        '{"openWithLightroom":true}',
        linkJSON("Lumerist.exe"),
        linkJSON("C:\\Tools\\other.exe"),
        linkJSON('C:\\Lumerist" & calc & ".exe\\Lumerist.exe'),
        linkJSON("C:\\%APPDATA%\\Lumerist.exe"),
        linkJSON("C:\\Lumerist^\\Lumerist.exe"),
    }) do
        local env = onWindows(text)
        local OpenApp = require "OpenApp"
        falsy(OpenApp.readWindowsLink(), "rejected: " .. text)
        OpenApp.launchWithLightroom()
        env.tasks[1]()
        eq(#env.executed, 0, "nothing started for: " .. text)
    end
end)

test("macOS is unaffected by the Windows link file", function()
    local env = fresh()
    env.files["/Users/test/Library/Application Support/Lumerist/plugin-link.json"] = linkJSON(WINDOWS_EXE, true)
    local OpenApp = require "OpenApp"
    truthy(OpenApp.run(), "opened")
    eq(env.executed[1], "open -b io.github.ziago.lumerist --args --show-window", "the Mac command")
end)

test("Info.lua lists only the Open Lumerist menu item", function()
    local info = dofile(pluginDir .. "/Info.lua")
    eq(#info.LrExportMenuItems, 1, "menu items")
    eq(info.LrExportMenuItems[1].title, "Open Lumerist", "title")
    local handle = io.open(pluginDir .. "/" .. info.LrExportMenuItems[1].file, "r")
    truthy(handle, "menu script exists")
    if handle then handle:close() end
end)

test("the generated catalog is up to date", function()
    local ok = os.execute("python3 '" .. repoRoot .. "/Scripts/generate-parameters.py' --check >/dev/null")
    truthy(ok == 0 or ok == true, "python3 Scripts/generate-parameters.py --check")
end)

-- Runner ----------------------------------------------------------------------

local passed, failed = 0, 0
for _, entry in ipairs(tests) do
    local ok, message = pcall(entry.fn)
    if ok then
        passed = passed + 1
        print("✔ " .. entry.name)
    else
        failed = failed + 1
        print("✘ " .. entry.name .. "\n    " .. tostring(message))
    end
end
print(string.format("\n%d Lua tests, %d passed, %d failed", passed + failed, passed, failed))
os.exit(failed == 0 and 0 or 1)
