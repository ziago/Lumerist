local LrLogger = import "LrLogger"
local Bridge = require "SocketBridge"

local logger = LrLogger("Lumerist")
logger:enable("logfile")
Bridge.stop()
logger:info("Lumerist plug-in shut down")

