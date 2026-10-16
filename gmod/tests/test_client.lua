return function(t)
	local stub = t.stub

	local function setup(opts)
		opts = opts or {}
		local server = dofile(TESTS_ROOT .. "/tests/fake_server.lua")(stub)
		local client = TicketToolLink.load("client")
		local signer = TicketToolLink.load("signer")
		local common = TicketToolLink.load("common")
		local random = TicketToolLink.load("random")
		local logs = {}
		local function logger(level)
			return function(msg) logs[#logs + 1] = level .. ": " .. msg end
		end
		local handled = {}
		local c = client.new({
			key = assert(signer.parseKey(server.KEY)),
			apiBase = server.base,
			plugin = { type = "gmod", version = "1.2.3", platform = "Garry's Mod test" },
			userAgent = "ticket-tool-gmod/1.2.3 (test)",
			handler = opts.handler or function(request, reply)
				handled[#handled + 1] = request
				reply({ ok = false, error = "unknown_player" })
			end,
			banProviders = function() return { "ulx", "server" } end,
			log = { info = logger("info"), warn = logger("warn"), debug = logger("debug") },
			http = function(req) return server.http(req) end,
			after = stub.loop.after,
			now = server.clientNow,
			random = function() return 0.5 end,
			nonce = function() return common.base64url(random.bytes(16)) end,
		})
		local function logText() return table.concat(logs, "\n") end
		return server, c, handled, logText
	end

	local function run(seconds, stop)
		return stub.loop.run(stub.loop.t + seconds, stop)
	end

	return {
		{ "hello, poll, lookup, respond, goodbye", function()
			local server, c, handled, logText = setup()
			c:start()
			run(1)
			t.eq(server.count("/hello"), 1, "one hello")
			t.eq(server.last("/hello").raw,
				'{"protocol":1,"plugin":{"type":"gmod","version":"1.2.3","platform":"Garry\'s Mod test"},"banProviders":["ulx","server"]}',
				"hello body")
			local h = server.last("/hello").headers
			t.eq(h["X-TT-Protocol"], "1", "protocol header")
			t.eq(h["X-TT-Plugin"], "ticket-tool-gmod/1.2.3 (test)", "plugin header")
			t.eq(#h["X-TT-Nonce"], 22, "nonce length")
			t.eq(c:status().state, "connected", "connected")
			t.contains(logText(), 'Connected to Ticket Tool as "Main" for Discord server My Discord (ttp_0123456789abcdef)', "log")

			local item = server.lookup()
			run(1)
			t.eq(#handled, 1, "handled")
			t.eq(handled[1].steamId, "76561197960287930", "steamId kept as a string")
			t.eq(handled[1].wantsBans, true, "bans wanted")
			t.eq(server.last("/respond").raw, '{"requestId":"' .. item.id .. '","ok":false,"error":"unknown_player"}', "respond body")
			t.eq(c:status().answered, 1, "counted")
			t.ok(server.count("/poll") >= 2, "polled again")

			local stopped = false
			c:stop(function() stopped = true end)
			run(5)
			t.ok(stopped, "stop callback")
			t.eq(server.count("/goodbye"), 1, "goodbye sent")
			local polls = server.count("/poll")
			run(120)
			t.eq(server.count("/poll"), polls, "no polling after stop")
			t.eq(#server.rejected, 0, "server accepted every request")
			t.ok(not logText():find("AAAAAAAAAA", 1, true), "key secret never logged")
		end },

		{ "answers only the parts asked for", function()
			local json = TicketToolLink.load("json")
			local server, c = setup({ handler = function(_, reply)
				reply({ ok = true, profile = json.object("steamId", "76561197960287930", "name", "Gabe", "online", true),
					bans = json.object("banned", false, "provider", "server") })
			end })
			c:start()
			run(1)
			local item = server.lookup({ include = { "bans", "inventory" } })
			run(1)
			t.eq(server.last("/respond").raw,
				'{"requestId":"' .. item.id .. '","ok":true,"bans":{"banned":false,"provider":"server"}}', "bans only")
		end },

		{ "drops expired lookups, ignores unknown types, and refuses lookups with no SteamID", function()
			local json = TicketToolLink.load("json")
			local server, c, handled = setup()
			c:start()
			run(1)
			server.lookup({ expiresAt = os.date("!%Y-%m-%dT%H:%M:%S.000Z", server.now() - 1) })
			server.lookup({ type = "player.inventory" })
			local noId = server.lookup({ player = { uuid = "069a79f4-44e9-4726-a5be-fca90e38aaf5", steamId = json.null, name = "Notch" } })
			local badId = server.lookup({ player = { steamId = "12345" } })
			run(1)
			t.eq(#handled, 0, "handler never called")
			t.eq(server.count("/respond"), 2, "two answers")
			t.eq(server.received[#server.received - 1].raw ~= nil, true, "sanity")
			local bodies = {}
			for _, r in ipairs(server.received) do
				if r.endpoint == "/respond" then bodies[r.body.requestId] = r.raw end
			end
			t.eq(bodies[noId.id], '{"requestId":"' .. noId.id .. '","ok":false,"error":"unsupported"}', "no steamId")
			t.eq(bodies[badId.id], '{"requestId":"' .. badId.id .. '","ok":false,"error":"unsupported"}', "bad steamId")
		end },

		{ "a slow handler gets a timeout answer within the budget", function()
			local server, c = setup({ handler = function() end })
			c:start()
			run(1)
			local item = server.lookup()
			local asked = stub.loop.t
			run(10, function() return server.count("/respond") > 0 end)
			local r = server.last("/respond")
			t.ok(r and r.at - asked <= 3.2, "answered within about 3 s")
			t.eq(r.raw, '{"requestId":"' .. item.id .. '","ok":false,"error":"error","message":"The game server took too long to answer"}', "timeout body")
		end },

		{ "a handler that throws answers error and keeps polling", function()
			local server, c = setup({ handler = function() error("boom") end })
			c:start()
			run(1)
			server.lookup()
			run(1)
			t.contains(server.last("/respond").raw, '"error":"error"', "error answer")
			t.eq(c:status().state, "connected", "still connected")
		end },

		{ "clock skew: corrects from serverTime and retries once with a fresh nonce", function()
			local server, c = setup()
			server.clockOffset = 1000
			c:start()
			run(1)
			t.eq(server.rejected[1], "stale_timestamp", "first try rejected")
			t.eq(server.count("/hello"), 1, "accepted after correction")
			t.eq(c:status().offset, 1000, "offset kept")
			t.eq(c:status().state, "connected", "connected")
		end },

		{ "an unsigned success reply is refused", function()
			local server, c, _, logText = setup()
			server.overrides["/hello"] = function() return 200, { ok = true }, false end
			c:start()
			run(0.5)
			t.eq(c:status().state, "reconnecting", "not connected")
			t.contains(logText(), "Unsigned success reply", "logged")
		end },

		{ "a reply with a bad signature or a different nonce is refused", function()
			local server, c, _, logText = setup()
			server.tamper = true
			c:start()
			run(0.5)
			t.eq(c:status().state, "reconnecting", "not connected")
			t.contains(logText(), "Reply signature doesn't match", "bad signature")

			server.tamper = false
			server.overrides["/hello"] = function(req)
				return 200, { ok = true }, true
			end
			local json = TicketToolLink.load("json")
			local origHttp = server.http
			-- Echo a nonce the client didn't send.
			c.http = function(req)
				local success = req.success
				req.success = function(code, raw, headers)
					headers["X-TT-Nonce"] = "AAAAAAAAAAAAAAAAAAAAAA"
					success(code, raw, headers)
				end
				return origHttp(req)
			end
			run(5)
			t.contains(logText(), "Reply nonce doesn't match", "nonce mismatch")
			t.ok(json ~= nil, "json loaded")
		end },

		{ "invalid_key stops for good", function()
			local server, c, _, logText = setup()
			server.overrides["/hello"] = function() return 401, { ok = false, error = "invalid_key" }, false end
			c:start()
			run(3600)
			t.eq(server.count("/hello"), 1, "no retries")
			t.eq(c:status().state, "stopped_invalid_key", "stopped")
			t.contains(logText(), "doesn't recognize key ttp_0123456789abcdef", "told the admin")
			c:stop()
			run(5)
			t.eq(server.count("/goodbye"), 0, "no goodbye for a dead key")
		end },

		{ "wrong_plugin_type and unsupported_protocol stop", function()
			local server, c, _, logText = setup()
			server.overrides["/hello"] = function() return 400, { ok = false, error = "wrong_plugin_type", expected = "minecraft" }, true end
			c:start()
			run(600)
			t.eq(server.count("/hello"), 1, "no retries")
			t.eq(c:status().state, "stopped_error", "stopped")
			t.contains(logText(), "made for a different game (minecraft)", "explains")
		end },

		{ "plan_not_entitled waits an hour, then says hello again", function()
			local server, c = setup()
			local refuse = true
			server.overrides["/hello"] = function()
				if refuse then return 403, { ok = false, error = "plan_not_entitled" }, true end
			end
			c:start()
			run(10)
			t.eq(c:status().state, "paused_plan", "paused")
			refuse = false
			run(3500)
			t.eq(server.count("/hello"), 1, "still waiting")
			run(200)
			t.eq(server.count("/hello"), 2, "hello again after an hour")
			t.eq(c:status().state, "connected", "connected")
		end },

		{ "too_many_polls waits at least 15 s; rate_limited a minute", function()
			local server, c = setup()
			local mode = "too_many_polls"
			server.overrides["/poll"] = function()
				if mode then return 429, { ok = false, error = mode }, true end
			end
			c:start()
			run(1)
			t.eq(server.count("/poll"), 1, "first poll")
			run(14)
			t.eq(server.count("/poll"), 1, "no poll inside 15 s")
			run(6)
			t.eq(server.count("/poll"), 2, "polls again after the wait")
			mode = "rate_limited"
			run(20)
			local n = server.count("/poll")
			run(30)
			t.eq(server.count("/poll"), n, "rate limit holds for a minute")
			mode = nil
			run(40)
			t.ok(server.count("/poll") > n, "back to polling")
		end },

		{ "network errors back off 1, 2, 4 ... and reset on success", function()
			local server, c = setup()
			local fail = true
			local times = {}
			local inner = server.http
			c.http = function(req)
				if fail then
					times[#times + 1] = stub.loop.t
					stub.loop.after(0.01, function() req.failed("unsuccessful") end)
					return true
				end
				return inner(req)
			end
			c:start()
			run(40)
			t.ok(#times >= 5, "retried")
			local gaps = {}
			for i = 2, 5 do gaps[#gaps + 1] = times[i] - times[i - 1] end
			-- random() is 0.5, so each wait is three quarters of its step.
			t.ok(math.abs(gaps[1] - 0.76) < 0.05 and math.abs(gaps[2] - 1.51) < 0.05 and math.abs(gaps[3] - 3.01) < 0.05,
				"backoff steps " .. table.concat(gaps, ", "))
			fail = false
			run(70)
			t.eq(c:status().state, "connected", "recovered")
			t.eq(c.backoff.attempt, 0, "backoff reset")
		end },

		{ "a dropped HTTP request is caught by the watchdog", function()
			local server, c = setup()
			local swallow = true
			local inner = server.http
			c.http = function(req)
				if swallow then return true end
				return inner(req)
			end
			c:start()
			run(16)
			t.contains(c:status().lastError or "", "didn't answer in time", "timed out")
			swallow = false
			run(20)
			t.eq(c:status().state, "connected", "recovered")
		end },

		{ "apiBase accepts a host or a full base and requires https", function()
			local client = TicketToolLink.load("client")
			t.eq(client.apiBase(""), "https://dash.ticket-tool.app/api/plugin/v1", "default")
			t.eq(client.apiBase("https://api.ticket-tool.app/api/plugin/v1/"), "https://api.ticket-tool.app/api/plugin/v1", "full base")
			t.eq(client.apiBase("http://127.0.0.1:8080"), "http://127.0.0.1:8080/api/plugin/v1", "loopback http")
			t.ok(client.apiBase("http://example.com") == nil, "plain http refused")
		end },
	}
end
