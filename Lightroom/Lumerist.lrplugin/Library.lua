-- Library Quick Develop: relative changes applied to every target photo in
-- one catalog transaction (one undo step).

local LrApplication = import "LrApplication"
local LrApplicationView = import "LrApplicationView"

local Library = {}

local KELVIN_PER_TEMPERATURE_POINT = 50

-- Catalog id -> develop-settings keys to try (process 2012+ first) and range.
local SETTINGS = {
    Exposure = { keys = { "Exposure2012", "Exposure" }, minimum = -5, maximum = 5, unit = "EV" },
    Contrast = { keys = { "Contrast2012", "Contrast" }, minimum = -100, maximum = 100 },
    Highlights = { keys = { "Highlights2012" }, minimum = -100, maximum = 100 },
    Shadows = { keys = { "Shadows2012" }, minimum = -100, maximum = 100 },
    Whites = { keys = { "Whites2012" }, minimum = -100, maximum = 100 },
    Blacks = { keys = { "Blacks2012" }, minimum = -100, maximum = 100 },
    Clarity = { keys = { "Clarity2012", "Clarity" }, minimum = -100, maximum = 100 },
    Texture = { keys = { "Texture" }, minimum = -100, maximum = 100 },
    Vibrance = { keys = { "Vibrance" }, minimum = -100, maximum = 100 },
    Saturation = { keys = { "Saturation" }, minimum = -100, maximum = 100 },
}

local function isFinite(value)
    return type(value) == "number" and value == value and value ~= math.huge and value ~= -math.huge
end

-- Which settings key a photo uses for this parameter, its range, the change
-- to apply, and the unit to report.
local function resolve(id, settings, delta)
    if id == "Temperature" then
        if isFinite(settings.Temperature) then -- raw/DNG: Kelvin
            return "Temperature", 2000, 50000, delta * KELVIN_PER_TEMPERATURE_POINT, "K"
        end
        return "IncrementalTemperature", -100, 100, delta, "pt"
    end
    if id == "Tint" then
        if isFinite(settings.Tint) then
            return "Tint", -150, 150, delta, "pt"
        end
        return "IncrementalTint", -100, 100, delta, "pt"
    end
    local spec = SETTINGS[id]
    if not spec then
        return nil
    end
    local key = spec.keys[1]
    for _, candidate in ipairs(spec.keys) do
        if settings[candidate] ~= nil then
            key = candidate
            break
        end
    end
    return key, spec.minimum, spec.maximum, delta, spec.unit or "pt"
end

--- Returns ok, message, details (details describe the first target photo).
function Library.adjust(entry, delta)
    if LrApplicationView.getCurrentModuleName() ~= "library" then
        return false, "switch to the Library module first"
    end
    local catalog = LrApplication.activeCatalog()
    local photos = catalog:getTargetPhotos()
    if type(photos) ~= "table" or #photos == 0 then
        return false, "select photos first"
    end
    if not resolve(entry.id, {}, 0) then
        return false, entry.id .. " is not a Quick Develop control"
    end

    local changed = 0
    local first
    catalog:withWriteAccessDo("Lumerist: Quick " .. entry.id, function()
        for _, photo in ipairs(photos) do
            local settings = photo:getDevelopSettings() or {}
            local key, minimum, maximum, change, unit = resolve(entry.id, settings, delta)
            local current = tonumber(settings[key]) or 0
            local target = math.max(minimum, math.min(maximum, current + change))
            if math.abs(target - current) > 1e-9 then
                photo:applyDevelopSettings({ [key] = target }, "Lumerist: Quick " .. entry.id)
                changed = changed + 1
            end
            first = first or { value = target, minimum = minimum, maximum = maximum, unit = unit }
        end
    end, { timeout = 3 })

    local details = {
        parameter = entry.id,
        scope = "library",
        value = first and first.value,
        minimum = first and first.minimum,
        maximum = first and first.maximum,
        unit = first and first.unit,
    }
    local noun = #photos == 1 and "photo" or "photos"
    if changed == 0 then
        return true, "at the " .. entry.id .. " limit on the selected " .. noun, details
    end
    return true, changed .. " of " .. #photos .. " " .. noun .. " updated", details
end

return Library
