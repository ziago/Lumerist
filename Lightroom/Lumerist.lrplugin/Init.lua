local LrLogger = import "LrLogger"

-- The log is enabled before anything else so that a failure while loading the
-- plug-in's modules is written to Lumerist.log instead of disappearing.
local logger = LrLogger("Lumerist")
logger:enable("logfile")
logger:info("Lumerist plug-in loading")

local ok, failure = pcall(function()
    local Actions = require "Actions"
    local Bridge = require "SocketBridge"
    logger:info("Lumerist plug-in initialized (protocol " .. Actions.protocolVersion .. ")")
    Bridge.start()
    -- Lumerist follows Lightroom: started here, it quits when Lightroom quits.
    require("OpenApp").launchWithLightroom()
end)

if not ok then
    logger:error("Lumerist failed to start: " .. tostring(failure))
end
