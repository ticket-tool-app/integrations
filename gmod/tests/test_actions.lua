return function(t)
	local stub = t.stub
	local KEY = "ttp_0123456789abcdef.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
	local GABE = "76561197960287930"

	local function boot(cfg)
		os.execute("mkdir -p '" .. stub.dataDir .. "/ticket_tool_link'")
		local f = assert(io.open(stub.dataDir .. "/ticket_tool_link/config.json", "wb"))
		f:write('{"key":"' .. KEY .. '"' .. (cfg or "") .. "}")
		f:close()
		local server
		stub.http = function(req) return server.http(req) end
		dofile(stub.LUA .. "autorun/server/ticket_tool_link.lua")
		server = dofile(TESTS_ROOT .. "/tests/fake_server.lua")(stub)
		server.base = "https://dash.ticket-tool.app/api/plugin/v1"
		stub.fire("Think")
		stub.loop.run(stub.loop.t + 1)
		return server
	end

	local function run() stub.loop.run(stub.loop.t + 1) end

	local function say(ply, text)
		local out
		for _, fn in pairs(stub.hooks.PlayerSay or {}) do out = fn(ply, text) end
		return out
	end

	return {
		{ "hello lists only the actions and events turned on", function()
			local server = boot(',"actions":["kick","message","slay"],"reports":true')
			t.contains(server.last("/hello").raw, '"actions":["message","kick"],"events":["player.report"]', "hello")
			t.contains(stub.allOutput(), 'actions: "slay" isn\'t one of', "unknown kind explained")
		end },

		{ "everything is off by default", function()
			local server = boot()
			t.ok(not server.last("/hello").raw:find('"actions"', 1, true), "no actions")
			t.ok(not server.last("/hello").raw:find('"events"', 1, true), "no events")
			local item = server.action("kick", "Bye")
			run()
			t.eq(server.last("/respond").raw, '{"requestId":"' .. item.id .. '","ok":false,"error":"not_allowed"}', "refused")
		end },

		{ "messages, warns and kicks an online player", function()
			local server = boot(',"actions":"message,warn,kick"')
			local ply = stub.player({ steamId = GABE, name = "Gabe" })
			stub.players = { ply }
			server.action("message", "Hi")
			run()
			t.contains(server.last("/respond").raw, '"ok":true', "message done")
			t.eq(ply.printed[1], "[Staff] mod.alex: Hi", "chat line")
			server.action("kick", "Griefing")
			run()
			t.eq(ply.kicked, "Kicked by staff: Griefing", "kicked")
			stub.players = {}
			server.action("warn", "Stop")
			run()
			t.contains(server.last("/respond").raw, '"error":"player_offline"', "offline")
		end },

		{ "unban lifts a server-list ban and says not_banned otherwise", function()
			local server = boot(',"actions":["unban"],"ban-provider":"server"')
			server.action("unban", nil)
			run()
			t.contains(server.last("/respond").raw, '"error":"not_banned"', "not banned")
			stub.writeGameFile("cfg/banned_user.cfg", "banid 0 STEAM_0:0:11101\n")
			server.action("unban", nil)
			run()
			t.contains(server.last("/respond").raw, '"ok":true', "unbanned")
			local cmds = {}
			for _, c in ipairs(stub.consoleCommands) do cmds[#cmds + 1] = table.concat(c, " ") end
			t.contains(table.concat(cmds, "\n"), "removeid STEAM_0:0:11101\nwriteid", "removeid then writeid")
		end },

		{ "!report sends the reporter and a named target, with a cooldown", function()
			local server = boot(',"reports":true,"server-name":"TTT 1"')
			local ply = stub.player({ steamId = GABE, name = "Gabe" })
			local other = stub.player({ steamId = "76561198000000000", name = "Flyer" })
			stub.players = { ply, other }
			t.eq(say(ply, "!report flyer is flying"), "", "kept out of chat")
			run()
			t.eq(server.last("/event").raw,
				'{"type":"player.report","player":{"steamId":"' .. GABE .. '","name":"Gabe"},"message":"is flying",'
				.. '"target":{"steamId":"76561198000000000","name":"Flyer"},"server":"TTT 1"}', "event body")
			t.contains(table.concat(ply.printed, "\n"), "reached the staff team", "thanked")
			say(ply, "/report again")
			t.contains(ply.printed[#ply.printed], "You just sent a report", "cooldown")
			t.eq(server.count("/event"), 1, "one report")
			t.eq(say(ply, "hello everyone"), nil, "normal chat untouched")
		end },

		{ "!report explains when reports are off", function()
			local server = boot()
			local ply = stub.player({ steamId = GABE, name = "Gabe" })
			say(ply, "!report help")
			t.contains(ply.printed[1], "aren't turned on", "explained")
			t.eq(server.count("/event"), 0, "nothing sent")
		end },
	}
end
