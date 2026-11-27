-- The secret that proves a request comes from the Lumerist app (protocol 4).
--
-- The app creates it once: 32 random bytes written as 64 hexadecimal
-- characters to a file only the user's own account can read:
--   macOS:   ~/Library/Application Support/Lumerist/bridge-token
--   Windows: %APPDATA%\Lumerist\bridge-token (next to plugin-link.json)
-- Every request line carries it. Another account on the same computer, or a
-- web page posting to 127.0.0.1, cannot read the file, so it cannot drive
-- Lightroom through the bridge.
--
-- onMessage cannot yield, so the file is never read there: `accepts` only
-- compares with the token already read, and marks it stale on a mismatch.
-- The worker task then reads the file again (`refreshIfNeeded`), at most once
-- a second, which also picks up a token the app created after Lightroom
-- started.

local BridgeToken = {}

BridgeToken.fileName = "bridge-token"
BridgeToken.length = 64

local REREAD_SECONDS = 1

local current = nil
local lastRead = nil
local stale = true

--- Where the app writes the file, most likely first.
function BridgeToken.paths()
    local LrPathUtils = import "LrPathUtils"
    local paths = {}
    if WIN_ENV then
        -- The same folders as plugin-link.json; every one belongs to the user.
        for _, folder in ipairs(require("OpenApp").windowsLinkFolders()) do
            paths[#paths + 1] = LrPathUtils.child(folder, BridgeToken.fileName)
        end
        return paths
    end
    local home = LrPathUtils.getStandardFilePath("home")
    if type(home) == "string" and home ~= "" then
        local folder = LrPathUtils.child(LrPathUtils.child(LrPathUtils.child(home, "Library"), "Application Support"), "Lumerist")
        paths[1] = LrPathUtils.child(folder, BridgeToken.fileName)
    end
    return paths
end

--- The token in a file's text, or nil: 64 hexadecimal characters, surrounding
--- whitespace allowed, compared in lower case.
function BridgeToken.parse(text)
    if type(text) ~= "string" then
        return nil
    end
    local token = text:match("^%s*(%x+)%s*$")
    if not token or #token ~= BridgeToken.length then
        return nil
    end
    return token:lower()
end

local function readFrom(path)
    local LrFileUtils = import "LrFileUtils"
    if LrFileUtils.exists(path) ~= "file" then
        return nil
    end
    local ok, text = pcall(LrFileUtils.readFile, path)
    if not ok then
        return nil
    end
    return BridgeToken.parse(text)
end

--- Reads the file now. Without a readable token every request is refused.
--- Must run inside a task (file access).
function BridgeToken.refresh(now)
    lastRead = now
    stale = false
    current = nil
    local ok, paths = pcall(BridgeToken.paths)
    if not ok then
        return nil
    end
    for _, path in ipairs(paths) do
        local token = readFrom(path)
        if token then
            current = token
            break
        end
    end
    return current
end

--- Reads the file again when a request carried a token the plug-in did not
--- know, or while it has none; at most once every REREAD_SECONDS.
function BridgeToken.refreshIfNeeded(now)
    if (stale or current == nil) and (lastRead == nil or now - lastRead >= REREAD_SECONDS) then
        BridgeToken.refresh(now)
    end
end

--- Whether a request's token is this computer's. Safe from onMessage: no
--- file access. (Lua strings are interned, so `==` takes the same time for
--- every wrong token.)
function BridgeToken.accepts(candidate)
    if current ~= nil and type(candidate) == "string" and candidate == current then
        return true
    end
    stale = true
    return false
end

--- Test hook: forget the token read so far.
function BridgeToken._resetForTests()
    current, lastRead, stale = nil, nil, true
end

return BridgeToken
