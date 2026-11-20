-- Allow-listed Lightroom commands and queries (protocol 3). Every handler runs
-- on the bridge's worker task inside the dispatcher's LrTasks.pcall and
-- returns ok, message[, details]; queries return a result payload instead.

local LrApplication = import "LrApplication"
local LrApplicationView = import "LrApplicationView"
local LrDevelopController = import "LrDevelopController"
local LrSelection = import "LrSelection"
local LrUndo = import "LrUndo"
local Catalog = require "Catalog"
local Develop = require "Develop"
local Json = require "Json"

local Commands = {}

local RATINGS = { ["0"] = 0, ["1"] = 1, ["2"] = 2, ["3"] = 3, ["4"] = 4, ["5"] = 5 }
local LABELS = { red = true, yellow = true, green = true, blue = true, purple = true, none = true }
local MODULES = { library = true, develop = true, map = true, book = true, slideshow = true, print = true, web = true }
local TOOLS = { loupe = true, crop = true, dust = true, redeye = true, masking = true, upright = true }
local MASK_TYPES = { brush = true, gradient = true, radialGradient = true, rangeMask = true, aiSelection = true }
local MASK_SUBTYPES = {
    subject = true, sky = true, background = true, objects = true, people = true,
    landscape = true, color = true, luminance = true, depth = true,
}
-- Results may be large (preset lists) but stay well under the 256 KB line limit.
local MAXIMUM_PRESET_BYTES = 200000

local function maskArgument(argument)
    if type(argument) ~= "string" then
        return false
    end
    local maskType, subtype = argument:match("^([%a]+)/?([%a]*)$")
    if not maskType or not MASK_TYPES[maskType] then
        return false
    end
    if subtype ~= "" and not MASK_SUBTYPES[subtype] then
        return false
    end
    return true
end

-- name -> argument validator (nil = no argument allowed)
local ARGUMENTS = {
    rating = function(argument) return RATINGS[argument] ~= nil end,
    colorLabel = function(argument) return LABELS[argument] == true end,
    switchModule = function(argument) return MODULES[argument] == true end,
    selectTool = function(argument) return TOOLS[argument] == true end,
    revealPanel = function(argument) return Catalog.get("develop", argument) ~= nil end,
    createMask = maskArgument,
    addToMask = maskArgument,
    subtractFromMask = maskArgument,
    intersectWithMask = maskArgument,
    applyPreset = function(argument)
        return type(argument) == "string" and #argument <= 64 and argument:match("^[%w%-]+$") ~= nil
    end,
}

local NAMES = {
    undo = true, redo = true, autoTone = true, autoWhiteBalance = true, resetAll = true,
    nextPhoto = true, previousPhoto = true, flagPick = true, flagReject = true, flagRemove = true,
    rating = true, colorLabel = true, switchModule = true, selectTool = true, revealPanel = true,
    zoomToggle = true, showClipping = true, createMask = true, addToMask = true,
    subtractFromMask = true, intersectWithMask = true, invertMask = true, toggleMaskOverlay = true,
    applyPreset = true, copySettings = true, pasteSettings = true,
}

local QUERIES = { context = true, presets = true, catalog = true }

--- Checks a command name and argument without touching Lightroom.
function Commands.validate(name, argument)
    if type(name) ~= "string" or not NAMES[name] then
        return false, "unknown command " .. tostring(name)
    end
    local validator = ARGUMENTS[name]
    if validator then
        if not validator(argument) then
            return false, "invalid argument for " .. name .. ": " .. tostring(argument)
        end
    elseif argument ~= nil then
        return false, name .. " takes no argument"
    end
    return true
end

function Commands.isQuery(name)
    return QUERIES[name] == true
end

local function module()
    return LrApplicationView.getCurrentModuleName()
end

local function needsDevelop(label)
    if module() ~= "develop" then
        return false, label .. " needs the Develop module"
    end
    return true
end

--- Calls namespace[functionName](...) if this Lightroom has it.
local function call(namespace, functionName, ...)
    local fn = namespace[functionName]
    if type(fn) ~= "function" then
        return false, functionName .. " is not available in this Lightroom version"
    end
    fn(...)
    return true
end

local function developCall(label, functionName, ...)
    local ready, reason = needsDevelop(label)
    if not ready then
        return false, reason
    end
    local ok, missing = call(LrDevelopController, functionName, ...)
    if not ok then
        return false, missing
    end
    return true, label
end

local function maskCall(label, functionName, argument)
    local maskType, subtype = argument:match("^([%a]+)/?([%a]*)$")
    if subtype == "" then subtype = nil end
    return developCall(label, functionName, maskType, subtype)
end

local function targetPhotos()
    local catalog = LrApplication.activeCatalog()
    if module() == "library" then
        return catalog, catalog:getTargetPhotos() or {}
    end
    local photo = catalog:getTargetPhoto()
    return catalog, photo and { photo } or {}
end

local handlers = {}

function handlers.undo()
    if type(LrUndo.canUndo) == "function" and not LrUndo.canUndo() then
        return false, "nothing to undo"
    end
    local ok, missing = call(LrUndo, "undo")
    return ok, ok and "undo" or missing
end

function handlers.redo()
    if type(LrUndo.canRedo) == "function" and not LrUndo.canRedo() then
        return false, "nothing to redo"
    end
    local ok, missing = call(LrUndo, "redo")
    return ok, ok and "redo" or missing
end

function handlers.autoTone()
    return developCall("Auto Tone", "setAutoTone")
end

function handlers.autoWhiteBalance()
    return developCall("Auto White Balance", "setAutoWhiteBalance")
end

function handlers.resetAll()
    return developCall("Reset all", "resetAllDevelopAdjustments")
end

local function selectionCall(label, functionName, ...)
    local ok, missing = call(LrSelection, functionName, ...)
    return ok, ok and label or missing
end

function handlers.nextPhoto() return selectionCall("next photo", "nextPhoto") end
function handlers.previousPhoto() return selectionCall("previous photo", "previousPhoto") end
function handlers.flagPick() return selectionCall("flagged as pick", "flagAsPick") end
function handlers.flagReject() return selectionCall("flagged as rejected", "flagAsReject") end
function handlers.flagRemove() return selectionCall("flag removed", "removeFlag") end

function handlers.rating(argument)
    local stars = RATINGS[argument]
    if stars == 0 and type(LrSelection.clearRating) == "function" then
        return selectionCall("rating cleared", "clearRating")
    end
    return selectionCall(stars .. " stars", "setRating", stars)
end

function handlers.colorLabel(argument)
    return selectionCall("label " .. argument, "setColorLabel", argument)
end

function handlers.switchModule(argument)
    local ok, missing = call(LrApplicationView, "switchToModule", argument)
    return ok, ok and argument or missing
end

function handlers.selectTool(argument)
    return developCall("tool " .. argument, "selectTool", argument)
end

function handlers.revealPanel(argument)
    return developCall("panel for " .. argument, "revealPanel", argument)
end

function handlers.zoomToggle()
    local ok, missing = call(LrApplicationView, "toggleZoom")
    return ok, ok and "zoom" or missing
end

function handlers.showClipping()
    return developCall("clipping", "showClipping")
end

function handlers.createMask(argument) return maskCall("new mask " .. argument, "createNewMask", argument) end
function handlers.addToMask(argument) return maskCall("added " .. argument, "addToCurrentMask", argument) end
function handlers.subtractFromMask(argument) return maskCall("subtracted " .. argument, "subtractFromCurrentMask", argument) end
function handlers.intersectWithMask(argument) return maskCall("intersected " .. argument, "intersectWithCurrentMask", argument) end
function handlers.invertMask() return developCall("mask inverted", "invertMask") end
function handlers.toggleMaskOverlay() return developCall("mask overlay", "toggleOverlay") end

function handlers.applyPreset(argument)
    local current = module()
    if current ~= "develop" and current ~= "library" then
        return false, "presets need the Develop or Library module"
    end
    if type(LrApplication.developPresetByUuid) ~= "function" then
        return false, "developPresetByUuid is not available in this Lightroom version"
    end
    local preset = LrApplication.developPresetByUuid(argument)
    if not preset then
        return false, "preset not found"
    end
    local catalog, photos = targetPhotos()
    if #photos == 0 then
        return false, "select a photo first"
    end
    local name = preset:getName()
    catalog:withWriteAccessDo("Lumerist: " .. tostring(name), function()
        for _, photo in ipairs(photos) do
            photo:applyDevelopPreset(preset, _PLUGIN)
        end
    end, { timeout = 3 })
    return true, tostring(name)
end

function handlers.copySettings()
    local catalog = LrApplication.activeCatalog()
    local photo = catalog:getTargetPhoto()
    if not photo then
        return false, "select a photo first"
    end
    if type(photo.copySettings) ~= "function" then
        return false, "copySettings is not available in this Lightroom version"
    end
    photo:copySettings()
    return true, "settings copied"
end

function handlers.pasteSettings()
    local catalog, photos = targetPhotos()
    if #photos == 0 then
        return false, "select a photo first"
    end
    if type(photos[1].pasteSettings) ~= "function" then
        return false, "pasteSettings is not available in this Lightroom version"
    end
    catalog:withWriteAccessDo("Lumerist: paste settings", function()
        for _, photo in ipairs(photos) do
            photo:pasteSettings()
        end
    end, { timeout = 3 })
    return true, "settings pasted"
end

--- Runs a validated command. Returns ok, message.
function Commands.run(name, argument)
    return handlers[name](argument)
end

--- The current module and Develop tool ("" outside Develop).
function Commands.context()
    local current = tostring(module() or "")
    local tool = ""
    if current == "develop" then
        tool = Develop.selectedTool()
    end
    return current, tool
end

local queries = {}

function queries.context()
    local current, tool = Commands.context()
    return { name = "context", module = current, tool = tool }
end

function queries.presets()
    local items = Json.array({})
    local bytes = 0
    local truncated = false
    for _, folder in ipairs(LrApplication.developPresetFolders() or {}) do
        local group = folder:getName()
        for _, preset in ipairs(folder:getDevelopPresets() or {}) do
            local item = { id = preset:getUuid(), name = preset:getName(), group = group }
            bytes = bytes + #tostring(item.id) + #tostring(item.name) + #tostring(group) + 32
            if bytes > MAXIMUM_PRESET_BYTES then
                truncated = true
                break
            end
            items[#items + 1] = item
        end
        if truncated then break end
    end
    return { name = "presets", items = items, truncated = truncated or nil }
end

function queries.catalog()
    if module() ~= "develop" then
        return nil, "the catalog check needs the Develop module"
    end
    local masking = Develop.selectedTool() == "masking"
        and (type(LrDevelopController.getSelectedMask) ~= "function" or LrDevelopController.getSelectedMask() ~= nil)
    local unsupported, skipped = Json.array({}), Json.array({})
    for _, entry in ipairs(Catalog.list("develop")) do
        if entry.special then
            -- handled by the plug-in itself
        elseif Catalog.isLocal(entry.id) and not masking then
            skipped[#skipped + 1] = entry.id -- needs a selected mask to be checked
        else
            local ok, minimum = pcall(LrDevelopController.getRange, entry.id)
            if not ok or minimum == nil then
                unsupported[#unsupported + 1] = entry.id
            end
        end
    end
    return { name = "catalog", unsupported = unsupported, skipped = skipped }
end

--- Runs a query. Returns the result payload, or nil and a reason.
function Commands.query(name)
    return queries[name]()
end

return Commands
