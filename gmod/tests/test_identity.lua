return function(t)
	local stub = t.stub
	local SID = "76561197960287930" -- STEAM_0:0:11101
	local OTHER = "76561198000000000"
	local NOW = 1792065600

	local function setup()
		local players = TicketToolLink.load("players")
		assert(players.ensureTable())
		return players
	end

	local function request(fields)
		local r = { id = "r1", steamId = SID, name = "gaben", wantsProfile = true, wantsBans = true }
		for k, v in pairs(fields or {}) do r[k] = v end
		return r
	end

	local function answer(handlerDeps, req)
		local lookup = TicketToolLink.load("lookup")
		local deps = {
			banChain = function() return TicketToolLink.load("bans").resolve("auto") end,
			serverName = function() return "DarkRP #1" end,
			now = function() return NOW end,
		}
		for k, v in pairs(handlerDeps or {}) do deps[k] = v end
		local got
		lookup.newHandler(deps)(req, function(result) got = result end)
		return got
	end

	local function encode(v) return TicketToolLink.load("json").encode(v) end

	return {
		{ "online player is matched by SteamID64 and reported in full", function()
			setup()
			stub.players = {
				stub.player({ steamId = OTHER, name = "Someone" }),
				stub.player({ steamId = SID, name = "Gabe", group = "superadmin", utime = 3 * 3600 + 120 }),
			}
			local res = answer(nil, request({ wantsBans = false }))
			t.eq(res.ok, true, "ok")
			t.eq(encode(res.profile),
				'{"steamId":"76561197960287930","name":"Gabe","online":true,"lastSeen":"2026-10-15T12:00:00Z",'
				.. '"firstSeen":null,"server":"DarkRP #1","fields":[{"label":"Rank","value":"superadmin"},'
				.. '{"label":"Playtime","value":"3h 2m"},{"label":"SteamID","value":"STEAM_0:0:11101"}]}', "profile")
			t.eq(res.bans, nil, "bans not asked for")
		end },

		{ "offline player comes from the sv.db store written on join and leave", function()
			local players = setup()
			local ply = stub.player({ steamId = SID, name = "Gabe", group = "admin" })
			players.record(ply, NOW - 7200)
			ply.nick = "Gabe N"
			players.record(ply, NOW - 3600)
			players.record(stub.player({ steamId = "90071996842377216", name = "Bot01", bot = true }), NOW)
			local row = players.stored(SID)
			t.eq(row.name, "Gabe N", "name updated")
			t.eq(row.first_seen, tostring(NOW - 7200), "first seen kept")
			t.eq(row.last_seen, tostring(NOW - 3600), "last seen moved")
			t.eq(sql.Query("SELECT COUNT(*) AS n FROM ticket_tool_link_players")[1].n, "1", "bots aren't stored")

			local res = answer(nil, request({ wantsBans = false }))
			t.eq(encode(res.profile),
				'{"steamId":"76561197960287930","name":"Gabe N","online":false,"lastSeen":"2026-10-15T11:00:00Z",'
				.. '"firstSeen":"2026-10-15T10:00:00Z","server":"DarkRP #1","fields":[{"label":"Rank","value":"admin"},'
				.. '{"label":"SteamID","value":"STEAM_0:0:11101"}]}', "profile")
		end },

		{ "names with quotes don't break the store", function()
			local players = setup()
			players.record(stub.player({ steamId = SID, name = "Robert'); DROP TABLE x;--" }), NOW)
			t.eq(players.stored(SID).name, "Robert'); DROP TABLE x;--", "stored verbatim")
		end },

		{ "a player this server never saw is unknown_player", function()
			setup()
			local res = answer(nil, request())
			t.eq(res.ok, false, "not ok")
			t.eq(res.error, "unknown_player", "error")
		end },

		{ "UTime and ULX fill in offline playtime and rank", function()
			setup()
			sql.Query("CREATE TABLE utime (id INTEGER PRIMARY KEY, player INTEGER NOT NULL, totaltime INTEGER NOT NULL, lastvisit INTEGER NOT NULL)")
			local uid = util.CRC("gm_STEAM_0:0:11101_gm")
			sql.Query("INSERT INTO utime (player, totaltime, lastvisit) VALUES (" .. uid .. ", 90000, " .. (NOW - 60) .. ")")
			ULib = { bans = {}, ucl = { users = { ["STEAM_0:0:11101"] = { group = "moderator" } } } }
			local res = answer(nil, request({ name = "gaben" }))
			t.eq(res.ok, true, "known through UTime")
			t.eq(encode(res.profile),
				'{"steamId":"76561197960287930","name":"gaben","online":false,"lastSeen":"2026-10-15T11:59:00Z",'
				.. '"firstSeen":null,"server":"DarkRP #1","fields":[{"label":"Rank","value":"moderator"},'
				.. '{"label":"Playtime","value":"1d 1h"},{"label":"SteamID","value":"STEAM_0:0:11101"}]}', "profile")
			t.eq(encode(res.bans), '{"banned":false,"provider":"ulx","reason":null,"createdAt":null,"expiresAt":null,"actor":null}', "bans")
		end },

		{ "ULX bans: active, permanent and expired", function()
			setup()
			ULib = { bans = { ["STEAM_0:0:11101"] = {
				reason = "Prop \n spam", admin = "ModAlex(STEAM_0:1:42)", time = NOW - 86400, unban = NOW + 3600,
			} } }
			local res = answer(nil, request({ wantsProfile = false }))
			t.eq(res.ok, true, "banned and never joined still answers")
			t.eq(res.profile, nil, "no profile")
			t.eq(encode(res.bans), '{"banned":true,"provider":"ulx","reason":"Prop spam","createdAt":"2026-10-14T12:00:00Z",'
				.. '"expiresAt":"2026-10-15T13:00:00Z","actor":"ModAlex"}', "timed ban")

			ULib.bans["STEAM_0:0:11101"] = { reason = "", admin = "(Console)", time = "1792000000", unban = "0" }
			res = answer(nil, request({ wantsProfile = false }))
			t.eq(encode(res.bans), '{"banned":true,"provider":"ulx","reason":null,"createdAt":"2026-10-14T17:46:40Z",'
				.. '"expiresAt":null,"actor":"Console"}', "permanent, values as strings")

			ULib.bans["STEAM_0:0:11101"].unban = NOW - 1
			res = answer(nil, request({ wantsProfile = false }))
			t.eq(res.error, "unknown_player", "expired ban and never joined")
		end },

		{ "SAM bans through sam.player.is_banned", function()
			setup()
			local asked
			sam = { player = { is_banned = function(steamid, cb)
				asked = steamid
				cb({ reason = "Cheating", admin = "STEAM_0:1:42", unban_date = "0" })
			end } }
			stub.players = { stub.player({ steamId = SID, name = "Gabe" }) }
			TicketToolLink.load("players").record(stub.player({ steamId = "76561197960265813", name = "Admin Ann" }), NOW)
			local res = answer(nil, request())
			t.eq(asked, "STEAM_0:0:11101", "asked SAM with the SteamID")
			t.eq(encode(res.bans), '{"banned":true,"provider":"sam","reason":"Cheating","createdAt":null,"expiresAt":null,'
				.. '"actor":"Admin Ann"}', "admin name resolved from the store")
		end },

		{ "SAM bans and history from sv.db when there's no API", function()
			setup()
			sam = { player = {} }
			sql.Query("CREATE TABLE sam_bans (id INTEGER PRIMARY KEY, steamid TEXT, reason TEXT, admin TEXT, unban_date INTEGER)")
			sql.Query("CREATE TABLE sam_players (id INTEGER PRIMARY KEY, steamid TEXT, name TEXT, rank TEXT, expiry_date INTEGER, first_join INTEGER, last_join INTEGER, play_time INTEGER)")
			sql.Query("INSERT INTO sam_bans (steamid, reason, admin, unban_date) VALUES ('STEAM_0:0:11101', 'RDM', 'Console', " .. (NOW + 60) .. ")")
			sql.Query("INSERT INTO sam_players (steamid, name, rank, first_join, last_join, play_time) VALUES ('STEAM_0:0:11101', 'Gabe', 'vip', 1700000000, 1792000000, 7200)")
			local res = answer(nil, request())
			t.eq(encode(res.bans), '{"banned":true,"provider":"sam","reason":"RDM","createdAt":null,"expiresAt":"2026-10-15T12:01:00Z",'
				.. '"actor":"Console"}', "bans")
			t.eq(encode(res.profile), '{"steamId":"76561197960287930","name":"Gabe","online":false,"lastSeen":"2026-10-14T17:46:40Z",'
				.. '"firstSeen":"2023-11-14T22:13:20Z","server":"DarkRP #1","fields":[{"label":"Rank","value":"vip"},'
				.. '{"label":"Playtime","value":"2h 0m"},{"label":"SteamID","value":"STEAM_0:0:11101"}]}', "profile from sam_players")
		end },

		{ "banned_user.cfg is read for the server provider", function()
			setup()
			local bans = TicketToolLink.load("bans")
			stub.writeGameFile("cfg/banned_user.cfg", "banid 0 STEAM_0:1:5\r\nbanid 0.000000 STEAM_0:0:11101\nbanid 0 [U:1:7]\n")
			t.ok(bans.parseBannedUsers("banid 0 [U:1:22202]", 22202), "steam3 form")
			t.ok(not bans.parseBannedUsers("banid 0 STEAM_0:0:111010", 22202), "no prefix match")
			local res = answer(nil, request({ wantsProfile = false }))
			t.eq(encode(res.bans), '{"banned":true,"provider":"server","reason":null,"createdAt":null,"expiresAt":null,"actor":null}', "server ban")
		end },

		{ "an admin mod that says not banned still defers to banned_user.cfg", function()
			setup()
			ULib = { bans = {} }
			stub.writeGameFile("cfg/banned_user.cfg", "banid 0 STEAM_0:0:11101\n")
			local res = answer(nil, request({ wantsProfile = false }))
			t.eq(res.bans.values.provider, "server", "server list caught it")
			t.eq(res.bans.values.banned, true, "banned")
		end },

		{ "ban source resolution", function()
			local bans = TicketToolLink.load("bans")
			local chain = bans.resolve("auto")
			t.eq(table.concat(chain, ","), "server", "nothing installed")
			ULib = { bans = {} }
			sam = { player = {} }
			t.eq(table.concat(bans.resolve("auto"), ","), "sam,server", "SAM wins over ULX")
			t.eq(table.concat(bans.resolve("ulx"), ","), "ulx", "explicit")
			sam = nil
			local c, warn = bans.resolve("sam")
			t.eq(table.concat(c, ","), "server", "missing explicit falls back")
			t.ok(warn ~= nil, "with a warning")
			c, warn = bans.resolve("litebans")
			t.eq(table.concat(c, ","), "ulx,server", "unknown means auto")
			t.ok(warn ~= nil, "with a warning")
		end },

		{ "a failing ban source answers error, not a crash", function()
			setup()
			sam = { player = { is_banned = function() error("mysql went away") end } }
			stub.players = { stub.player({ steamId = SID, name = "Gabe" }) }
			local res = answer(nil, request())
			t.eq(res.ok, false, "not ok")
			t.eq(res.error, "error", "error code")
			t.contains(res.message, "SAM ban check failed", "message")
		end },
	}
end
