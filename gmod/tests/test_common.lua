return function(t)
	return {
		{ "SteamID conversions use exact digits", function()
			local c = TicketToolLink.load("common")
			t.eq(c.steamId32("76561197960287930"), "STEAM_0:0:11101", "Gabe")
			t.eq(c.steamId32("76561197960265729"), "STEAM_0:1:0", "first odd id")
			t.eq(c.steamId32("76561198000000000"), "STEAM_0:0:19867136", "brief example")
			t.eq(c.fromAccountId(22202), "76561197960287930", "back to 64")
			t.eq(c.accountIdFromText("STEAM_1:0:11101"), 22202, "universe 1")
			t.eq(c.accountIdFromText("[U:1:22202]"), 22202, "steam3")
			t.eq(c.accountIdFromText("garbage"), nil, "garbage")
			t.ok(c.isSteamId64("76561198000000000"), "valid")
			t.ok(not c.isSteamId64("7656119800000000"), "16 digits")
			t.ok(not c.isSteamId64("76561190000000000"), "below the base")
			t.ok(not c.isSteamId64(76561198000000000), "a number, not a string")
		end },

		{ "ISO 8601 times", function()
			local c = TicketToolLink.load("common")
			t.eq(c.parseIso("1970-01-01T00:00:00Z"), 0, "epoch")
			t.eq(c.parseIso("2026-10-15T12:00:04.000Z"), 1792065604, "request expiry")
			t.eq(c.parseIso("2026-10-15T12:00:04.250Z"), 1792065604.25, "fraction")
			t.eq(c.parseIso("2026-10-15T14:00:04+02:00"), 1792065604, "offset")
			t.eq(c.parseIso("2000-02-29T00:00:00Z"), 951782400, "leap day")
			t.eq(c.parseIso("yesterday"), nil, "garbage")
			t.eq(c.iso(1792065604), "2026-10-15T12:00:04Z", "format")
		end },

		{ "clipping counts characters, not bytes", function()
			local c = TicketToolLink.load("common")
			t.eq(c.clip("h\195\169llo", 2), "h\195\169", "two characters")
			t.eq(c.clip("abc", 5), "abc", "short")
			t.eq(c.cleanText("  a\n\tb\1 "), "a b", "whitespace and control")
		end },

		{ "durations", function()
			local c = TicketToolLink.load("common")
			t.eq(c.duration(30), "under a minute", "seconds")
			t.eq(c.duration(7 * 60), "7m", "minutes")
			t.eq(c.duration(3 * 3600 + 5 * 60), "3h 5m", "hours")
			t.eq(c.duration(2 * 86400 + 4 * 3600), "2d 4h", "days")
		end },
	}
end
