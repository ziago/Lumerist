-- Minimal, strict JSON for the bridge protocol (protocol 3).
-- decode: objects, arrays, strings (with \u escapes), numbers, booleans, null.
-- encode: objects with sorted keys, arrays marked with Json.array, finite
-- numbers only (non-finite values are left out), strings escaped so the line
-- stays valid UTF-8 JSON for Swift's JSONDecoder.

local Json = {}

local ARRAY = { __jsonArray = true }
local MAXIMUM_DEPTH = 32

--- Marks a Lua sequence as a JSON array (needed for empty arrays).
function Json.array(list)
    return setmetatable(list or {}, ARRAY)
end

function Json.isArray(value)
    return type(value) == "table" and getmetatable(value) == ARRAY
end

local function isFinite(value)
    return type(value) == "number" and value == value and value ~= math.huge and value ~= -math.huge
end
Json.isFinite = isFinite

-- Decoding ---------------------------------------------------------------------

local function utf8FromCodePoint(code)
    if code < 0x80 then
        return string.char(code)
    elseif code < 0x800 then
        return string.char(0xC0 + math.floor(code / 0x40), 0x80 + code % 0x40)
    elseif code < 0x10000 then
        return string.char(0xE0 + math.floor(code / 0x1000), 0x80 + math.floor(code / 0x40) % 0x40, 0x80 + code % 0x40)
    end
    return string.char(0xF0 + math.floor(code / 0x40000), 0x80 + math.floor(code / 0x1000) % 0x40,
        0x80 + math.floor(code / 0x40) % 0x40, 0x80 + code % 0x40)
end

function Json.decode(text)
    if type(text) ~= "string" then
        return nil, "payload must be text"
    end
    local position = 1
    local length = #text

    local function fail(message)
        error({ json = message .. " at " .. position }, 0)
    end

    local function skip()
        position = text:find("[^ \t\r\n]", position) or (length + 1)
    end

    local parseValue

    local function parseString()
        position = position + 1 -- opening quote
        local parts = {}
        while true do
            local char = text:sub(position, position)
            if char == "" then fail("unterminated string") end
            if char == '"' then
                position = position + 1
                break
            end
            if char:byte() < 0x20 then fail("control character in string") end
            if char == "\\" then
                local escape = text:sub(position + 1, position + 1)
                local simple = { ['"'] = '"', ["\\"] = "\\", ["/"] = "/", b = "\b", f = "\f", n = "\n", r = "\r", t = "\t" }
                if simple[escape] then
                    parts[#parts + 1] = simple[escape]
                    position = position + 2
                elseif escape == "u" then
                    local hex = text:sub(position + 2, position + 5)
                    if not hex:match("^%x%x%x%x$") then fail("bad unicode escape") end
                    local code = tonumber(hex, 16)
                    position = position + 6
                    if code >= 0xD800 and code <= 0xDBFF and text:sub(position, position + 1) == "\\u" then
                        local low = tonumber(text:sub(position + 2, position + 5), 16)
                        if low and low >= 0xDC00 and low <= 0xDFFF then
                            code = 0x10000 + (code - 0xD800) * 0x400 + (low - 0xDC00)
                            position = position + 6
                        end
                    end
                    parts[#parts + 1] = utf8FromCodePoint(code)
                else
                    fail("unsupported escape")
                end
            else
                local stop = text:find('["\\%c]', position) or (length + 1)
                parts[#parts + 1] = text:sub(position, stop - 1)
                position = stop
            end
        end
        return table.concat(parts)
    end

    function parseValue(depth)
        if depth > MAXIMUM_DEPTH then fail("nesting too deep") end
        skip()
        local char = text:sub(position, position)
        if char == "{" then
            position = position + 1
            local object = {}
            skip()
            if text:sub(position, position) == "}" then
                position = position + 1
                return object
            end
            while true do
                skip()
                if text:sub(position, position) ~= '"' then fail("expected key") end
                local key = parseString()
                skip()
                if text:sub(position, position) ~= ":" then fail("expected ':'") end
                position = position + 1
                object[key] = parseValue(depth + 1)
                skip()
                local separator = text:sub(position, position)
                position = position + 1
                if separator == "}" then return object end
                if separator ~= "," then fail("expected ',' or '}'") end
            end
        elseif char == "[" then
            position = position + 1
            local array = Json.array({})
            skip()
            if text:sub(position, position) == "]" then
                position = position + 1
                return array
            end
            while true do
                array[#array + 1] = parseValue(depth + 1)
                skip()
                local separator = text:sub(position, position)
                position = position + 1
                if separator == "]" then return array end
                if separator ~= "," then fail("expected ',' or ']'") end
            end
        elseif char == '"' then
            return parseString()
        elseif text:sub(position, position + 3) == "true" then
            position = position + 4
            return true
        elseif text:sub(position, position + 4) == "false" then
            position = position + 5
            return false
        elseif text:sub(position, position + 3) == "null" then
            position = position + 4
            return nil
        else
            local number = text:match("^%-?%d+%.?%d*[eE]?[%+%-]?%d*", position)
            local value = number and tonumber(number)
            if not value then fail("unexpected character") end
            position = position + #number
            return value
        end
    end

    local ok, result = pcall(function()
        local value = parseValue(1)
        skip()
        if position <= length then fail("trailing data") end
        return value
    end)
    if ok then
        return result
    end
    if type(result) == "table" and result.json then
        return nil, "malformed JSON: " .. result.json
    end
    return nil, "malformed JSON"
end

-- Encoding ---------------------------------------------------------------------

local ESCAPES = { ['"'] = '\\"', ["\\"] = "\\\\", ["\b"] = "\\b", ["\f"] = "\\f", ["\n"] = "\\n", ["\r"] = "\\r", ["\t"] = "\\t" }

function Json.encodeString(value)
    local text = tostring(value)
    text = text:gsub('[%z\1-\31"\\\127]', function(char)
        return ESCAPES[char] or string.format("\\u%04x", char:byte())
    end)
    return '"' .. text .. '"'
end

function Json.encodeNumber(value)
    if not isFinite(value) then
        return nil
    end
    if value == math.floor(value) and math.abs(value) < 1e15 then
        return string.format("%d", value)
    end
    -- %.6f, trailing zeros removed; a comma decimal separator (non-C numeric
    -- locale) would break the JSON, so it is normalised.
    local text = string.format("%.6f", value):gsub(",", ".")
    text = text:gsub("0+$", ""):gsub("%.$", "")
    return text
end

local function encodeValue(value, depth)
    if depth > MAXIMUM_DEPTH then
        error("JSON value nested too deeply", 0)
    end
    local kind = type(value)
    if kind == "string" then
        return Json.encodeString(value)
    elseif kind == "number" then
        return Json.encodeNumber(value)
    elseif kind == "boolean" then
        return value and "true" or "false"
    elseif kind == "nil" then
        return "null"
    elseif kind == "table" then
        if Json.isArray(value) then
            local parts = {}
            for index = 1, #value do
                parts[#parts + 1] = encodeValue(value[index], depth + 1) or "null"
            end
            return "[" .. table.concat(parts, ",") .. "]"
        end
        local keys = {}
        for key in pairs(value) do
            if type(key) == "string" then
                keys[#keys + 1] = key
            end
        end
        table.sort(keys)
        local parts = {}
        for _, key in ipairs(keys) do
            local encoded = encodeValue(value[key], depth + 1)
            if encoded then -- non-finite numbers are left out
                parts[#parts + 1] = Json.encodeString(key) .. ":" .. encoded
            end
        end
        return "{" .. table.concat(parts, ",") .. "}"
    end
    error("cannot encode " .. kind .. " as JSON", 0)
end

function Json.encode(value)
    return encodeValue(value, 1)
end

return Json
