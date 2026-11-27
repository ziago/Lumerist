-- Fake Lightroom SDK namespaces for running the plug-in's logic under LuaJIT
-- (Lua 5.1 semantics, like Lightroom Classic). Only the calls the plug-in
-- makes are modelled, and every call is recorded in env.calls.
-- Nothing here talks to Lightroom.

local Stubs = {}

local function defaultValues()
    return {
        Exposure = 0, Contrast = 0, Highlights = 0, Shadows = 0, Whites = 0, Blacks = 0,
        Temperature = 0, Tint = 0, Texture = 0, Clarity = 0, Dehaze = 0, Vibrance = 0, Saturation = 0,
        SplitToningShadowHue = 0, PostCropVignetteStyle = 1,
        CropLeft = 0, CropRight = 1, CropTop = 0, CropBottom = 1,
        local_Exposure = 0, local_Contrast = 0, local_Hue = 0, local_Amount = 100,
    }
end

local function defaultRanges()
    return {
        Exposure = { -5, 5 }, Contrast = { -100, 100 }, Highlights = { -100, 100 },
        Shadows = { -100, 100 }, Whites = { -100, 100 }, Blacks = { -100, 100 },
        Temperature = { -100, 100 }, Tint = { -150, 150 },
        Texture = { -100, 100 }, Clarity = { -100, 100 }, Dehaze = { -100, 100 },
        Vibrance = { -100, 100 }, Saturation = { -100, 100 },
        SplitToningShadowHue = { 0, 360 }, PostCropVignetteStyle = { 1, 3 },
        local_Exposure = { -4, 4 }, local_Contrast = { -100, 100 }, local_Hue = { -180, 180 },
        local_Amount = { 0, 100 },
    }
end

local function makePhoto(id, settings)
    local photo = { localIdentifier = id, settings = settings or {}, applied = {}, presets = {}, pasted = 0, copied = 0 }
    function photo:getDevelopSettings()
        local copy = {}
        for key, value in pairs(self.settings) do copy[key] = value end
        return copy
    end
    function photo:applyDevelopSettings(settings, historyName)
        self.applied[#self.applied + 1] = { settings = settings, historyName = historyName }
        for key, value in pairs(settings) do self.settings[key] = value end
    end
    function photo:applyDevelopPreset(preset, plugin)
        self.presets[#self.presets + 1] = { preset = preset, plugin = plugin }
    end
    function photo:copySettings() self.copied = self.copied + 1 end
    function photo:pasteSettings() self.pasted = self.pasted + 1 end
    return photo
end
Stubs.makePhoto = makePhoto

function Stubs.new()
    local env = {
        module = "develop",
        tool = "loupe",
        selectedMask = nil,
        clock = 1000,
        log = {},
        tasks = {},
        sleeps = {},
        onSleep = nil,
        prefs = {},
        calls = {},
        writeAccess = {},
        canUndo = true,
        canRedo = true,
    }
    env.photo = makePhoto(1, { Exposure2012 = 0 })
    env.photos = { env.photo }

    local function record(name, ...)
        env.calls[#env.calls + 1] = { name = name, args = { ... } }
    end

    --- How often a recorded SDK function was called, and its last arguments.
    function env.called(name)
        local count = 0
        local lastArgs
        for _, call in ipairs(env.calls) do
            if call.name == name then
                count = count + 1
                lastArgs = call.args
            end
        end
        return count, lastArgs
    end

    -- LrDevelopController with Lightroom's asynchronous behaviour: setValue
    -- only queues the change; getValue keeps returning the old value until the
    -- test calls env.develop.flush().
    local develop = {
        values = defaultValues(),
        ranges = defaultRanges(),
        pending = {},
        sets = {},
        failWith = nil,
    }

    function develop.getValue(parameter)
        if develop.failWith then error(develop.failWith, 0) end
        return develop.values[parameter]
    end

    function develop.getRange(parameter)
        if develop.failWith then error(develop.failWith, 0) end
        local range = develop.ranges[parameter]
        if not range then
            if develop.rangeErrors then error("unknown parameter " .. parameter, 0) end
            return nil, nil
        end
        return range[1], range[2]
    end

    function develop.setValue(parameter, value)
        develop.sets[#develop.sets + 1] = { parameter = parameter, value = value }
        develop.pending[#develop.pending + 1] = { parameter = parameter, value = value }
    end

    --- Applies the first `count` queued setValue calls (all when omitted).
    function develop.flush(count)
        count = count or #develop.pending
        for _ = 1, count do
            local change = table.remove(develop.pending, 1)
            if not change then break end
            develop.values[change.parameter] = change.value
        end
    end

    function develop.lastSet()
        return develop.sets[#develop.sets]
    end

    function develop.getSelectedTool() return env.tool end
    function develop.getSelectedMask() return env.selectedMask end
    for _, name in ipairs({
        "resetToDefault", "resetCrop", "startTracking", "stopTracking", "setAutoTone",
        "setAutoWhiteBalance", "resetAllDevelopAdjustments", "selectTool", "revealPanel",
        "showClipping", "createNewMask", "addToCurrentMask", "subtractFromCurrentMask",
        "intersectWithCurrentMask", "invertMask", "toggleOverlay",
    }) do
        develop[name] = function(...) record(name, ...) end
    end

    env.develop = develop

    local catalog = {}
    function catalog:getTargetPhoto()
        return env.photo
    end
    function catalog:getTargetPhotos()
        return env.photos
    end
    function catalog:withWriteAccessDo(name, fn, params)
        env.writeAccess[#env.writeAccess + 1] = { name = name, params = params }
        fn()
    end
    env.catalog = catalog

    local presets = {}
    local function makePreset(uuid, name)
        local preset = { uuid = uuid, name = name }
        function preset:getUuid() return self.uuid end
        function preset:getName() return self.name end
        presets[uuid] = preset
        return preset
    end
    local function makeFolder(name, list)
        local folder = { name = name, list = list }
        function folder:getName() return self.name end
        function folder:getDevelopPresets() return self.list end
        return folder
    end
    env.presetFolders = {
        makeFolder("Favourites", { makePreset("A1-B2", "Warm \"Film\""), makePreset("C3-D4", "Neutral") }),
        makeFolder("Black & White", { makePreset("E5-F6", "Selenium") }),
    }

    local function logger()
        local object = {}
        function object:enable() end
        function object:info(message) env.log[#env.log + 1] = { level = "info", message = message } end
        function object:warn(message) env.log[#env.log + 1] = { level = "warn", message = message } end
        function object:error(message) env.log[#env.log + 1] = { level = "error", message = message } end
        return object
    end

    local selection = {}
    for _, name in ipairs({ "nextPhoto", "previousPhoto", "flagAsPick", "flagAsReject", "removeFlag",
                            "setRating", "clearRating", "setColorLabel" }) do
        selection[name] = function(...) record(name, ...) end
    end

    local namespaces = {
        LrApplication = {
            activeCatalog = function() return catalog end,
            developPresetFolders = function() return env.presetFolders end,
            developPresetByUuid = function(uuid) return presets[uuid] end,
        },
        LrApplicationView = {
            getCurrentModuleName = function()
                if env.moduleError then error(env.moduleError, 0) end
                return env.module
            end,
            switchToModule = function(name) record("switchToModule", name) end,
            toggleZoom = function() record("toggleZoom") end,
        },
        LrDate = {
            currentTime = function() return env.clock end,
        },
        LrDevelopController = develop,
        LrSelection = selection,
        LrUndo = {
            canUndo = function() return env.canUndo end,
            canRedo = function() return env.canRedo end,
            undo = function() record("undo") end,
            redo = function() record("redo") end,
        },
        LrTasks = {
            pcall = pcall,
            startAsyncTask = function(fn) env.tasks[#env.tasks + 1] = fn end,
            execute = function(command)
                env.executed[#env.executed + 1] = command
                return env.executeStatus and env.executeStatus(command) or 0
            end,
            sleep = function(seconds)
                env.sleeps[#env.sleeps + 1] = seconds
                if env.onSleep then env.onSleep(seconds) end
            end,
        },
        LrLogger = logger,
        LrDialogs = {
            message = function(title, message, style)
                env.dialogs[#env.dialogs + 1] = { title = title, message = message, style = style }
            end,
        },
        -- Plug-in preferences: the only state Lightroom shares between the
        -- plug-in's script environments.
        LrPrefs = {
            prefsForPlugin = function()
                if env.prefsError then error(env.prefsError, 0) end
                return env.prefs
            end,
        },
        LrSocket = {
            bind = function(params)
                if env.bindSocket then return env.bindSocket(params) end
                error("LrSocket.bind is not available in tests", 0)
            end,
        },
        LrFunctionContext = {
            callWithContext = function(_, fn) return fn({}) end,
        },
        -- Paths and files for the Windows link file (OpenApp.lua).
        LrPathUtils = {
            getStandardFilePath = function(name) return env.standardPaths[name] end,
            child = function(parent, child) return parent .. env.pathSeparator .. child end,
            parent = function(path)
                local separator = env.pathSeparator:gsub("%p", "%%%0")
                return path:match("^(.*)" .. separator .. "[^" .. separator .. "]*$")
            end,
        },
        LrFileUtils = {
            exists = function(path) return env.files[path] ~= nil and "file" or false end,
            readFile = function(path) return env.files[path] end,
        },
    }

    function env.import(name)
        local namespace = namespaces[name]
        if not namespace then
            error("test stub missing for Lightroom namespace " .. tostring(name), 2)
        end
        return namespace
    end

    env.executed = {}
    env.dialogs = {}
    env.standardPaths = { appData = "/Users/test/Library/Application Support", home = "/Users/test" }
    env.pathSeparator = "/"
    env.files = {}

    -- Records every line the bridge would send to the companion.
    env.responses = {}
    env.responseSocket = {}
    function env.responseSocket:send(payload)
        env.responses[#env.responses + 1] = payload
    end

    return env
end

return Stubs
