-- Ticket Tool Link for Garry's Mod: server-side only, nothing is sent to clients.

TicketToolLink = TicketToolLink or {}

-- Modules are cached so each file runs once per load, however many other
-- modules use it; a fresh table on every autorun pass makes lua_refresh pick
-- up edited files.
local cache = {}
function TicketToolLink.load(name)
	local mod = cache[name]
	if mod == nil then
		mod = include("ticket_tool_link/" .. name .. ".lua")
		cache[name] = mod
	end
	return mod
end

TicketToolLink.load("main").init()
