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
  "actions": [],
  "reports": false,
  "report-command": "report",
  "report-cooldown": 60,
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
	{ name = "ticket_tool_actions", field = "actions", help = "Actions staff may take from Discord: message,warn,kick,unban" },
	{ name = "ticket_tool_reports", field = "reports", help = "1 turns on the in-game report command" },
	{ name = "ticket_tool_report_command", field = "report-command", help = "Chat command for reports, without ! or /" },
	{ name = "ticket_tool_report_cooldown", field = "report-cooldown", help = "Seconds between reports per player" },
	{ name = "ticket_tool_debug", field = "debug", help = "1 logs more detail" },
}

local function bool(v, default)
	if v == nil or v == "" then return default end
	if type(v) == "boolean" then return v end
	v = tostring(v):lower()
	return v == "1" or v == "true" or v == "yes" or v == "on"
end

local ACTION_KINDS = { message = true, warn = true, kick = true, unban = true }
local ACTION_ORDER = { "message", "warn", "kick", "unban" }

-- A JSON array or a "kick, message" string; unknown kinds become problems.
local function actionList(v, problems)
	local items = {}
	if type(v) == "table" then
		for _, item in ipairs(v) do
			if type(item) == "string" then items[#items + 1] = item end
		end
	elseif type(v) == "string" then
		for item in v:gmatch("[^%s,]+") do items[#items + 1] = item end
	end
	local on = {}
	for _, item in ipairs(items) do
		item = item:lower()
		if ACTION_KINDS[item] then
			on[item] = true
		else
			problems[#problems + 1] = "actions: \"" .. item .. "\" isn't one of message, warn, kick, unban; ignoring it"
		end
	end
	local out = {}
	for _, k in ipairs(ACTION_ORDER) do
		if on[k] then out[#out + 1] = k end
	end
	return out
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
	local reportCommand = tostring(pick("report-command") or "report"):gsub("^[!/]", ""):lower()
	if not reportCommand:match("^[%w_]+$") or #reportCommand > 32 then
		problems[#problems + 1] = "report-command \"" .. reportCommand .. "\" isn't a valid command name; using report"
		reportCommand = "report"
	end
	local cooldown = tonumber(pick("report-cooldown"))
	return {
		key = pick("key") or "",
		serverName = pick("server-name") or "",
		banProvider = pick("ban-provider") or "auto",
		apiUrl = pick("api-url") or "",
		keepAwake = bool(pick("keep-awake"), true),
		actions = actionList(pick("actions"), problems),
		reports = bool(pick("reports"), false),
		reportCommand = reportCommand,
		reportCooldown = (cooldown and cooldown >= 0) and math.floor(cooldown) or 60,
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
