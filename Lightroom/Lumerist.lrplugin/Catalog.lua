-- Index over the generated Parameters.lua (from Shared/parameters.json).
local entries = require "Parameters"

local Catalog = {}
local index = {}
for _, entry in ipairs(entries) do
    index[entry.scope .. ":" .. entry.id] = entry
end

--- The catalog entry for a parameter in one scope ("develop"/"library"), or nil.
function Catalog.get(scope, id)
    if type(scope) ~= "string" or type(id) ~= "string" then
        return nil
    end
    return index[scope .. ":" .. id]
end

--- Every entry of one scope, in catalog order.
function Catalog.list(scope)
    local result = {}
    for _, entry in ipairs(entries) do
        if entry.scope == scope then
            result[#result + 1] = entry
        end
    end
    return result
end

--- Mask (local adjustment) parameters act on the selected mask only.
function Catalog.isLocal(id)
    return type(id) == "string" and id:sub(1, 6) == "local_"
end

return Catalog
