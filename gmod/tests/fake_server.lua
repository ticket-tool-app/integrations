-- An in-process protocol v1 server for client tests: checks each request's
-- signature, clock window and nonce reuse the way Ticket Tool does, signs its
-- replies, and answers on the stub's virtual clock.

local KEY = "ttp_0123456789abcdef.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"

return function(stub)
	local json = TicketToolLink.load("json")
	local signer = TicketToolLink.load("signer")
	local secret = signer.deriveSigningSecret(KEY)
	local loop = stub.loop

	local s = {
		KEY = KEY,
		base = "https://dash.example.test/api/plugin/v1",
		clockOffset = 0, -- server clock minus client clock
		pollWait = 25,
		latency = 0.05,
		queue = {}, -- poll items waiting to go out
		received = {}, -- every accepted request
		rejected = {},
		nonces = {},
		overrides = {}, -- endpoint -> fn(req) returning status, body, signed (or nil to fall through)
		tamper = false,
	}

	function s.clientNow() return 1792065600 + loop.t end
	function s.now() return math.floor(s.clientNow() + s.clockOffset) end

	local function reply(req, endpoint, status, body, signed, nonce)
		local raw = type(body) == "string" and body or json.encode(body)
		local headers = { ["content-type"] = "application/json" }
		if signed then
			local ts = tostring(s.now())
			headers["X-TT-Timestamp"] = ts
			headers["X-TT-Nonce"] = nonce
			local sig = signer.signWith(secret, "RESPONSE", endpoint, ts, nonce, raw)
			if s.tamper then sig = sig:sub(1, -2) .. (sig:sub(-1) == "0" and "1" or "0") end
			headers["X-TT-Signature"] = sig
		end
		loop.after(s.latency, function() req.success(status, raw, headers) end)
	end

	local function accept(req)
		local endpoint = req.url:sub(#s.base + 1)
		local h = req.headers
		local nonce = h["X-TT-Nonce"]
		if h["X-TT-Key-Id"] ~= "0123456789abcdef" then
			return reply(req, endpoint, 401, { ok = false, error = "invalid_key" }, false)
		end
		local ts = tonumber(h["X-TT-Timestamp"])
		if not ts or math.abs(ts - s.now()) > 300 then
			s.rejected[#s.rejected + 1] = "stale_timestamp"
			return reply(req, endpoint, 401, json.object("ok", false, "error", "stale_timestamp", "serverTime", s.now()), true, nonce)
		end
		if not nonce or not nonce:match("^[A-Za-z0-9_%-]+$") or #nonce < 16 or #nonce > 64 then
			s.rejected[#s.rejected + 1] = "bad_nonce"
			return reply(req, endpoint, 401, { ok = false, error = "bad_nonce" }, true, nonce)
		end
		if h["X-TT-Signature"] ~= signer.signWith(secret, "POST", endpoint, h["X-TT-Timestamp"], nonce, req.body) then
			s.rejected[#s.rejected + 1] = "bad_signature"
			return reply(req, endpoint, 401, { ok = false, error = "bad_signature" }, true, nonce)
		end
		if s.nonces[nonce] then
			s.rejected[#s.rejected + 1] = "replayed"
			return reply(req, endpoint, 401, { ok = false, error = "replayed" }, true, nonce)
		end
		s.nonces[nonce] = true
		local body = json.decode(req.body)
		local entry = { endpoint = endpoint, body = body, raw = req.body, nonce = nonce, at = loop.t, headers = h }
		s.received[#s.received + 1] = entry

		local fn = s.overrides[endpoint]
		if fn then
			local status, out, signed = fn(entry)
			if status then return reply(req, endpoint, status, out, signed ~= false, nonce) end
		end
		if endpoint == "/hello" then
			return reply(req, endpoint, 200, json.object("ok", true, "protocol", 1, "pluginType", "gmod",
				"key", json.object("name", "Main"), "server", json.object("id", "123", "name", "My Discord"),
				"poll", json.object("waitSeconds", s.pollWait)), true, nonce)
		elseif endpoint == "/poll" then
			local function flush()
				local items = s.queue
				s.queue = {}
				return json.object("ok", true, "requests", json.array(items))
			end
			if #s.queue > 0 then return reply(req, endpoint, 200, flush(), true, nonce) end
			local done = false
			local function answer()
				if done then return end
				done = true
				s.pending = nil
				reply(req, endpoint, 200, flush(), true, nonce)
			end
			s.pending = answer
			loop.after(s.pollWait, answer)
			return
		elseif endpoint == "/event" then
			return reply(req, endpoint, 200, { ok = true, result = "opened" }, true, nonce)
		elseif endpoint == "/respond" or endpoint == "/goodbye" then
			return reply(req, endpoint, 200, { ok = true }, true, nonce)
		end
		reply(req, endpoint, 404, { ok = false, error = "not_found" }, true, nonce)
	end

	s.http = function(req)
		assert(req.method == "POST", "POST only")
		assert(req.type == "application/json", "content type")
		assert(req.url:sub(1, #s.base) == s.base, "base URL")
		accept(req)
		return true
	end

	function s.count(endpoint)
		local n = 0
		for _, r in ipairs(s.received) do
			if r.endpoint == endpoint then n = n + 1 end
		end
		return n
	end

	function s.last(endpoint)
		for i = #s.received, 1, -1 do
			if s.received[i].endpoint == endpoint then return s.received[i] end
		end
	end

	function s.lookup(fields)
		local item = {
			id = "req-" .. tostring(#s.received), type = "player.lookup",
			expiresAt = os.date("!%Y-%m-%dT%H:%M:%S.000Z", s.now() + 5),
			player = { uuid = json.null, steamId = "76561197960287930", discordId = json.null, name = "Gabe" },
			include = { "profile", "bans" },
		}
		for k, v in pairs(fields or {}) do item[k] = v end
		s.queue[#s.queue + 1] = item
		if s.pending then s.pending() end
		return item
	end

	function s.action(kind, message, fields)
		local item = {
			id = "act-" .. tostring(#s.received), type = "player.action",
			expiresAt = os.date("!%Y-%m-%dT%H:%M:%S.000Z", s.now() + 10),
			player = { uuid = json.null, steamId = "76561197960287930", discordId = json.null, name = "Gabe" },
			action = { kind = kind, message = message == nil and json.null or message },
			actor = { name = "mod.alex" },
		}
		for k, v in pairs(fields or {}) do item[k] = v end
		s.queue[#s.queue + 1] = item
		if s.pending then s.pending() end
		return item
	end

	return s
end
