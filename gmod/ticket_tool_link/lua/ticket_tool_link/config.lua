-- Settings come from convars (server.cfg) or data/ticket_tool_link/config.json,
-- with the same field names as the other Ticket Tool Link plugins. A convar
-- that is set wins over the file.

local json = TicketToolLink.load("json")
local common = TicketToolLink.load("common")

local M = {}

M.DIR = "ticket_tool_link"
M.FILE = "ticket_tool_link/config.json"

M.TEMPLATE = [[
{
  "key": "",
  "server-name": "",
  "ban-provider": "auto",
  "api-url": "https://dash.ticket-tool.app",
  "keep-awake": true,
  "debug": false
}
]]

-- Convar name -> config.json field.
M.CONVARS = {
	{ name = "ticket_tool_key", field = "key", help = "Integration key from the Ticket Tool dashboard", secret = true },
	{ name = "ticket_tool_server_name", field = "server-name", help = "Server name staff see on the ticket card" },
	{ name = "ticket_tool_ban_provider", field = "ban-provider", help = "auto, ulx, sam or server" },
	{ name = "ticket_tool_api_url", field = "api-url", help = "Ticket Tool URL (staging and testing only)" },
	{ name = "ticket_tool_keep_awake", field = "keep-awake", help = "1 keeps the server answering while empty" },
	{ name = "ticket_tool_debug", field = "debug", help = "1 logs more detail" },
}

local function bool(v, default)
	if v == nil or v == "" then return default end
	if type(v) == "boolean" then return v end
	v = tostring(v):lower()
	return v == "1" or v == "true" or v == "yes" or v == "on"
end

-- Returns the merged settings and a list of problems worth logging.
function M.merge(convarValues, fileText)
	local problems = {}
	local file = {}
	if fileText and common.trim(fileText) ~= "" then
		local parsed, err = json.decode(fileText)
		if type(parsed) == "table" then
			file = parsed
		else
			problems[#problems + 1] = "data/" .. M.FILE .. " isn't valid JSON (" .. tostring(err) .. "); ignoring it"
		end
	end
	local function pick(field)
		local cv = convarValues[field]
		if cv ~= nil and common.trim(cv) ~= "" then return common.trim(cv) end
		local fv = file[field]
		if fv == json.null then return nil end
		if type(fv) == "string" then return common.trim(fv) end
		return fv
	end
	return {
		key = pick("key") or "",
		serverName = pick("server-name") or "",
		banProvider = pick("ban-provider") or "auto",
		apiUrl = pick("api-url") or "",
		keepAwake = bool(pick("keep-awake"), true),
		debug = bool(pick("debug"), false),
	}, problems
end

function M.createConvars()
	for _, c in ipairs(M.CONVARS) do
		local flags = FCVAR_ARCHIVE
		if c.secret then
			-- PROTECTED: the value is never sent to clients or shown by the
			-- console's cvar query; UNLOGGED/DONTRECORD keep it out of logs and demos.
			flags = bit.bor(flags, FCVAR_PROTECTED, FCVAR_UNLOGGED, FCVAR_DONTRECORD)
		end
		if not ConVarExists(c.name) then
			CreateConVar(c.name, "", flags, c.help)
		end
	end
end

function M.read()
	local values = {}
	for _, c in ipairs(M.CONVARS) do
		local cv = GetConVar(c.name)
		values[c.field] = cv and cv:GetString() or nil
	end
	if not file.Exists(M.FILE, "DATA") then
		file.CreateDir(M.DIR)
		file.Write(M.FILE, M.TEMPLATE)
	end
	return M.merge(values, file.Read(M.FILE, "DATA"))
end

return M
