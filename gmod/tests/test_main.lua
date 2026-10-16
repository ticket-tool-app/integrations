return function(t)
	local stub = t.stub
	local KEY = "ttp_0123456789abcdef.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"

	local function boot(configText)
		if configText then
			os.execute("mkdir -p '" .. stub.dataDir .. "/ticket_tool_link'")
			local f = assert(io.open(stub.dataDir .. "/ticket_tool_link/config.json", "wb"))
			f:write(configText)
			f:close()
		end
		local server
		stub.http = function(req) return server.http(req) end
		dofile(stub.LUA .. "autorun/server/ticket_tool_link.lua")
		server = dofile(TESTS_ROOT .. "/tests/fake_server.lua")(stub)
		server.base = "https://dash.ticket-tool.app/api/plugin/v1"
		return server
	end

	local function command(ply, ...)
		stub.commands.ticket_tool_link(ply, "ticket_tool_link", { ... })
	end

	return {
		{ "boots from config.json, connects on the first tick, answers a lookup", function()
			local server = boot('{"key":"' .. KEY .. '","server-name":"TTT 1"}')
			t.ok(stub.convars.ticket_tool_key ~= nil, "convars created")
			t.ok(bit.band(stub.convars.ticket_tool_key.flags, FCVAR_PROTECTED) ~= 0, "key convar is protected")
			t.eq(stub.convars.sv_hibernate_think.value, "1", "kept awake")
			stub.players = { stub.player({ steamId = "76561197960287930", name = "Gabe", group = "admin" }) }
			stub.fire("Think")
			stub.loop.run(stub.loop.t + 1)
			t.eq(server.count("/hello"), 1, "hello")
			t.contains(server.last("/hello").raw, '"plugin":{"type":"gmod","version":"1.0.0-dev","platform":"Garry\'s Mod 2024.06.28 (x86-64)"}', "plugin info")
			t.contains(server.last("/hello").raw, '"banProviders":["server"]', "ban providers")

			local item = server.lookup()
			stub.loop.run(stub.loop.t + 1)
			local body = server.last("/respond").raw
			t.contains(body, '"requestId":"' .. item.id .. '","ok":true', "answered")
			t.contains(body, '"steamId":"76561197960287930","name":"Gabe","online":true', "matched the online player")
			t.contains(body, '"server":"TTT 1"', "server name")
			t.contains(body, '"bans":{"banned":false,"provider":"server"', "bans")

			command(nil, "status")
			local out = stub.allOutput()
			t.contains(out, "State: Connected to Ticket Tool", "status")
			t.contains(out, "Key: ttp_0123456789abcdef", "key id shown")
			t.ok(not out:find("AAAAAAAAAA", 1, true), "secret never printed")

			stub.fire("ShutDown")
			stub.loop.run(stub.loop.t + 3)
			t.eq(server.count("/goodbye"), 1, "goodbye on shutdown")
		end },

		{ "writes a config template and explains a missing key", function()
			boot(nil)
			stub.fire("Think")
			t.ok(file.Exists("ticket_tool_link/config.json", "DATA"), "template written")
			t.contains(stub.allOutput(), "No key set", "explains")
			command(nil, "status")
			t.contains(stub.allOutput(), "State: No key set", "status")
		end },

		{ "a convar key wins over config.json; a mangled key is never echoed", function()
			local server = boot('{"key":"ttp_ffffffffffffffff.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}')
			stub.convars.ticket_tool_key.value = "ttp_0123456789abcdef.notAKeyAtAll"
			stub.fire("Think")
			t.contains(stub.allOutput(), "That isn't an integration key", "explains")
			t.ok(not stub.allOutput():find("notAKeyAtAll", 1, true), "not echoed")
			stub.convars.ticket_tool_key.value = KEY
			command(nil, "reload")
			stub.loop.run(stub.loop.t + 1)
			t.eq(server.count("/hello"), 1, "connected with the convar key")
		end },

		{ "only superadmins and the console may use the command", function()
			boot('{"key":"' .. KEY .. '"}')
			local ply = stub.player({ steamId = "76561197960287930", name = "Gabe" })
			command(ply, "status")
			t.eq(ply.printed[1], "ticket_tool_link: superadmins only", "refused")
			ply.superadmin = true
			command(ply, "status")
			t.contains(table.concat(ply.printed, "\n"), "Ticket Tool Link 1.0.0-dev", "allowed")
		end },

		{ "join and leave hooks record players", function()
			boot('{"key":"' .. KEY .. '"}')
			local ply = stub.player({ steamId = "76561197960287930", name = "Gabe" })
			stub.fire("PlayerInitialSpawn", ply)
			local row = TicketToolLink.load("players").stored("76561197960287930")
			t.eq(row.name, "Gabe", "stored on join")
		end },

		{ "keep-awake can be turned off", function()
			boot('{"key":"' .. KEY .. '","keep-awake":false}')
			stub.fire("Think")
			t.eq(stub.convars.sv_hibernate_think.value, "0", "left alone")
		end },
	}
end
