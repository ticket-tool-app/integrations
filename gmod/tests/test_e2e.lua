-- End to end against tests/e2e_server.py over real HTTP, with HTTP() played
-- by curl. Runs only when TT_E2E_URL is set (tests/e2e.sh sets it).

return function(t)
	local url = os.getenv("TT_E2E_URL")
	if not url or url == "" then return {} end
	local stub = t.stub
	local KEY = "ttp_0123456789abcdef.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"

	local function shellQuote(s)
		return "'" .. tostring(s):gsub("'", "'\\''") .. "'"
	end

	local function readAll(path)
		local f = io.open(path, "rb")
		if not f then return "" end
		local s = f:read("*a")
		f:close()
		return s
	end

	-- GMod's HTTP() on top of curl: same request table, callbacks on the next tick.
	local function curlHttp(req)
		local dir = stub.tmp
		local reqFile, bodyFile, headFile = dir .. "/e2e-req", dir .. "/e2e-body", dir .. "/e2e-head"
		local f = assert(io.open(reqFile, "wb"))
		f:write(req.body or "")
		f:close()
		local args = { "curl", "-sS", "-o", shellQuote(bodyFile), "-D", shellQuote(headFile),
			"-w", "'%{http_code}'", "--max-time", tostring(req.timeout or 60), "-X", req.method,
			"--data-binary", shellQuote("@" .. reqFile), "-H", shellQuote("Content-Type: " .. req.type) }
		for k, v in pairs(req.headers or {}) do
			args[#args + 1] = "-H"
			args[#args + 1] = shellQuote(k .. ": " .. v)
		end
		args[#args + 1] = shellQuote(req.url)
		local p = io.popen(table.concat(args, " ") .. " 2>/dev/null")
		local code = tonumber(p:read("*a"))
		p:close()
		if not code or code == 0 then
			stub.loop.after(0, function() req.failed("unsuccessful") end)
			return true
		end
		local headers = {}
		for line in readAll(headFile):gmatch("[^\r\n]+") do
			local k, v = line:match("^([^:]+):%s*(.*)$")
			if k then headers[k] = v end
		end
		local body = readAll(bodyFile)
		stub.loop.after(0, function() req.success(code, body, headers) end)
		return true
	end

	local function serverLog()
		local p = io.popen("curl -sS " .. shellQuote(url .. "/__log"))
		local text = p:read("*a")
		p:close()
		return assert(TicketToolLink.load("json").decode(text))
	end

	return {
		{ "connects, corrects skew, answers a lookup and says goodbye over real HTTP", function()
			os.execute("mkdir -p '" .. stub.dataDir .. "/ticket_tool_link'")
			local f = assert(io.open(stub.dataDir .. "/ticket_tool_link/config.json", "wb"))
			f:write('{"key":"' .. KEY .. '","api-url":"' .. url .. '","server-name":"e2e"}')
			f:close()
			ULib = { bans = { ["STEAM_0:0:11101"] = { reason = "Mass RDM", admin = "Mod(STEAM_0:1:2)", time = os.time() - 60, unban = 0 } } }
			stub.http = curlHttp
			dofile(stub.LUA .. "autorun/server/ticket_tool_link.lua")
			stub.players = { stub.player({ steamId = "76561197960287930", name = "Gabe", group = "vip" }) }
			stub.fire("Think")

			local responded = false
			local real = stub.http
			stub.http = function(req)
				if req.url:find("/respond", 1, true) then
					local success = req.success
					req.success = function(...) responded = true return success(...) end
				end
				return real(req)
			end
			local deadline = os.time() + 20
			stub.loop.run(nil, function() return responded or os.time() > deadline end)
			t.ok(responded, "lookup answered")

			stub.fire("ShutDown")
			stub.loop.run(stub.loop.t + 5)

			local log = serverLog()
			local endpoints, respond = {}, nil
			for _, r in ipairs(log.accepted) do
				endpoints[#endpoints + 1] = r.endpoint
				if r.endpoint == "/respond" then respond = r.body end
			end
			local seq = table.concat(endpoints, ",")
			t.ok(seq:match("^/hello,/poll,") ~= nil, "hello then poll: " .. seq)
			t.ok(seq:match(",/goodbye$") ~= nil, "goodbye last: " .. seq)
			t.eq(#log.rejected, 1, "only the first request was rejected")
			t.eq(log.rejected[1].error, "stale_timestamp", "for clock skew")
			t.contains(respond, '{"requestId":"e2e-1","ok":true,"profile":{"steamId":"76561197960287930","name":"Gabe","online":true', "profile")
			t.contains(respond, '"server":"e2e"', "server name")
			t.contains(respond, '"bans":{"banned":true,"provider":"ulx","reason":"Mass RDM"', "ULX ban")
			t.contains(respond, '"actor":"Mod"', "actor")
			t.contains(log.accepted[1].plugin, "ticket-tool-gmod/1.0.0-dev (Garry's Mod", "X-TT-Plugin")
		end },
	}
end
