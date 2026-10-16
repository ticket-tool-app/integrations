-- Just enough of Garry's Mod's server Lua API to load the addon outside the
-- game: include, sql (real SQLite through a Python helper), file, player,
-- hooks, timers on a virtual clock, and HTTP() routed to whatever a test sets.

local stub = {}

local ROOT = assert(TESTS_ROOT, "TESTS_ROOT")
stub.ADDON = ROOT .. "/ticket_tool_link"
stub.LUA = stub.ADDON .. "/lua/"

bit = bit or dofile(ROOT .. "/tests/bit_shim.lua")

local tmp = os.getenv("TT_TEST_TMP") or ("/tmp/ttl-gmod-tests-" .. tostring(os.time()))
os.execute("mkdir -p '" .. tmp .. "'")
stub.tmp = tmp

-- Virtual clock and timers.
local loop = { t = 0, seq = 0, q = {} }
stub.loop = loop
function loop.after(delay, fn)
	loop.seq = loop.seq + 1
	loop.q[#loop.q + 1] = { at = loop.t + math.max(0, tonumber(delay) or 0), seq = loop.seq, fn = fn }
end
function loop.run(limit, stop)
	limit = limit or 1e9
	local steps = 0
	while #loop.q > 0 do
		if stop and stop() then return true end
		table.sort(loop.q, function(a, b)
			if a.at ~= b.at then return a.at < b.at end
			return a.seq < b.seq
		end)
		local item = loop.q[1]
		if item.at > limit then
			loop.t = limit
			return false
		end
		table.remove(loop.q, 1)
		loop.t = math.max(loop.t, item.at)
		item.fn()
		steps = steps + 1
		if steps > 100000 then error("event loop runaway") end
	end
	if stop then return stop() end
	return false
end
function loop.reset()
	loop.t, loop.seq, loop.q = 0, 0, {}
end

stub.output = {}
local function capture(...)
	local parts = {}
	for i = 1, select("#", ...) do
		local v = select(i, ...)
		if type(v) ~= "table" then parts[#parts + 1] = tostring(v) end
	end
	stub.output[#stub.output + 1] = table.concat(parts)
end

function stub.install()
	stub.output = {}
	stub.hooks = {}
	stub.commands = {}
	stub.convars = {}
	stub.players = {}
	stub.http = function() error("no HTTP handler set") end
	stub.consoleCommands = {}

	TicketToolLink = {}
	local cache = {}
	function TicketToolLink.load(name)
		local mod = cache[name]
		if mod == nil then
			mod = include("ticket_tool_link/" .. name .. ".lua")
			cache[name] = mod
		end
		return mod
	end

	SERVER = true
	VERSIONSTR = "2024.06.28"
	BRANCH = "x86-64"
	FCVAR_ARCHIVE, FCVAR_PROTECTED, FCVAR_UNLOGGED, FCVAR_DONTRECORD = 128, 32, 2048, 131072
	HUD_PRINTCONSOLE = 2
	ULib, sam, UTime = nil, nil, nil

	function include(path) return dofile(stub.LUA .. path) end
	function Color(r, g, b) return { r = r, g = g, b = b } end
	MsgC = capture
	print = capture
	function SysTime() return loop.t end
	function IsValid(v)
		if v == nil then return false end
		if type(v) == "table" and v.IsValid then return v:IsValid() end
		return true
	end
	function RunConsoleCommand(name, ...)
		stub.consoleCommands[#stub.consoleCommands + 1] = { name, ... }
		if stub.convars[name] then stub.convars[name].value = tostring((...)) end
	end

	hook = {
		Add = function(event, id, fn)
			stub.hooks[event] = stub.hooks[event] or {}
			stub.hooks[event][id] = fn
		end,
		Remove = function(event, id)
			if stub.hooks[event] then stub.hooks[event][id] = nil end
		end,
	}
	function stub.fire(event, ...)
		local list = {}
		for id, fn in pairs(stub.hooks[event] or {}) do list[#list + 1] = fn end
		for _, fn in ipairs(list) do fn(...) end
	end
	concommand = { Add = function(name, fn) stub.commands[name] = fn end }
	timer = { Simple = loop.after }

	local function convar(name)
		local c = stub.convars[name]
		if not c then return nil end
		return {
			GetString = function() return c.value end,
			GetBool = function() return c.value ~= "" and c.value ~= "0" end,
		}
	end
	function CreateConVar(name, default, flags, help)
		stub.convars[name] = stub.convars[name] or { value = default, flags = flags, help = help }
		return convar(name)
	end
	function ConVarExists(name) return stub.convars[name] ~= nil end
	GetConVar = convar
	stub.convars.sv_hibernate_think = { value = "0" }

	player = { GetHumans = function() return stub.players end }

	HTTP = function(req) return stub.http(req) end

	stub.dataDir = tmp .. "/data"
	stub.gameDir = tmp .. "/game"
	os.execute("rm -rf '" .. stub.dataDir .. "' '" .. stub.gameDir .. "'")
	os.execute("mkdir -p '" .. stub.dataDir .. "' '" .. stub.gameDir .. "/cfg'")
	local function resolve(path, where)
		return ((where == "DATA") and stub.dataDir or stub.gameDir) .. "/" .. path
	end
	file = {
		Read = function(path, where)
			local f = io.open(resolve(path, where or "DATA"), "rb")
			if not f then return nil end
			local text = f:read("*a")
			f:close()
			return text
		end,
		Write = function(path, text)
			local f = assert(io.open(resolve(path, "DATA"), "wb"))
			f:write(text)
			f:close()
		end,
		Exists = function(path, where)
			local f = io.open(resolve(path, where), "rb")
			if f then f:close() return true end
			return false
		end,
		CreateDir = function(path) os.execute("mkdir -p '" .. resolve(path, "DATA") .. "'") end,
	}

	stub.db = tmp .. "/sv.db"
	os.remove(stub.db)
	local lastError
	sql = {
		Query = function(query)
			local qf = tmp .. "/query.sql"
			local f = assert(io.open(qf, "wb"))
			f:write(query)
			f:close()
			local p = assert(io.popen("python3 '" .. ROOT .. "/tests/sqlite_shim.py' '" .. stub.db .. "' '" .. qf .. "'"))
			local out = p:read("*a")
			p:close()
			local res = stub.json().decode(out)
			if type(res) ~= "table" then error("sqlite shim failed: " .. tostring(out)) end
			if not res.ok then
				lastError = res.error
				return false
			end
			if #res.rows == 0 then return nil end
			return res.rows
		end,
		LastError = function() return lastError end,
		SQLStr = function(s) return "'" .. tostring(s):gsub("'", "''") .. "'" end,
		TableExists = function(name)
			local rows = sql.Query("SELECT name FROM sqlite_master WHERE type = 'table' AND name = " .. sql.SQLStr(name))
			return rows ~= nil and rows ~= false
		end,
	}

	util = {
		CRC = function(s) return stub.crc32(s) end,
	}
end

function stub.json()
	return dofile(stub.LUA .. "ticket_tool_link/json.lua")
end

local crcTable
function stub.crc32(s)
	if not crcTable then
		crcTable = {}
		for i = 0, 255 do
			local c = i
			for _ = 1, 8 do
				if c % 2 == 1 then c = bit.bxor(bit.rshift(c, 1), 0xEDB88320) else c = bit.rshift(c, 1) end
			end
			crcTable[i] = c
		end
	end
	local c = 0xFFFFFFFF
	for i = 1, #s do
		c = bit.bxor(bit.rshift(c, 8), crcTable[bit.band(bit.bxor(c, s:byte(i)), 255)])
	end
	return string.format("%.0f", bit.bnot(c) % 2 ^ 32)
end

function stub.player(opts)
	local p = {
		sid = opts.steamId, nick = opts.name or "Player", group = opts.group or "user",
		bot = opts.bot or false, superadmin = opts.superadmin or false, printed = {},
	}
	function p:IsValid() return true end
	function p:SteamID64() return self.sid end
	function p:Nick() return self.nick end
	function p:GetUserGroup() return self.group end
	function p:IsBot() return self.bot end
	function p:IsSuperAdmin() return self.superadmin end
	function p:PrintMessage(_, text) self.printed[#self.printed + 1] = text end
	if opts.utime then
		function p:GetUTimeTotalTime() return opts.utime end
	end
	return p
end

function stub.writeGameFile(path, text)
	local f = assert(io.open(stub.gameDir .. "/" .. path, "wb"))
	f:write(text)
	f:close()
end

function stub.allOutput()
	return table.concat(stub.output, "\n")
end

return stub
