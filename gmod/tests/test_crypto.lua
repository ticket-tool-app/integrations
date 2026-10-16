return function(t)
	local function vectors()
		local json = t.stub.json()
		return assert(json.decode(t.readFile(TESTS_ROOT .. "/../spec/test-vectors/v1.json")))
	end

	local function pyHash(data)
		local path = t.stub.tmp .. "/hash-input.bin"
		local f = assert(io.open(path, "wb"))
		f:write(data)
		f:close()
		local p = io.popen("python3 -c \"import hashlib,sys;print(hashlib.sha256(open(sys.argv[1],'rb').read()).hexdigest())\" '" .. path .. "'")
		local hex = p:read("*l")
		p:close()
		return hex
	end

	return {
		{ "SHA-256 known answers", function()
			local sha = TicketToolLink.load("sha256")
			t.eq(sha.hex(""), "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "empty")
			t.eq(sha.hex("abc"), "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "abc")
			t.eq(sha.hex("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq"),
				"248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1", "two blocks")
			t.eq(sha.hex(string.rep("a", 1000)), "41edece42d63e8d9bf515a9ba6932e1c20cbc9f5a5d134645adb5db1b9737ea3", "1000 a")
		end },

		{ "SHA-256 matches Python's hashlib across block boundaries", function()
			local sha = TicketToolLink.load("sha256")
			for _, n in ipairs({ 1, 54, 55, 56, 57, 63, 64, 65, 119, 120, 128, 300 }) do
				local parts = {}
				for i = 1, n do parts[i] = string.char((i * 37 + n) % 256) end
				local data = table.concat(parts)
				t.eq(sha.hex(data), pyHash(data), "length " .. n)
			end
		end },

		{ "HMAC-SHA256 RFC 4231 cases", function()
			local sha = TicketToolLink.load("sha256")
			t.eq(sha.hmacHex(string.rep("\11", 20), "Hi There"),
				"b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7", "case 1")
			t.eq(sha.hmacHex("Jefe", "what do ya want for nothing?"),
				"5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843", "case 2")
			t.eq(sha.hmacHex(string.rep("\170", 131), "Test Using Larger Than Block-Size Key - Hash Key First"),
				"60e431591ee0b67f0d8a26aacbf5b77f8e0bc6213728c5140546040f0ee37f54", "case 6 (long key)")
		end },

		{ "spec/test-vectors/v1.json", function()
			local v = vectors()
			local signer = TicketToolLink.load("signer")
			t.eq(signer.deriveSigningSecret(v.key), v.signingSecret, "signing secret")
			for _, b in ipairs(v.bodyHashes) do
				t.eq(signer.bodyHash(b.body), b.sha256, "body hash " .. b.body)
			end
			local key = assert(signer.parseKey(v.key))
			t.eq(key.keyId, v.keyId, "key id")
			for _, r in ipairs(v.requests) do
				t.eq(signer.canonical(r.method, r.endpoint, string.format("%.0f", r.timestamp), r.nonce, r.body),
					r.canonical, "canonical")
				t.eq(key.signRequest(r.method, r.endpoint, string.format("%.0f", r.timestamp), r.nonce, r.body),
					r.signature, "request signature")
			end
			for _, r in ipairs(v.replies) do
				t.ok(key.verifyReply(r.endpoint, string.format("%.0f", r.timestamp), r.nonce, r.body, r.signature),
					"reply signature verifies")
				t.ok(not key.verifyReply(r.endpoint, string.format("%.0f", r.timestamp), r.nonce, r.body .. " ", r.signature),
					"tampered body fails")
				t.ok(not key.verifyReply(r.endpoint, string.format("%.0f", r.timestamp + 1), r.nonce, r.body, r.signature),
					"other timestamp fails")
			end
		end },

		{ "util.SHA256 is used when it agrees, and its hex output is decoded", function()
			local calls = 0
			util.SHA256 = function(s)
				calls = calls + 1
				return pyHash(s)
			end
			local sha = TicketToolLink.load("sha256")
			t.ok(sha.native, "native hash picked")
			local v = vectors()
			local signer = TicketToolLink.load("signer")
			local before = calls
			t.eq(signer.deriveSigningSecret(v.key), v.signingSecret, "secret through util.SHA256")
			t.ok(calls > before, "util.SHA256 was called")
		end },

		{ "a util.SHA256 that mishandles NUL bytes is not used", function()
			util.SHA256 = function(s)
				return pyHash((s:gsub("%z.*$", "")))
			end
			local sha = TicketToolLink.load("sha256")
			t.ok(not sha.native, "falls back to pure Lua")
			t.eq(TicketToolLink.load("signer").deriveSigningSecret(vectors().key), vectors().signingSecret, "still right")
		end },

		{ "key parsing never echoes the key", function()
			local signer = TicketToolLink.load("signer")
			local bad = "ttp_0123456789abcdef.tooShortSecret"
			local key, err = signer.parseKey(bad)
			t.ok(key == nil, "rejected")
			t.ok(not err:find("tooShort", 1, true), "message doesn't repeat it")
			t.ok(signer.parseKey("ttp_0123456789ABCDEF.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA") == nil, "upper-case id")
			local good = assert(signer.parseKey("  ttp_0123456789abcdef.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\n"))
			t.eq(tostring(good), "ttp_0123456789abcdef", "tostring shows the id only")
			for k, v in pairs(good) do
				t.ok(type(v) ~= "string" or not v:find("AAAAAAAAAA", 1, true), "field " .. k .. " holds no secret")
				t.ok(type(v) ~= "string" or v ~= signer.deriveSigningSecret(
					"ttp_0123456789abcdef.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"), "field " .. k .. " isn't the secret")
			end
		end },

		{ "nonces are 22 base64url characters and unique", function()
			local random = TicketToolLink.load("random")
			local common = TicketToolLink.load("common")
			local seen = {}
			for _ = 1, 200 do
				local n = common.base64url(random.bytes(16))
				t.eq(#n, 22, "length")
				t.ok(n:match("^[A-Za-z0-9_%-]+$") ~= nil, "alphabet")
				t.ok(not seen[n], "unique")
				seen[n] = true
			end
			t.eq(common.base64url("\251\255"), "-_8", "url-safe alphabet, no padding")
		end },
	}
end
