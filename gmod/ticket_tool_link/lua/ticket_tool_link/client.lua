-- Protocol v1 client: signed requests with verified replies, /hello, one
-- long-poll loop, /respond for each lookup or action, /event for reports and
-- /goodbye on stop. GMod runs
-- all Lua on one thread and HTTP() is callback based, so the "loop" is a
-- chain of callbacks and timers; a generation counter makes callbacks from a
-- stopped client do nothing.

local json = TicketToolLink.load("json")
local common = TicketToolLink.load("common")

local M = {}

M.PROTOCOL_VERSION = 1
M.DEFAULT_API_URL = "https://dash.ticket-tool.app"
M.API_PATH = "/api/plugin/v1"
M.SIGNATURE_WINDOW = 300
M.MAX_REPLY_BYTES = 256 * 1024
M.CALL_TIMEOUT = 10
M.GOODBYE_TIMEOUT = 2
M.PLAN_RETRY = 3600
M.RATE_LIMIT_WAIT = 60
-- A crashed server's poll lease lapses on Ticket Tool within 40 s.
M.TOO_MANY_POLLS_MIN_WAIT = 15
M.ANSWER_BUDGET = 3
M.KNOWN_PARTS = { profile = true, bans = true }
M.ACTION_KINDS = { "message", "warn", "kick", "unban" }
M.ACTION_BUDGET = 8
M.MAX_ACTION_MESSAGE = 500
M.MAX_REPORT_MESSAGE = 1000

-- Returns the base URL every endpoint hangs off, or nil and a message.
function M.apiBase(apiUrl)
	local url = common.trim(apiUrl)
	if url == "" then url = M.DEFAULT_API_URL end
	url = url:gsub("/+$", "")
	if url:sub(-#M.API_PATH) ~= M.API_PATH then url = url .. M.API_PATH end
	local scheme, host = url:match("^(%a[%w+.-]*)://([^/:]+)")
	if not scheme then return nil, "api-url isn't a valid URL" end
	scheme = scheme:lower()
	local loopback = host == "localhost" or host:match("^127%.") ~= nil
	if scheme ~= "https" and not (scheme == "http" and loopback) then
		return nil, "api-url must use https:// (plain http is only allowed for localhost)"
	end
	return url
end

-- Exponential backoff with equal jitter: a random point in the upper half of
-- each step, so servers that lost Ticket Tool together don't return in lockstep.
function M.newBackoff(random, base, max)
	base, max = base or 1, max or 60
	local b = { attempt = 0 }
	function b.next()
		local step = math.min(max, base * 2 ^ math.min(b.attempt, 30))
		b.attempt = b.attempt + 1
		local half = step / 2
		return half + random() * half
	end
	function b.reset() b.attempt = 0 end
	return b
end

local Client = {}
Client.__index = Client

--[[ opts: key (signer.parseKey result), apiBase, plugin {type, version, platform},
     userAgent, handler(request, reply), banProviders(), log {info, warn, debug},
     actionHandler(action, reply), actions() -> enabled kinds, reportsEnabled(),
     http (GMod HTTP), after(seconds, fn), now() wall seconds, random() in [0,1),
     nonce() ]]
function M.new(opts)
	local self = setmetatable({}, Client)
	self.key = assert(opts.key, "key")
	self.base = assert(opts.apiBase, "apiBase")
	self.plugin = assert(opts.plugin, "plugin")
	self.userAgent = opts.userAgent or ("ticket-tool-" .. self.plugin.type .. "/" .. self.plugin.version)
	self.handler = assert(opts.handler, "handler")
	self.banProviders = opts.banProviders or function() return {} end
	self.actionHandler = opts.actionHandler
	self.actions = opts.actions or function() return {} end
	self.reportsEnabled = opts.reportsEnabled or function() return false end
	self.log = opts.log
	self.http = opts.http
	self.after = opts.after
	self.now = opts.now
	self.random = opts.random
	self.nonce = opts.nonce
	self.answerBudget = opts.answerBudget or M.ANSWER_BUDGET
	self.backoff = M.newBackoff(self.random)
	self.offset = 0
	self.gen = 0
	self.state = "stopped"
	self.answered = 0
	self.failureStreak = 0
	self.running = false
	self.closed = false
	return self
end

function Client:nowSeconds()
	return math.floor(self.now()) + self.offset
end

function Client:serverNow()
	return self.now() + self.offset
end

function Client:status()
	return {
		state = self.state, lastError = self.lastError, lastErrorAt = self.lastErrorAt,
		hello = self.hello, lastPollAt = self.lastPollAt, answered = self.answered, offset = self.offset,
	}
end

local function lowerKeys(t)
	local out = {}
	for k, v in pairs(t or {}) do
		if type(k) == "string" then out[k:lower()] = v end
	end
	return out
end

function Client:verifyReply(endpoint, nonce, code, raw, headers)
	local h = lowerKeys(headers)
	local signature = h["x-tt-signature"]
	local success = code >= 200 and code < 300
	-- Errors are trusted unsigned anyway, so only a success has to verify.
	-- That matters for stale_timestamp: its reply carries the server's clock,
	-- which is outside our window until serverTime corrects it.
	if signature and not success then
		signature = nil
	end
	if signature then
		if h["x-tt-nonce"] ~= nonce then
			return nil, { kind = "verify", message = "Reply nonce doesn't match the request" }
		end
		local tsText = common.trim(h["x-tt-timestamp"])
		if not tsText:match("^%d+$") or #tsText > 12 then
			return nil, { kind = "verify", message = "Reply timestamp missing or malformed" }
		end
		if math.abs(self:nowSeconds() - tonumber(tsText)) > M.SIGNATURE_WINDOW then
			return nil, { kind = "verify", message = "Reply timestamp outside the signature window" }
		end
		if not self.key.verifyReply(endpoint, tsText, nonce, raw, signature) then
			return nil, { kind = "verify", message = "Reply signature doesn't match" }
		end
	elseif success then
		-- Only pre-auth errors may be unsigned; an unsigned success could be anyone's.
		return nil, { kind = "verify", message = "Unsigned success reply from " .. endpoint }
	end

	local parsed = {}
	if raw ~= "" then
		local value = json.decode(raw)
		if type(value) == "table" then
			parsed = value
		elseif success then
			return nil, { kind = "network", message = "Reply from " .. endpoint .. " wasn't JSON" }
		end
	end
	if success and parsed.ok ~= false then
		return parsed
	end
	return nil, {
		kind = "api", status = code, body = parsed,
		error = type(parsed.error) == "string" and parsed.error or nil,
	}
end

-- One signed request. The body is encoded once and those bytes are both
-- hashed and sent. cb(body) on success, cb(nil, err) otherwise.
function Client:send(endpoint, payload, timeout, cb)
	local body = json.encode(payload or json.object())
	local ts = self:nowSeconds()
	local nonce = self.nonce()
	local tsText = string.format("%.0f", ts)
	local finished = false
	local function finish(result, err)
		if finished then return end
		finished = true
		cb(result, err)
	end
	-- HTTP() normally reports its own timeout; this covers a request it drops.
	self.after(timeout + 5, function()
		finish(nil, { kind = "timeout", message = "Ticket Tool didn't answer in time" })
	end)
	local started, err = pcall(self.http, {
		url = self.base .. endpoint,
		method = "POST",
		body = body,
		type = "application/json",
		timeout = timeout,
		headers = {
			["Accept"] = "application/json",
			["X-TT-Key-Id"] = self.key.keyId,
			["X-TT-Timestamp"] = tsText,
			["X-TT-Nonce"] = nonce,
			["X-TT-Signature"] = self.key.signRequest("POST", endpoint, tsText, nonce, body),
			["X-TT-Protocol"] = tostring(M.PROTOCOL_VERSION),
			["X-TT-Plugin"] = self.userAgent,
		},
		success = function(code, raw, headers)
			raw = raw or ""
			if #raw > M.MAX_REPLY_BYTES then
				finish(nil, { kind = "network", message = "Reply larger than " .. M.MAX_REPLY_BYTES .. " bytes" })
				return
			end
			finish(self:verifyReply(endpoint, nonce, tonumber(code) or 0, raw, headers))
		end,
		failed = function(reason)
			local text = tostring(reason or "request failed")
			finish(nil, { kind = text == "timeout" and "timeout" or "network", message = text })
		end,
	})
	if not started then
		finish(nil, { kind = "network", message = tostring(err) })
	elseif err == false then
		finish(nil, { kind = "network", message = "HTTP request couldn't be started" })
	end
end

-- send() plus a single retry after correcting the clock from stale_timestamp.
function Client:call(endpoint, payload, timeout, cb)
	self:send(endpoint, payload, timeout, function(body, err)
		local serverTime = err and err.kind == "api" and tonumber(err.body.serverTime)
		if err and err.error == "stale_timestamp" and serverTime then
			self.offset = math.floor(serverTime) - math.floor(self.now())
			self.log.debug("Corrected clock offset to " .. self.offset .. " s")
			self:send(endpoint, payload, timeout, cb)
			return
		end
		cb(body, err)
	end)
end

function Client:start()
	if self.closed or self.running then return end
	self.running = true
	self.needHello = true
	self.state = "starting"
	self.gen = self.gen + 1
	local gen = self.gen
	self.after(0, function()
		if gen == self.gen then self:step() end
	end)
end

-- sendGoodbye=false is for a reload with the same key, where the next
-- client's /hello takes over at once.
function Client:stop(cb, sendGoodbye)
	if self.closed then
		if cb then cb() end
		return
	end
	self.closed = true
	self.running = false
	self.gen = self.gen + 1
	local wasConnected = sendGoodbye ~= false and self.everConnected and self.state ~= "stopped_invalid_key"
	if self.state ~= "stopped_invalid_key" and self.state ~= "stopped_error" then
		self.state = "stopped"
	end
	if wasConnected then
		self:send("/goodbye", json.object(), M.GOODBYE_TIMEOUT, function(_, err)
			if err then self.log.debug("Goodbye not delivered: " .. tostring(err.message or err.error)) end
			if cb then cb() end
		end)
	elseif cb then
		cb()
	end
end

function Client:wait(seconds, fn)
	local gen = self.gen
	self.log.debug(string.format("Retrying in %.1f s", seconds))
	self.after(seconds, function()
		if gen == self.gen and self.running then (fn or self.step)(self) end
	end)
end

function Client:helloBody()
	local providers = {}
	for i, p in ipairs(self.banProviders() or {}) do
		if i > 8 then break end
		providers[#providers + 1] = common.clip(p, 32)
	end
	local actions = {}
	if self.actionHandler then
		local enabled = {}
		for _, k in ipairs(self.actions() or {}) do enabled[k] = true end
		for _, k in ipairs(M.ACTION_KINDS) do
			if enabled[k] then actions[#actions + 1] = k end
		end
	end
	local plugin = json.object("type", self.plugin.type, "version", self.plugin.version,
		"platform", self.plugin.platform)
	return json.object("protocol", M.PROTOCOL_VERSION, "plugin", plugin,
		"banProviders", #providers > 0 and json.array(providers) or nil,
		"actions", #actions > 0 and json.array(actions) or nil,
		"events", self.reportsEnabled() and json.array({ "player.report" }) or nil)
end

-- Sends /hello again so the dashboard sees changed actions or reports.
function Client:refreshHello()
	self.needHello = true
end

local function field(t, k)
	if type(t) ~= "table" then return nil end
	return t[k]
end

local function str(v)
	return type(v) == "string" and v or nil
end

function M.parseHello(body)
	local wait = tonumber(field(body.poll, "waitSeconds"))
	return {
		protocol = tonumber(body.protocol) or M.PROTOCOL_VERSION,
		pluginType = str(body.pluginType),
		keyName = str(field(body.key, "name")),
		serverId = str(field(body.server, "id")),
		serverName = str(field(body.server, "name")),
		pollWaitSeconds = wait and math.max(1, math.min(120, wait)) or 25,
	}
end

function Client:step()
	if not self.running then return end
	local gen = self.gen
	if self.needHello then
		self:call("/hello", self:helloBody(), M.CALL_TIMEOUT, function(body, err)
			if gen ~= self.gen or not self.running then return end
			if err then return self:onError(err) end
			self.hello = M.parseHello(body)
			self.needHello = false
			self.everConnected = true
			self.log.info(string.format('Connected to Ticket Tool as "%s" for Discord server %s (%s)',
				self.hello.keyName or "?", self.hello.serverName or self.hello.serverId or "?", self.key.display))
			self:step()
		end)
		return
	end
	local timeout = math.max(35, self.hello.pollWaitSeconds + 10)
	self.lastPollAt = self.now()
	self.state = "connected"
	self:call("/poll", json.object(), timeout, function(body, err)
		if gen ~= self.gen or not self.running then return end
		if err then return self:onError(err) end
		self:recovered()
		local list = body.requests
		if type(list) == "table" then
			for _, item in ipairs(list) do
				self:dispatch(item)
			end
		end
		self:step()
	end)
end

function Client:recovered()
	if self.failureStreak > 0 then self.log.info("Reconnected to Ticket Tool") end
	self.failureStreak = 0
	self.backoff.reset()
end

function Client:note(message)
	self.lastError = message
	self.lastErrorAt = self.now()
	if self.failureStreak == 0 then
		self.log.warn(message)
	else
		self.log.debug(message)
	end
	self.failureStreak = self.failureStreak + 1
end

function Client:failed(message, wait)
	self.state = "reconnecting"
	self:note(message)
	self:wait(wait)
end

function Client:fatal(state, message)
	self.lastError = message
	self.lastErrorAt = self.now()
	self.state = state
	self.running = false
	self.gen = self.gen + 1
	self.log.warn(message)
end

function Client:onError(err)
	if err.kind ~= "api" then
		local message = err.kind == "timeout" and "Ticket Tool didn't answer in time"
			or err.kind == "verify" and ("Refused a reply from Ticket Tool: " .. err.message)
			or ("Can't reach Ticket Tool: " .. tostring(err.message))
		return self:failed(message, self.backoff.next())
	end
	local code = err.error or ("http_" .. tostring(err.status))
	local key = self.key.display
	if code == "invalid_key" then
		self:fatal("stopped_invalid_key", "Ticket Tool doesn't recognize key " .. key
			.. ". It was revoked, rotated or mistyped: set the current key from the dashboard and run ticket_tool_link reload.")
	elseif code == "wrong_plugin_type" then
		self:fatal("stopped_error", "Key " .. key .. " was made for a different game ("
			.. tostring(str(err.body.expected) or "?") .. "). Create a Garry's Mod key on the dashboard.")
	elseif code == "unsupported_protocol" then
		self:fatal("stopped_error", "Ticket Tool no longer serves protocol " .. M.PROTOCOL_VERSION .. ". Update the addon.")
	elseif code == "plan_not_entitled" then
		self.state = "paused_plan"
		self:note("The Discord server's plan doesn't include game lookups. Checking again in an hour.")
		self.needHello = true
		self:wait(M.PLAN_RETRY)
	elseif code == "rate_limited" then
		self:failed("Ticket Tool is rate limiting this key; waiting a minute", M.RATE_LIMIT_WAIT + self.random() * 5)
	elseif code == "too_many_polls" then
		local wait = self.backoff.next()
		if wait < M.TOO_MANY_POLLS_MIN_WAIT then wait = M.TOO_MANY_POLLS_MIN_WAIT + self.random() * 5 end
		self:failed("Three polls are already open for key " .. key
			.. ". Is the same key in use on another server? Each server needs its own key.", wait)
	elseif code == "bad_signature" or code == "missing_headers" or code == "bad_nonce" then
		self:failed("Ticket Tool rejected the request signature (" .. code .. "). Check the key was pasted whole.",
			self.backoff.next())
	else
		self:failed("Ticket Tool returned " .. code .. " (HTTP " .. tostring(err.status) .. ")", self.backoff.next())
	end
end

-- Turns a raw poll item into a request, or nil when it's malformed.
function M.parseRequest(item)
	if type(item) ~= "table" then return nil end
	local id, kind = str(item.id), str(item.type)
	local expiresAt = common.parseIso(item.expiresAt)
	if not id or not kind or not expiresAt then return nil end
	local player = type(item.player) == "table" and item.player or {}
	local include = {}
	if type(item.include) == "table" then
		for _, part in ipairs(item.include) do
			if type(part) == "string" and M.KNOWN_PARTS[part] then include[part] = true end
		end
	end
	local steamId = str(player.steamId)
	if steamId and not common.isSteamId64(steamId) then steamId = nil end
	return {
		id = id, type = kind, expiresAt = expiresAt, steamId = steamId, name = str(player.name),
		include = include, wantsProfile = include.profile == true, wantsBans = include.bans == true,
	}
end

function M.respondBody(request, result)
	if not result.ok then
		return json.object("requestId", request.id, "ok", false, "error", result.error or "error",
			"message", result.message and common.clip(result.message, 200) or nil)
	end
	return json.object("requestId", request.id, "ok", true,
		"profile", request.wantsProfile and result.profile or nil,
		"bans", request.wantsBans and result.bans or nil)
end

function Client:dispatch(item)
	local request = M.parseRequest(item)
	if not request then return end
	if request.type == "player.action" then return self:dispatchAction(item) end
	if request.type ~= "player.lookup" then
		self.log.debug("Ignoring request type " .. request.type)
		return
	end
	local received = self:serverNow()
	if request.expiresAt <= received then
		self.log.debug("Dropping expired request " .. request.id)
		return
	end
	if not request.steamId or not (request.wantsProfile or request.wantsBans) then
		return self:respond(request, { ok = false, error = "unsupported" })
	end

	local done = false
	local gen = self.gen
	local function reply(result)
		if done then return end
		done = true
		if gen ~= self.gen then return end
		if self:serverNow() >= request.expiresAt then
			self.log.debug("Answer for " .. request.id .. " is too late; not sending it")
			return
		end
		self:respond(request, result or { ok = false, error = "error", message = "The addon had no answer" })
	end
	local budget = math.min(received + self.answerBudget, request.expiresAt) - received
	self.after(math.max(0.05, budget), function()
		reply({ ok = false, error = "error", message = "The game server took too long to answer" })
	end)
	local ok, err = pcall(self.handler, request, reply)
	if not ok then
		self.log.warn("Lookup " .. request.id .. " failed: " .. tostring(err))
		reply({ ok = false, error = "error", message = "The addon hit an error answering this lookup" })
	end
end

local function isActionKind(kind)
	for _, k in ipairs(M.ACTION_KINDS) do
		if k == kind then return true end
	end
	return false
end

-- A player.action poll item, or nil when it's malformed or an unknown kind.
function M.parseAction(item)
	if type(item) ~= "table" or item.type ~= "player.action" then return nil end
	local id = str(item.id)
	local expiresAt = common.parseIso(item.expiresAt)
	local kind = str(field(item.action, "kind"))
	if not id or not expiresAt or not isActionKind(kind) then return nil end
	local player = type(item.player) == "table" and item.player or {}
	local steamId = str(player.steamId)
	if steamId and not common.isSteamId64(steamId) then steamId = nil end
	local message = kind ~= "unban" and str(field(item.action, "message")) or nil
	if message and common.trim(message) == "" then message = nil end
	local actor = common.cleanText(str(field(item.actor, "name")))
	return {
		id = id, expiresAt = expiresAt, steamId = steamId, name = str(player.name), kind = kind,
		message = message and common.clip(message, M.MAX_ACTION_MESSAGE) or nil,
		actorName = (actor and actor ~= "") and common.clip(actor, 64) or "Ticket Tool",
	}
end

function M.respondActionBody(action, result)
	if result.ok then return json.object("requestId", action.id, "ok", true) end
	return json.object("requestId", action.id, "ok", false, "error", result.error or "error",
		"message", result.message and common.clip(result.message, 200) or nil)
end

function Client:dispatchAction(item)
	local action = M.parseAction(item)
	if not action then return end
	local received = self:serverNow()
	if action.expiresAt <= received then
		self.log.debug("Dropping expired action " .. action.id)
		return
	end
	local allowed = false
	if self.actionHandler then
		for _, k in ipairs(self.actions() or {}) do
			if k == action.kind then allowed = true end
		end
	end
	if not allowed then
		self.log.info("Refused a " .. action.kind .. " from " .. action.actorName
			.. ": that action is off in the addon's config")
		return self:respondAction(action, { ok = false, error = "not_allowed" })
	end
	if not action.steamId then
		return self:respondAction(action, { ok = false, error = "unknown_player" })
	end
	local done = false
	local gen = self.gen
	local function reply(result)
		if done then return end
		done = true
		if gen ~= self.gen then return end
		-- Unlike a lookup, a late action already happened in game, so staff still hear about it.
		self:respondAction(action, result or { ok = false, error = "error", message = "The addon had no answer" })
	end
	local budget = math.min(received + M.ACTION_BUDGET, action.expiresAt) - received
	self.after(math.max(0.05, budget), function()
		reply({ ok = false, error = "error", message = "The game server took too long to carry it out" })
	end)
	local ok, err = pcall(self.actionHandler, action, reply)
	if not ok then
		self.log.warn("Action " .. action.id .. " failed: " .. tostring(err))
		reply({ ok = false, error = "error", message = "The addon hit an error carrying out this action" })
	end
end

function Client:respondAction(action, result)
	self:call("/respond", M.respondActionBody(action, result), M.CALL_TIMEOUT, function(_, err)
		if not err then
			self.answered = self.answered + 1
		elseif err.error == "unknown_request" then
			self.log.debug("Action " .. action.id .. " expired before the answer arrived")
		else
			self.log.warn("Ticket Tool refused the result of action " .. action.id .. ": "
				.. tostring(err.error or err.message))
		end
	end)
end

local function reportPlayer(p)
	local name = common.cleanText(p.name)
	return json.object("steamId", common.isSteamId64(p.steamId) and p.steamId or nil,
		"name", common.clip((name and name ~= "") and name or "Unknown", 64))
end

function M.reportBody(from, message, target, server)
	local targetName = target and common.cleanText(target.name)
	server = server and common.trim(server) or ""
	return json.object("type", "player.report", "player", reportPlayer(from),
		"message", common.clip(common.trim(message), M.MAX_REPORT_MESSAGE),
		"target", (targetName and targetName ~= "") and reportPlayer(target) or json.null,
		"server", server ~= "" and common.clip(server, 64) or json.null)
end

--[[ Sends an in-game report; from and target are { steamId, name }. Pass the
     player who ran the command, never values from its arguments.
     cb({ outcome = "opened"|"ignored"|"rate_limited"|"not_enabled"|"failed", error }) ]]
function Client:report(from, message, target, server, cb)
	if self.closed then return cb({ outcome = "failed", error = "not_running" }) end
	if not self.reportsEnabled() then return cb({ outcome = "not_enabled", error = "not_enabled" }) end
	if common.trim(message) == "" then return cb({ outcome = "failed", error = "empty_message" }) end
	self:call("/event", M.reportBody(from, message, target, server), M.CALL_TIMEOUT, function(body, err)
		if not err then
			return cb({ outcome = body.result == "opened" and "opened" or "ignored" })
		end
		if err.kind == "api" then
			local code = err.error or ("http_" .. tostring(err.status))
			return cb({ outcome = code == "rate_limited" and "rate_limited" or "failed", error = code })
		end
		self.log.warn("Couldn't reach Ticket Tool to send " .. tostring(from.name) .. "'s report: "
			.. tostring(err.message))
		cb({ outcome = "failed", error = "network" })
	end)
end

function Client:respond(request, result)
	self:call("/respond", M.respondBody(request, result), M.CALL_TIMEOUT, function(_, err)
		if not err then
			self.answered = self.answered + 1
		elseif err.error == "unknown_request" then
			self.log.debug("Request " .. request.id .. " expired before the answer arrived")
		else
			self.log.warn("Ticket Tool refused the answer to " .. request.id .. ": "
				.. tostring(err.error or err.message))
		end
	end)
end

return M
