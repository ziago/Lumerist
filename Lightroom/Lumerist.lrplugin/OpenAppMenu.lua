-- Menu item: File > Plug-in Extras > Open Lumerist
local LrTasks = import "LrTasks"
local OpenApp = require "OpenApp"

LrTasks.startAsyncTask(function()
    OpenApp.run()
end)
