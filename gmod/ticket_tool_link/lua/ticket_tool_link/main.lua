-- Wires the protocol client to Garry's Mod: convars and config, hooks,
-- the ticket_tool_link console command, and start/stop.

local TTL = TicketToolLink
local json = TTL.load("json")
local common = TTL.load("common")
local signer = TTL.load("signer")
local random = TTL.load("random")
local client = TTL.load("client")
local config = TTL.load("config")
local players = TTL.load("players")
local bans = TTL.load("bans")
local lookup = TTL.load("lookup")

local M = {}

TTL.VERSION = TTL.load("version")

local PREFIX_COLOR = Color(88, 101, 242)
local TEXT_COLOR = Color(230, 230, 230)
local WARN_COLOR = Color(255, 196, 0)

local settings = { debug = false }

local log = {
	info = function(msg) MsgC(PREFIX_COLOR, "[Ticket Tool] ", TEXT_COLOR, msg, "\n") end,
	warn = function(msg) MsgC(PREFIX_COLOR, "[Ticket Tool] ", WARN_COLOR, msg, "\n") end,
	debug = function(msg)
		if settings.debug then MsgC(PREFIX_COLOR, "[Ticket Tool] ", TEXT_COLOR, "(debug) " .. msg, "\n") end
	end,
}
TTL.log = log

-- os.time() only has whole seconds; SysTime() fills in the fraction, and the
-- anchor moves whenever the two drift apart (NTP steps, suspend).
local anchorWall, anchorSys = os.time(), SysTime()
local function now()
	local t = anchorWall + (SysTime() - anchorSys)
	local wall = os.time()
	if math.abs(t - wall) > 2 then
		anchorWall, anchorSys = wall, SysTime()
		t = wall
	end
	return t
end
TTL.now = now

local function platform()
	local p = "Garry's Mod " .. tostring(VERSIONSTR or VERSION or "?")
	if BRANCH and BRANCH ~= "unknown" then p = p .. " (" .. BRANCH .. ")" end
	return p
end

local function nonce()
	return common.base64url(random.bytes(16))
end

local state = {
	client = nil,
	keyDisplay = nil,
	fingerprint = nil,
	base = nil,
	chain = { "server" },
	problem = nil,
}
TTL.state = state

local function banChain()
	return state.chain
end

local function serverName()
	if settings.serverName ~= "" then return settings.serverName end
	local c = state.client
	return c and c.hello and c.hello.keyName or nil
end

local function keepAwake()
	if not settings.keepAwake then return end
	local cv = GetConVar("sv_hibernate_think")
	if cv and not cv:GetBool() then
		RunConsoleCommand("sv_hibernate_think", "1")
		log.info("Set sv_hibernate_think 1 so lookups are answered while the server is empty")
	end
end

-- (Re)reads settings and connects. Sends /goodbye for the old key only when
-- the key or URL changed; otherwise the new client's /hello takes over.
function M.reload()
	local cfg, problems = config.read()
	settings = cfg
	for _, p in ipairs(problems) do log.warn(p) end

	local chain, warning = bans.resolve(cfg.banProvider)
	state.chain = chain
	if warning then log.warn(warning) end

	local key, keyErr = signer.parseKey(cfg.key)
	local base, baseErr = client.apiBase(cfg.apiUrl)
	local old = state.client
	local changed = not key or not old or state.fingerprint ~= key.fingerprint or state.base ~= base
	state.client = nil
	if old then old:stop(nil, changed) end

	if not key then
		state.problem = keyErr == "No integration key set"
			and "No key set. Put the key in data/ticket_tool_link/config.json or set ticket_tool_key, then run ticket_tool_link reload."
			or keyErr
		state.keyDisplay = nil
		log.warn(state.problem)
		return
	end
	if not base then
		state.problem = baseErr
		log.warn(baseErr)
		return
	end
	state.problem = nil
	state.keyDisplay = key.display
	state.fingerprint = key.fingerprint
	state.base = base
	keepAwake()

	state.client = client.new({
		key = key,
		apiBase = base,
		plugin = { type = "gmod", version = TTL.VERSION, platform = platform() },
		userAgent = "ticket-tool-gmod/" .. TTL.VERSION .. " (" .. platform() .. ")",
		handler = lookup.newHandler({ banChain = banChain, serverName = serverName, now = now }),
		banProviders = banChain,
		log = log,
		http = HTTP,
		after = timer.Simple,
		now = now,
		random = math.random,
		nonce = nonce,
	})
	log.info("Connecting to Ticket Tool with key " .. key.display .. "; bans from " .. table.concat(chain, ", "))
	state.client:start()
end

local STATE_TEXT = {
	stopped = "Stopped",
	starting = "Connecting",
	connected = "Connected to Ticket Tool",
	reconnecting = "Reconnecting",
	paused_plan = "Paused: the Discord server's plan doesn't include game lookups",
	stopped_invalid_key = "Stopped: Ticket Tool doesn't recognize this key",
	stopped_error = "Stopped",
}

function M.statusLines()
	local lines = {}
	local c = state.client
	lines[#lines + 1] = "Ticket Tool Link " .. TTL.VERSION
	if not c then
		lines[#lines + 1] = "State: " .. (state.problem or "not configured")
		return lines
	end
	local s = c:status()
	lines[#lines + 1] = "State: " .. (STATE_TEXT[s.state] or s.state)
	lines[#lines + 1] = "Key: " .. tostring(state.keyDisplay)
	if s.hello then
		lines[#lines + 1] = "Discord server: " .. tostring(s.hello.serverName or s.hello.serverId)
			.. " (key \"" .. tostring(s.hello.keyName) .. "\")"
	end
	lines[#lines + 1] = "Server name on cards: " .. tostring(serverName() or "(none)")
	lines[#lines + 1] = "Ban sources: " .. table.concat(state.chain, ", ")
	lines[#lines + 1] = "Lookups answered: " .. s.answered
	if s.offset ~= 0 then lines[#lines + 1] = "Clock offset: " .. s.offset .. " s" end
	if s.lastError then
		lines[#lines + 1] = "Last problem: " .. s.lastError .. " (" .. math.floor(now() - s.lastErrorAt) .. " s ago)"
	end
	return lines
end

local function command(ply, _, args)
	-- Console, RCON or a superadmin only: status names the Discord server.
	if IsValid(ply) and not ply:IsSuperAdmin() then
		ply:PrintMessage(HUD_PRINTCONSOLE, "ticket_tool_link: superadmins only")
		return
	end
	local function out(line)
		if IsValid(ply) then ply:PrintMessage(HUD_PRINTCONSOLE, line) else print(line) end
	end
	local sub = (args[1] or "status"):lower()
	if sub == "reload" then
		M.reload()
		out("Ticket Tool Link reloaded.")
	elseif sub == "status" then
		for _, line in ipairs(M.statusLines()) do out(line) end
	else
		out("Usage: ticket_tool_link status | reload")
	end
end

function M.init()
	if TTL.shutdown then TTL.shutdown() end

	-- An empty dedicated server hibernates and stops running Think, timers
	-- and HTTP callbacks; staying awake lets lookups for offline players work.
	local early = config.merge({}, file.Read(config.FILE, "DATA"))
	if early.keepAwake then RunConsoleCommand("sv_hibernate_think", "1") end

	local ok, err = players.ensureTable()
	if not ok then log.warn("Couldn't create the player table in sv.db: " .. tostring(err)) end
	config.createConvars()

	hook.Add("PlayerInitialSpawn", "TicketToolLink", function(ply) players.record(ply, os.time()) end)
	hook.Add("PlayerDisconnected", "TicketToolLink", function(ply) players.record(ply, os.time()) end)
	hook.Add("ShutDown", "TicketToolLink", function() if TTL.shutdown then TTL.shutdown() end end)
	concommand.Add("ticket_tool_link", command, nil, "Ticket Tool Link: status | reload")

	TTL.shutdown = function()
		TTL.shutdown = nil
		if state.client then state.client:stop() end
		state.client = nil
	end

	-- HTTP() isn't usable until the server has ticked once.
	hook.Add("Think", "TicketToolLinkStart", function()
		hook.Remove("Think", "TicketToolLinkStart")
		for _, ply in ipairs(player.GetHumans()) do players.record(ply, os.time()) end
		M.reload()
	end)
end

TTL.reload = M.reload
TTL.json = json

return M
