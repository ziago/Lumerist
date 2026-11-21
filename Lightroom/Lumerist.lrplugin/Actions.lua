-- Protocol 3 request validation and dispatch (see docs/protocol-v3.md).

local LrTasks = import "LrTasks"
local Catalog = require "Catalog"
local Commands = require "Commands"
local Develop = require "Develop"
local Library = require "Library"

local Actions = {}

Actions.protocolVersion = 3

local SCOPES = { develop = true, library = true }
local KINDS = { ping = true, adjust = true, reset = true, gesture = true, command = true, query = true }

local function isFinite(value)
    return type(value) == "number" and value == value and value ~= math.huge and value ~= -math.huge
end

local function isInteger(value)
    return isFinite(value) and value == math.floor(value)
end

--- Checks a decoded request without touching Lightroom. Returns ok, reason.
function Actions.validate(request)
    if type(request) ~= "table" then
        return false, "request must be a JSON object"
    end
    if request.version ~= Actions.protocolVersion then
        return false, "protocol " .. Actions.protocolVersion .. " required (got " .. tostring(request.version) .. ")"
    end
    if not isInteger(request.id) then
        return false, "request id must be an integer"
    end
    local kind = request.kind
    if not KINDS[kind] then
        return false, "unknown request kind " .. tostring(kind)
    end

    if kind == "adjust" or kind == "reset" then
        if not SCOPES[request.scope] then
            return false, "scope must be develop or library"
        end
        local entry = Catalog.get(request.scope, request.parameter)
        if not entry then
            return false, "unknown " .. tostring(request.scope) .. " parameter " .. tostring(request.parameter)
        end
        if kind == "reset" then
            if request.scope == "library" then
                return false, "reset is only available in Develop"
            end
            return true
        end
        if not isFinite(request.delta) or math.abs(request.delta) > entry.maxDelta then
            return false, "delta for " .. entry.id .. " must be finite and between -"
                .. entry.maxDelta .. " and " .. entry.maxDelta
        end
        return true
    elseif kind == "gesture" then
        if request.phase ~= "begin" and request.phase ~= "end" then
            return false, "gesture phase must be begin or end"
        end
        if request.parameter ~= nil and not Catalog.get("develop", request.parameter) then
            return false, "unknown develop parameter " .. tostring(request.parameter)
        end
        if request.phase == "begin" and request.parameter == nil then
            return false, "gesture begin needs a parameter"
        end
        return true
    elseif kind == "command" then
        return Commands.validate(request.name, request.argument)
    elseif kind == "query" then
        if not Commands.isQuery(request.name) then
            return false, "unknown query " .. tostring(request.name)
        end
        return true
    end
    return true -- ping
end

local function pingMessage()
    return "Lumerist plug-in ready (protocol " .. Actions.protocolVersion .. ")"
end

--- Runs one validated request or merged batch.
--- Returns ok, message, details (for an ack) and result (a payload that
--- replaces the ack for queries). Lightroom errors never escape.
function Actions.run(batch)
    local called, ok, message, details, result = LrTasks.pcall(function()
        local kind = batch.kind
        if kind == "ping" then
            return true, pingMessage()
        elseif kind == "adjust" then
            local entry = Catalog.get(batch.scope, batch.parameter)
            if batch.scope == "library" then
                return Library.adjust(entry, batch.delta)
            end
            return Develop.adjust(entry, batch.delta)
        elseif kind == "reset" then
            return Develop.reset(Catalog.get("develop", batch.parameter))
        elseif kind == "gesture" then
            return Develop.gesture(batch.phase, batch.parameter and Catalog.get("develop", batch.parameter))
        elseif kind == "command" then
            return Commands.run(batch.name, batch.argument)
        elseif kind == "query" then
            local payload, reason = Commands.query(batch.name)
            if not payload then
                return false, reason
            end
            return true, nil, nil, payload
        end
        return false, "unknown request kind"
    end)
    if not called then
        return false, "Lightroom error: " .. tostring(ok)
    end
    return ok, message, details, result
end

--- Current module and Develop tool, for the context event.
function Actions.context()
    return Commands.context()
end

--- Test hook: forget remembered writes.
function Actions._resetForTests()
    Develop._resetForTests()
end

return Actions
