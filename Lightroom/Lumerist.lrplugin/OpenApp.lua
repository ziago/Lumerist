-- Starts the Lumerist app on macOS and Windows: in the background when
-- Lightroom starts (Init.lua), and with its window in front from
-- File > Plug-in Extras > Open Lumerist (OpenAppMenu.lua).
local LrTasks = import "LrTasks"
local LrDialogs = import "LrDialogs"

local OpenApp = {}

-- Named in the dialog shown when the app cannot be found.
OpenApp.downloadPage = "https://github.com/ziago/Lumerist/releases"

-- Windows ---------------------------------------------------------------------
-- The Windows app writes %APPDATA%\Lumerist\plugin-link.json every time it
-- starts: { "exe": "C:\\...\\Lumerist.exe", "openWithLightroom": true }.
-- The plug-in reads it instead of querying the registry or the process list.
-- The app runs as a single instance: a second launch hands its arguments to
-- the running copy and exits, so nothing needs checking here first.

OpenApp.linkFolderName = "Lumerist"
OpenApp.linkFileName = "plugin-link.json"

--- Folders that may hold plugin-link.json, most likely first. The app writes
--- it under %APPDATA% (…\AppData\Roaming). Lightroom's own "appData" folder
--- may be a folder inside it (…\Roaming\Adobe\Lightroom), so the plug-in
--- climbs out of that one; the home folder's AppData\Roaming is tried too.
--- Every candidate belongs to the user, as the file names a program to start.
function OpenApp.windowsLinkFolders()
    local LrPathUtils = import "LrPathUtils"
    local folders = {}
    local function add(folder)
        if type(folder) == "string" and folder ~= "" then
            folders[#folders + 1] = LrPathUtils.child(folder, OpenApp.linkFolderName)
        end
    end
    local hasEnvironment, roaming = pcall(function() return os.getenv("APPDATA") end)
    if hasEnvironment then add(roaming) end
    local appData = LrPathUtils.getStandardFilePath("appData")
    add(appData)
    if type(appData) == "string" and appData:lower():match("\\adobe\\lightroom\\?$") then
        add(LrPathUtils.parent(LrPathUtils.parent(appData)))
    end
    local home = LrPathUtils.getStandardFilePath("home")
    if type(home) == "string" and home ~= "" then
        add(LrPathUtils.child(LrPathUtils.child(home, "AppData"), "Roaming"))
    end
    return folders
end

--- The app's path and options from plugin-link.json, or nil and a reason.
function OpenApp.readWindowsLink()
    local LrPathUtils = import "LrPathUtils"
    local LrFileUtils = import "LrFileUtils"
    local path
    for _, folder in ipairs(OpenApp.windowsLinkFolders()) do
        local candidate = LrPathUtils.child(folder, OpenApp.linkFileName)
        if LrFileUtils.exists(candidate) then
            path = candidate
            break
        end
    end
    if not path then return nil, "missing" end
    local ok, link = pcall(require("Json").decode, LrFileUtils.readFile(path))
    if not ok or type(link) ~= "table" or type(link.exe) ~= "string" then return nil, "unreadable" end
    -- A plain absolute path to Lumerist.exe, with nothing cmd.exe would read
    -- as quoting, variables or escapes.
    local exe = link.exe
    if not exe:match("^%a:\\") or exe:find('[%c"%%^]') or not exe:lower():match("\\lumerist%.exe$") then
        return nil, "invalid"
    end
    -- Uninstalling the app leaves this file behind: `start` on a missing exe
    -- would show a Windows error each time Lightroom starts.
    if LrFileUtils.exists(exe) ~= "file" then return nil, "gone" end
    return { exe = exe, openWithLightroom = link.openWithLightroom ~= false }
end

--- `start` returns at once (LrTasks.execute waits for the command), and
--- Lightroom runs the line through cmd.exe, which drops one pair of outer
--- quotes: hence the extra pair.
function OpenApp.windowsCommand(exe, argument)
    return '"start "" "' .. exe .. '" ' .. argument .. '"'
end

function OpenApp.launchWithLightroomOnWindows()
    LrTasks.startAsyncTask(function()
        local link = OpenApp.readWindowsLink()
        if link and link.openWithLightroom then
            LrTasks.execute(OpenApp.windowsCommand(link.exe, "--with-lightroom"))
        end
    end)
end

function OpenApp.runOnWindows()
    local link = OpenApp.readWindowsLink()
    if link and LrTasks.execute(OpenApp.windowsCommand(link.exe, "--show-window")) == 0 then
        return true
    end
    LrDialogs.message(
        "Lumerist",
        "The Lumerist app was not found on this PC. Download it from " .. OpenApp.downloadPage ..
            ", install it and open it once.",
        "warning"
    )
    return false
end

-- macOS -----------------------------------------------------------------------

OpenApp.bundleIdentifier = "io.github.ziago.lumerist"

-- `open` starts Lumerist, or brings it forward like a Dock click when it is
-- already running. --show-window keeps the window visible on a fresh launch
-- even with "Start with the window hidden" on.
OpenApp.commands = {
    "open -b " .. OpenApp.bundleIdentifier .. " --args --show-window",
    "open -a Lumerist --args --show-window",
}

-- Run as Lightroom starts: opens Lumerist in the background (no window, no
-- focus) unless it already runs or "Open Lumerist when Lightroom starts" is
-- off in its General settings. `defaults read` prints 0 when it is off.
OpenApp.launchCommand =
    "[ \"$(defaults read " .. OpenApp.bundleIdentifier .. " org.lumerist.open-with-lightroom 2>/dev/null)\" = 0 ]" ..
    " || pgrep -xq LumeristApp" ..
    " || open -g -b " .. OpenApp.bundleIdentifier .. " --args --with-lightroom"

function OpenApp.launchWithLightroom()
    if WIN_ENV then return OpenApp.launchWithLightroomOnWindows() end
    LrTasks.startAsyncTask(function()
        LrTasks.execute(OpenApp.launchCommand)
    end)
end

-- Must run inside a task (LrTasks.execute). Returns true when the app opened.
function OpenApp.run()
    if WIN_ENV then return OpenApp.runOnWindows() end
    for _, command in ipairs(OpenApp.commands) do
        if LrTasks.execute(command) == 0 then
            return true
        end
    end
    LrDialogs.message(
        "Lumerist",
        "The Lumerist app was not found on this Mac. Download it from " .. OpenApp.downloadPage ..
            ", move it to Applications and open it once.",
        "warning"
    )
    return false
end

return OpenApp
