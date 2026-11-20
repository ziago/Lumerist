-- Develop-module operations: adjust, reset and gesture tracking through the
-- public LrDevelopController API. Every function here runs on the bridge's
-- single worker task, inside the dispatcher's LrTasks.pcall.

local LrApplication = import "LrApplication"
local LrApplicationView = import "LrApplicationView"
local LrDate = import "LrDate"
local LrDevelopController = import "LrDevelopController"
local Catalog = require "Catalog"

local Develop = {}

-- Each Temperature point is this many kelvin when Lightroom shows the Kelvin
-- scale (raw and DNG files); rendered files use Lightroom's -100..100 scale.
Develop.kelvinPerTemperaturePoint = 50

-- LrDevelopController applies changes asynchronously, so getValue can still
-- report a value from before our previous setValue. Within this window a
-- reading that matches one of our own recent values is treated as stale.
local STALE_READ_WINDOW_SECONDS = 1.5

-- Smallest crop (fraction of the image) "Crop zoom" goes down to.
local MINIMUM_CROP_SIZE = 0.05
local CROP_EDGES = { "CropLeft", "CropRight", "CropTop", "CropBottom" }

-- Post-crop vignette styles in Lightroom's order. getValue may report them as
-- numbers or as names; names are matched loosely and written back in the
-- spelling Lightroom used.
local VIGNETTE_STYLES = {
    { "Highlight Priority", "highlightPriority", "HIGHLIGHT_PRIORITY" },
    { "Color Priority", "colorPriority", "COLOR_PRIORITY" },
    { "Paint Overlay", "paintOverlay", "PAINT_OVERLAY" },
}

local recentWrites = {}
local recentCrop = nil
local trackingLocal = false

local function isFinite(value)
    return type(value) == "number" and value == value and value ~= math.huge and value ~= -math.huge
end

local function valueKey(value)
    return string.format("%.4f", value)
end

local function clamp(value, minimum, maximum)
    if isFinite(minimum) and value < minimum then value = minimum end
    if isFinite(maximum) and value > maximum then value = maximum end
    return value
end

--- Develop module + target photo, or nil and the reason.
-- Names of the LrDevelopController functions this Lightroom lacks, or nil.
-- Every catalog call goes through these, so an older Lightroom answers with a
-- clear rejection instead of a Lua error.
local function missingFunctions(names)
    local missing = {}
    for _, name in ipairs(names) do
        if type(LrDevelopController[name]) ~= "function" then
            missing[#missing + 1] = name
        end
    end
    if #missing == 0 then
        return nil
    end
    return "this Lightroom version does not provide LrDevelopController." .. table.concat(missing, ", ")
end

local function developPhoto()
    if LrApplicationView.getCurrentModuleName() ~= "develop" then
        return nil, "switch to the Develop module first"
    end
    local photo = LrApplication.activeCatalog():getTargetPhoto()
    if not photo then
        return nil, "select a photo first"
    end
    return photo
end

--- The tool Lightroom reports in Develop ("" when unknown).
function Develop.selectedTool()
    if type(LrDevelopController.getSelectedTool) ~= "function" then
        return ""
    end
    return tostring(LrDevelopController.getSelectedTool() or "")
end

--- Mask parameters only ever touch the selected mask: never fall back to the
--- global slider of the same name.
local function requireSelectedMask()
    if Develop.selectedTool() ~= "masking" then
        return false, "open the Masking panel to adjust a mask"
    end
    if type(LrDevelopController.getSelectedMask) == "function" then
        local mask = LrDevelopController.getSelectedMask()
        if mask == nil or mask == "" then
            return false, "select a mask first"
        end
    end
    return true
end

local function displayUnit(entry, maximum)
    if entry.unit == "temp" then
        return (isFinite(maximum) and maximum > 1000) and "K" or "pt"
    end
    return entry.unit
end

local function details(entry, value, minimum, maximum, unit)
    return {
        parameter = entry.id,
        scope = "develop",
        value = isFinite(value) and value or nil,
        minimum = isFinite(minimum) and minimum or nil,
        maximum = isFinite(maximum) and maximum or nil,
        unit = unit or displayUnit(entry, maximum),
    }
end

-- Chooses the value to add the delta to, accounting for queued setValue calls.
local function baseValue(id, photoId, current, now)
    local recent = recentWrites[id]
    if recent and recent.photoId == photoId
        and now - recent.time < STALE_READ_WINDOW_SECONDS
        and recent.known[valueKey(current)] then
        return recent.target, recent
    end
    return current, nil
end

local function remember(id, photoId, current, target, now, chain)
    local known = chain and chain.known or { [valueKey(current)] = true }
    known[valueKey(target)] = true
    recentWrites[id] = { photoId = photoId, target = target, time = now, known = known }
end

local function adjustNumeric(entry, delta, photo)
    local id = entry.id
    local current = LrDevelopController.getValue(id)
    if not isFinite(current) then
        if Catalog.isLocal(id) then
            return false, "select a mask first"
        end
        return false, "Lightroom did not report " .. id .. " for this photo"
    end

    local minimum, maximum = LrDevelopController.getRange(id)
    local change = delta
    if entry.unit == "temp" and isFinite(maximum) and maximum > 1000 then
        change = delta * Develop.kelvinPerTemperaturePoint
    end

    local now = LrDate.currentTime()
    local photoId = photo.localIdentifier
    local base, chain = baseValue(id, photoId, current, now)
    local target = base + change
    if entry.wraps then
        local low = isFinite(minimum) and minimum or 0
        local high = isFinite(maximum) and maximum or 360
        local span = high - low
        if span > 0 then
            target = low + ((target - low) % span)
        end
    else
        target = clamp(target, minimum, maximum)
    end

    if math.abs(target - base) < 1e-9 then
        -- Not an error: the slider is already at its end.
        return true, "at the " .. id .. " slider limit", details(entry, base, minimum, maximum)
    end

    LrDevelopController.setValue(id, target)
    remember(id, photoId, current, target, now, chain)
    return true, nil, details(entry, target, minimum, maximum)
end

local function normalized(text)
    return tostring(text):lower():gsub("[^%w]", "")
end

local function adjustChoice(entry, delta)
    local id = entry.id
    local direction = delta > 0 and 1 or -1
    local current = LrDevelopController.getValue(id)

    if isFinite(current) then
        local minimum, maximum = LrDevelopController.getRange(id)
        local low = isFinite(minimum) and minimum or 1
        local high = isFinite(maximum) and maximum or #VIGNETTE_STYLES
        local now = math.floor(current + 0.5)
        local target = clamp(now + direction, low, high)
        if target == now then
            return true, "at the last " .. id .. " option", details(entry, now, low, high, "choice")
        end
        LrDevelopController.setValue(id, target)
        local label = VIGNETTE_STYLES[target] and VIGNETTE_STYLES[target][1] or tostring(target)
        return true, label, details(entry, target, low, high, "choice")
    end

    if type(current) == "string" then
        -- Exact spelling first (to write back the same style), then loosely.
        local index, spelling
        for optionIndex, variants in ipairs(VIGNETTE_STYLES) do
            for variantIndex, variant in ipairs(variants) do
                if not index and variant == current then
                    index, spelling = optionIndex, variantIndex
                end
            end
        end
        if not index then
            for optionIndex, variants in ipairs(VIGNETTE_STYLES) do
                if not index and normalized(variants[1]) == normalized(current) then
                    index, spelling = optionIndex, 1
                end
            end
        end
        if not index then
            return false, "unknown " .. id .. " option: " .. current
        end
        local target = math.max(1, math.min(#VIGNETTE_STYLES, index + direction))
        if target == index then
            return true, "at the last " .. id .. " option", details(entry, index, 1, #VIGNETTE_STYLES, "choice")
        end
        LrDevelopController.setValue(id, VIGNETTE_STYLES[target][spelling])
        return true, VIGNETTE_STYLES[target][1], details(entry, target, 1, #VIGNETTE_STYLES, "choice")
    end

    return false, "Lightroom did not report " .. id .. " for this photo"
end

local function cropKey(crop)
    return string.format("%.4f/%.4f/%.4f/%.4f", crop.left, crop.right, crop.top, crop.bottom)
end

-- "Crop zoom": scale the crop rectangle around its centre, keeping its
-- aspect ratio. Positive delta (percent) zooms in, i.e. makes the crop smaller.
local function adjustCropZoom(entry, delta, photo)
    local values = {}
    for _, edge in ipairs(CROP_EDGES) do
        values[edge] = LrDevelopController.getValue(edge)
        if not isFinite(values[edge]) then
            return false, "Lightroom did not report the crop for this photo"
        end
    end
    local current = { left = values.CropLeft, right = values.CropRight, top = values.CropTop, bottom = values.CropBottom }

    local now = LrDate.currentTime()
    local base = current
    local known
    if recentCrop and recentCrop.photoId == photo.localIdentifier
        and now - recentCrop.time < STALE_READ_WINDOW_SECONDS
        and recentCrop.known[cropKey(current)] then
        base = recentCrop.target
        known = recentCrop.known
    end

    local width = base.right - base.left
    local height = base.bottom - base.top
    if width <= 0 or height <= 0 then
        return false, "the crop rectangle is empty"
    end
    local centerX = (base.left + base.right) / 2
    local centerY = (base.top + base.bottom) / 2

    local scale = 1 - delta / 100
    local maximumScale = math.min(2 * math.min(centerX, 1 - centerX) / width, 2 * math.min(centerY, 1 - centerY) / height)
    local minimumScale = math.max(MINIMUM_CROP_SIZE / width, MINIMUM_CROP_SIZE / height)
    scale = math.max(minimumScale, math.min(maximumScale, scale))

    local newWidth = width * scale
    local newHeight = height * scale
    local percent = newWidth * 100
    if math.abs(newWidth - width) < 1e-6 then
        return true, "at the crop zoom limit", details(entry, width * 100, MINIMUM_CROP_SIZE * 100, 100, "%")
    end

    local target = {
        left = clamp(centerX - newWidth / 2, 0, 1),
        right = clamp(centerX + newWidth / 2, 0, 1),
        top = clamp(centerY - newHeight / 2, 0, 1),
        bottom = clamp(centerY + newHeight / 2, 0, 1),
    }
    LrDevelopController.setValue("CropLeft", target.left)
    LrDevelopController.setValue("CropRight", target.right)
    LrDevelopController.setValue("CropTop", target.top)
    LrDevelopController.setValue("CropBottom", target.bottom)

    known = known or { [cropKey(current)] = true }
    known[cropKey(target)] = true
    recentCrop = { photoId = photo.localIdentifier, time = now, target = target, known = known }
    return true, nil, details(entry, percent, MINIMUM_CROP_SIZE * 100, 100, "%")
end

--- Applies a validated change (possibly the sum of several requests). The
--- total is limited only by the range Lightroom reports.
--- Returns ok, message, details.
function Develop.adjust(entry, delta)
    local unavailable = missingFunctions({ "getValue", "getRange", "setValue" })
    if unavailable then
        return false, unavailable
    end
    local photo, reason = developPhoto()
    if not photo then
        return false, reason
    end
    if Catalog.isLocal(entry.id) then
        local ready, why = requireSelectedMask()
        if not ready then
            return false, why
        end
    end
    if entry.special and entry.id == "lumerist.cropZoom" then
        return adjustCropZoom(entry, delta, photo)
    end
    if entry.enumerated then
        return adjustChoice(entry, delta)
    end
    return adjustNumeric(entry, delta, photo)
end

function Develop.reset(entry)
    local unavailable = missingFunctions({ entry.id == "lumerist.cropZoom" and "resetCrop" or "resetToDefault" })
    if unavailable then
        return false, unavailable
    end
    local photo, reason = developPhoto()
    if not photo then
        return false, reason
    end
    if Catalog.isLocal(entry.id) then
        local ready, why = requireSelectedMask()
        if not ready then
            return false, why
        end
    end
    if entry.id == "lumerist.cropZoom" then
        LrDevelopController.resetCrop()
        recentCrop = nil
    else
        LrDevelopController.resetToDefault(entry.id)
        recentWrites[entry.id] = nil
    end
    return true, "reset", { parameter = entry.id, scope = "develop" }
end

--- One Dial Key gesture = one history step (best effort).
function Develop.gesture(phase, entry)
    if LrApplicationView.getCurrentModuleName() ~= "develop" then
        return false, "switch to the Develop module first"
    end
    if phase == "begin" then
        if not entry or entry.special then
            return true, "tracking skipped", entry and { parameter = entry.id, scope = "develop" } or nil
        end
        if type(LrDevelopController.startTracking) ~= "function" then
            return false, "tracking is not available in this Lightroom version"
        end
        -- Same rule as adjust: a mask gesture never tracks the global slider.
        if Catalog.isLocal(entry.id) then
            local ready, why = requireSelectedMask()
            if not ready then
                return false, why
            end
        end
        LrDevelopController.startTracking(entry.id)
        trackingLocal = Catalog.isLocal(entry.id)
        return true, "tracking", { parameter = entry.id, scope = "develop" }
    end
    if type(LrDevelopController.stopTracking) ~= "function" then
        return false, "tracking is not available in this Lightroom version"
    end
    -- A local (mask) gesture is closed as a local history step.
    LrDevelopController.stopTracking(trackingLocal or (entry ~= nil and Catalog.isLocal(entry.id)))
    trackingLocal = false
    return true, "tracking stopped", entry and { parameter = entry.id, scope = "develop" } or nil
end

--- Test hook: forget remembered writes.
function Develop._resetForTests()
    recentWrites = {}
    recentCrop = nil
    trackingLocal = false
end

return Develop
