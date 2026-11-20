local LrDate = import "LrDate"
local LrFunctionContext = import "LrFunctionContext"
local LrLogger = import "LrLogger"
local LrPrefs = import "LrPrefs"
local LrSocket = import "LrSocket"
local LrTasks = import "LrTasks"
local Actions = require "Actions"
local Json = require "Json"

local Bridge = {}
local logger = LrLogger("Lumerist")

local REQUEST_PORT = 50550
local RESPONSE_PORT = 50551
local MAXIMUM_QUEUE_LENGTH = 256
local ACTIVE_POLL_SECONDS = 0.02
local IDLE_POLL_SECONDS = 0.1
local ACTIVE_WINDOW_SECONDS = 2
-- The context (module + Develop tool) is checked this often, so opening or
-- closing Masking reaches the companion within about a quarter second.
local CONTEXT_POLL_SECONDS = 0.15
local MAXIMUM_REQUEST_BYTES = 65536
-- The socket loop ticks every quarter second. If it stops ticking for this
-- long, an LrSocket call is stuck (seen when the companion quits halfway
-- through connecting: closing a "send" socket that never connected does not
-- return), and the worker starts a fresh bridge on a new generation.
Bridge.SOCKET_STALL_SECONDS = 20

-- Scripts of one loaded plug-in share `_G`, but "Reload plug-in" creates a new
-- Lua environment (Adobe SDK guide) while tasks from the old one can keep
-- running. Plug-in preferences survive across environments, so they carry the
-- two facts every environment must agree on: the bridge's status and which
-- start "owns" it.
-- Opened lazily and defensively: a failure here must never stop the bridge.
-- Without shared prefs the bridge still works within its own environment.
local prefsHandle = nil
local localPrefs = {}
local function sharedPrefs()
    if prefsHandle == nil then
        local ok, value = pcall(function()
            return LrPrefs.prefsForPlugin()
        end)
        if ok and value then
            prefsHandle = value
        else
            prefsHandle = false
            logger:warn("shared plug-in prefs unavailable: " .. tostring(value))
        end
    end
    return prefsHandle or localPrefs
end

--- Each start claims a new generation. Loops from an older generation, in any
--- environment, see the change and stop, so a reload or a stray second start
--- can never leave two bridges fighting over the ports.
local function claimGeneration()
    local prefs = sharedPrefs()
    local generation = (tonumber(prefs.bridgeGeneration) or 0) + 1
    prefs.bridgeGeneration = generation
    return generation
end

local function isCurrent(generation)
    return not _G.lumeristStopping and sharedPrefs().bridgeGeneration == generation
end

local function setStatus(value)
    if _G.lumeristBridgeStatus == value then
        return -- Rebinding repeats the same states every few seconds.
    end
    _G.lumeristBridgeStatus = value
    pcall(function()
        sharedPrefs().bridgeStatus = value
    end)
    logger:info(value)
end

--- Decodes one JSON line from the companion. Returns the request table, or
--- nil and a reason.
function Bridge.parseRequest(message)
    if type(message) ~= "string" then
        return nil, "socket payload must be text"
    end
    if #message > MAXIMUM_REQUEST_BYTES then
        return nil, "request too long"
    end
    local request, reason = Json.decode(message)
    if request == nil then
        return nil, reason or "malformed request"
    end
    if type(request) ~= "table" or Json.isArray(request) then
        return nil, "request must be a JSON object"
    end
    return request
end

-- Drops a multi-byte UTF-8 character cut in half by truncation; Swift's
-- JSONDecoder rejects the whole line otherwise.
local function trimPartialUTF8(text)
    local length = #text
    local index = length
    while index > 0 and index > length - 3 do
        local byte = text:byte(index)
        if byte < 0x80 or byte >= 0xC0 then
            break
        end
        index = index - 1
    end
    if index == 0 then
        return text
    end
    local lead = text:byte(index)
    local needed = (lead >= 0xF0 and 4) or (lead >= 0xE0 and 3) or (lead >= 0xC0 and 2) or 1
    if length - index + 1 < needed then
        return text:sub(1, index - 1)
    end
    return text
end

local ALLOWED_UNITS = { EV = true, K = true, deg = true, px = true, ["%"] = true, pt = true, choice = true }
local DETAIL_NUMBERS = { "value", "minimum", "maximum" }

local function line(object)
    return Json.encode(object) .. "\n"
end

--- Encodes one acknowledgement. `id` may be nil for unreadable requests.
--- `details` (optional) adds parameter, scope, value, range and unit.
function Bridge.encodeAck(id, accepted, message, details)
    local text = tostring(message or (accepted and "accepted" or "rejected"))
    if #text > 1024 then
        text = trimPartialUTF8(text:sub(1, 1024))
    end
    local object = {
        version = Actions.protocolVersion,
        kind = "ack",
        id = id,
        accepted = accepted and true or false,
        message = text,
    }
    if type(details) == "table" then
        if type(details.parameter) == "string" then object.parameter = details.parameter end
        if details.scope == "develop" or details.scope == "library" then object.scope = details.scope end
        for _, key in ipairs(DETAIL_NUMBERS) do
            if Json.isFinite(details[key]) then object[key] = details[key] end
        end
        if ALLOWED_UNITS[details.unit] then object.unit = details.unit end
    end
    return line(object)
end

--- Encodes the context event (current module and Develop tool).
function Bridge.encodeContext(module, tool)
    return line({
        version = Actions.protocolVersion,
        kind = "event",
        name = "context",
        module = tostring(module or ""),
        tool = tostring(tool or ""),
    })
end

--- Encodes a query result; `payload` carries name and data fields.
function Bridge.encodeResult(id, payload)
    local object = { version = Actions.protocolVersion, kind = "result", id = id }
    for key, value in pairs(payload) do
        if key ~= "version" and key ~= "kind" and key ~= "id" then
            object[key] = value
        end
    end
    return line(object)
end

--- Merges consecutive adjustments of the same scope + parameter so a fast
--- scroll becomes one Lightroom change. Order between requests is kept; the
--- merged batch answers with the id of the last request it contains.
function Bridge.coalesce(requests)
    local batches = {}
    for _, request in ipairs(requests) do
        local last = batches[#batches]
        if last and request.kind == "adjust" and last.kind == "adjust"
            and last.scope == request.scope and last.parameter == request.parameter then
            last.delta = last.delta + request.delta
            last.id = request.id
            last.count = last.count + 1
        else
            local batch = { count = 1 }
            for key, value in pairs(request) do
                batch[key] = value
            end
            batches[#batches + 1] = batch
        end
    end
    return batches
end

local function send(payload)
    local socket = _G.lumeristResponseSocket
    if not socket then
        logger:warn("Could not answer: response socket is unavailable")
        return false
    end
    local sent, sendError = pcall(function()
        socket:send(payload)
    end)
    if not sent then
        logger:warn("Could not answer: " .. tostring(sendError))
    end
    return sent
end

--- Called from LrSocket's onMessage (which cannot yield). Validation happens
--- immediately; Lightroom work happens later on the single worker task, one
--- batch at a time, so two requests never race each other.
function Bridge.enqueue(message)
    local request, parseError = Bridge.parseRequest(message)
    if not request then
        logger:warn("Rejected request: " .. tostring(parseError))
        send(Bridge.encodeAck(nil, false, parseError))
        return
    end
    local valid, reason = Actions.validate(request)
    if not valid then
        logger:warn("Rejected request: " .. tostring(reason))
        local id = Json.isFinite(request.id) and request.id == math.floor(request.id) and request.id or nil
        send(Bridge.encodeAck(id, false, reason))
        return
    end

    local queue = _G.lumeristQueue
    if #queue >= MAXIMUM_QUEUE_LENGTH then
        send(Bridge.encodeAck(request.id, false, "Lightroom is busy; request dropped"))
        return
    end
    queue[#queue + 1] = request
    _G.lumeristLastActivity = LrDate.currentTime()
end

--- Pushes the context event when the module/tool changed, or when a client
--- has just connected (lumeristContextDirty). Called from the worker.
function Bridge.pushContextIfChanged()
    local ok, module, tool = LrTasks.pcall(Actions.context)
    if not ok then
        return
    end
    local last = _G.lumeristLastContext
    local changed = not last or last.module ~= module or last.tool ~= tool
    if (changed or _G.lumeristContextDirty) and _G.lumeristResponseSocket then
        if send(Bridge.encodeContext(module, tool)) then
            _G.lumeristLastContext = { module = module, tool = tool }
            _G.lumeristContextDirty = false
        end
    end
end

local function runQueue(queue)
    for _, batch in ipairs(Bridge.coalesce(queue)) do
        local ok, message, details, result = Actions.run(batch)
        if result then
            send(Bridge.encodeResult(batch.id, result))
        else
            if not ok then
                logger:warn("Rejected " .. tostring(batch.kind) .. ": " .. tostring(message))
            end
            send(Bridge.encodeAck(batch.id, ok, message or (ok and "accepted" or "rejected"), details))
        end
    end
end

local function startWorker(generation)
    _G.lumeristWorkerRunning = true
    _G.lumeristWorkerGeneration = generation
    LrTasks.startAsyncTask(function()
        -- LrTasks.pcall (not pcall) because the loop yields in LrTasks.sleep.
        local ran, loopError = LrTasks.pcall(function()
            local lastContextPoll = 0
            while isCurrent(generation) do
                local now = LrDate.currentTime()
                if now - (_G.lumeristSocketHeartbeat or now) > Bridge.SOCKET_STALL_SECONDS then
                    setStatus("bridge stalled; starting it again")
                    Bridge.start()
                    return
                end
                if _G.lumeristContextDirty or now - lastContextPoll >= CONTEXT_POLL_SECONDS then
                    lastContextPoll = now
                    Bridge.pushContextIfChanged()
                end
                local queue = _G.lumeristQueue
                if #queue == 0 then
                    local recentlyActive = now - (_G.lumeristLastActivity or 0) < ACTIVE_WINDOW_SECONDS
                    LrTasks.sleep(recentlyActive and ACTIVE_POLL_SECONDS or IDLE_POLL_SECONDS)
                else
                    -- Lightroom tasks are cooperative, so swapping the table
                    -- cannot lose a request appended by onMessage.
                    _G.lumeristQueue = {}
                    runQueue(queue)
                end
            end
        end)
        if _G.lumeristWorkerGeneration == generation then
            _G.lumeristWorkerRunning = false
        end
        if not ran then
            -- The flag is cleared, so the next Bridge.start() can restart it.
            setStatus("command worker stopped: " .. tostring(loopError))
        end
    end)
end

local function runSockets(generation)
    -- LrSocket endpoints time out when no companion connects. Keep rebinding
    -- so the user never has to reload the plug-in manually.
    while isCurrent(generation) do
        _G.lumeristSocketHeartbeat = LrDate.currentTime()
        local retryAfterClose = false
        local called, socketError = LrTasks.pcall(LrFunctionContext.callWithContext, "LumeristSocket", function(context)
            local running = true
            local reconnectRequest = false
            local ok, receiver = pcall(function()
                return LrSocket.bind {
                    functionContext = context,
                    plugin = _PLUGIN,
                    port = REQUEST_PORT,
                    mode = "receive",
                    onConnecting = function()
                        setStatus("bridge listening on localhost:" .. REQUEST_PORT)
                    end,
                    onConnected = function()
                        _G.lumeristContextDirty = true
                        setStatus("local companion connected")
                    end,
                    onMessage = function(_, message)
                        Bridge.enqueue(message)
                    end,
                    onError = function(_, err)
                        reconnectRequest = true
                        if err ~= "timeout" then
                            setStatus("request socket error: " .. tostring(err) .. "; reconnecting")
                        end
                    end,
                    onClosed = function()
                        running = false
                        _G.lumeristRequestSocket = nil
                        setStatus("request socket closed")
                    end,
                }
            end)

            if not ok then
                setStatus("bridge bind failed: " .. tostring(receiver))
                retryAfterClose = true
                return
            end

            local responseOK, responder = pcall(function()
                return LrSocket.bind {
                    functionContext = context,
                    plugin = _PLUGIN,
                    port = RESPONSE_PORT,
                    mode = "send",
                    onConnecting = function()
                        setStatus("bridge waiting for local companion")
                    end,
                    onConnected = function()
                        _G.lumeristContextDirty = true
                        setStatus("local companion connected")
                    end,
                    onMessage = function()
                        -- Responses are one-way acknowledgements.
                    end,
                    onError = function(_, err)
                        -- Rebind instead of responder:reconnect(): reconnecting a
                        -- "send" socket can hang Lightroom, so close both and
                        -- start over.
                        running = false
                        if err ~= "timeout" then
                            setStatus("response socket error: " .. tostring(err) .. "; rebinding")
                        end
                    end,
                    onClosed = function()
                        running = false
                        _G.lumeristResponseSocket = nil
                        setStatus("response socket closed")
                    end,
                }
            end)

            if not responseOK then
                pcall(function()
                    receiver:close()
                end)
                setStatus("response socket bind failed: " .. tostring(responder))
                retryAfterClose = true
                return
            end

            _G.lumeristRequestSocket = receiver
            _G.lumeristResponseSocket = responder
            while running and isCurrent(generation) do
                _G.lumeristSocketHeartbeat = LrDate.currentTime()
                if reconnectRequest then
                    reconnectRequest = false
                    LrTasks.sleep(0.25)
                    local reconnectOK, reconnectError = pcall(function()
                        receiver:reconnect()
                    end)
                    if not reconnectOK then
                        setStatus("request socket reconnect failed: " .. tostring(reconnectError))
                        running = false
                    end
                end
                LrTasks.sleep(0.25)
            end

            pcall(function()
                receiver:close()
            end)
            pcall(function()
                responder:close()
            end)
            if _G.lumeristRequestSocket == receiver then _G.lumeristRequestSocket = nil end
            if _G.lumeristResponseSocket == responder then _G.lumeristResponseSocket = nil end
            retryAfterClose = isCurrent(generation)
        end)

        if not called then
            _G.lumeristRequestSocket = nil
            _G.lumeristResponseSocket = nil
            setStatus("bridge error: " .. tostring(socketError))
            retryAfterClose = true
        end
        if retryAfterClose and isCurrent(generation) then
            setStatus("bridge restarting")
            _G.lumeristSocketHeartbeat = LrDate.currentTime()
            LrTasks.sleep(1)
        end
    end
    if not _G.lumeristStopping and sharedPrefs().bridgeGeneration ~= generation then
        logger:info("bridge generation " .. generation .. " handed over to a newer start")
    end
    if sharedPrefs().bridgeGeneration == generation then
        _G.lumeristBridgeRunning = false
    end
end

--- Starts the bridge (called by Init.lua). Starting again, from any script
--- environment, supersedes the previous bridge instead of adding a second one.
function Bridge.start()
    local generation = claimGeneration()
    _G.lumeristStopping = false
    _G.lumeristQueue = {}
    _G.lumeristLastContext = nil
    _G.lumeristContextDirty = true
    _G.lumeristBridgeRunning = true
    _G.lumeristBridgeStatus = nil
    _G.lumeristSocketHeartbeat = LrDate.currentTime()
    setStatus("bridge starting on localhost:" .. REQUEST_PORT .. " (protocol " .. Actions.protocolVersion .. ")")
    startWorker(generation)
    LrTasks.startAsyncTask(function()
        runSockets(generation)
    end)
    return generation
end

--- The status as last reported by the running bridge, whichever script
--- environment it lives in.
function Bridge.status()
    return sharedPrefs().bridgeStatus or _G.lumeristBridgeStatus or "bridge has not started"
end

--- Stops the bridge (called by Shutdown.lua, also on Plug-in Manager reload).
--- Bumping the generation reaches loops started in other environments.
function Bridge.stop()
    _G.lumeristStopping = true
    claimGeneration()
    pcall(function()
        if _G.lumeristRequestSocket then
            _G.lumeristRequestSocket:close()
        end
    end)
    pcall(function()
        if _G.lumeristResponseSocket then
            _G.lumeristResponseSocket:close()
        end
    end)
    _G.lumeristRequestSocket = nil
    _G.lumeristResponseSocket = nil
    _G.lumeristQueue = {}
    setStatus("bridge stopped")
end

return Bridge
