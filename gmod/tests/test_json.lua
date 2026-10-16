return function(t)
	return {
		{ "encodes ordered objects byte for byte", function()
			local json = TicketToolLink.load("json")
			t.eq(json.encode(json.object()), "{}", "empty body")
			t.eq(json.encode({}), "{}", "empty plain table")
			t.eq(json.encode(json.object("protocol", 1, "plugin", json.object("type", "gmod", "version", "1.0.0"),
				"banProviders", json.array({ "ulx", "server" }))),
				'{"protocol":1,"plugin":{"type":"gmod","version":"1.0.0"},"banProviders":["ulx","server"]}', "hello")
			t.eq(json.encode(json.object("a", json.null, "b", nil, "c", false)), '{"a":null,"c":false}', "null and nil")
			t.eq(json.encode(json.array({})), "[]", "empty array")
			t.eq(json.encode({ b = 1, a = 2 }), '{"a":2,"b":1}', "plain tables sort keys")
		end },

		{ "numbers come out as plain integers", function()
			local json = TicketToolLink.load("json")
			t.eq(json.encode(1760529600), "1760529600", "timestamp")
			t.eq(json.encode(-3), "-3", "negative")
			t.eq(json.encode(0.5), "0.5", "fraction")
		end },

		{ "escapes strings and repairs bad UTF-8", function()
			local json = TicketToolLink.load("json")
			t.eq(json.encode('a"b\\c\n\t\1'), '"a\\"b\\\\c\\n\\t\\u0001"', "escapes")
			t.eq(json.encode("Jos\195\169 \240\159\152\128"), '"Jos\195\169 \240\159\152\128"', "valid UTF-8 kept")
			t.eq(json.encode("bad\255\192x"), '"bad??x"', "invalid bytes replaced")
			t.eq(json.encode("\237\160\128"), '"???"', "surrogate halves replaced")
		end },

		{ "decodes replies and keeps SteamID64 numbers exact", function()
			local json = TicketToolLink.load("json")
			local v = assert(json.decode('{"ok":true,"requests":[{"id":"x","player":{"steamId":"76561198000000000","n":76561198000000001,"name":null}}],"w":25,"f":1.5e2}'))
			t.eq(v.ok, true, "bool")
			t.eq(v.requests[1].player.steamId, "76561198000000000", "string id")
			t.eq(v.requests[1].player.n, "76561198000000001", "big integer kept as digits")
			t.eq(v.requests[1].player.name, json.null, "null")
			t.eq(v.w, 25, "small integer")
			t.eq(v.f, 150, "exponent")
		end },

		{ "decodes escapes and surrogate pairs", function()
			local json = TicketToolLink.load("json")
			t.eq(json.decode('"a\\u00e9\\ud83d\\ude00\\n\\/"'), "a\195\169\240\159\152\128\n/", "unicode")
		end },

		{ "rejects malformed JSON without throwing", function()
			local json = TicketToolLink.load("json")
			for _, bad in ipairs({ "", "{", '{"a":}', "[1,]", '{"a":1} x', "nul", '"open' }) do
				local v, err = json.decode(bad)
				t.ok(v == nil and type(err) == "string", "rejects " .. bad)
			end
		end },

		{ "round-trips", function()
			local json = TicketToolLink.load("json")
			local text = '{"a":[1,2,{"b":"c"}],"d":"e"}'
			t.eq(json.encode(json.decode(text)), text, "round trip")
		end },
	}
end
