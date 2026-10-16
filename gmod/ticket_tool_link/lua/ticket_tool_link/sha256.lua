-- SHA-256 and HMAC-SHA256. HMAC is always built here because GMod exposes no
-- HMAC; the hash itself comes from util.SHA256 when that passes a self-test
-- (it must handle embedded NUL bytes, which HMAC pads produce), and from the
-- pure-Lua code below otherwise.

local band, bor, bxor, bnot = bit.band, bit.bor, bit.bxor, bit.bnot
local rshift, lshift, ror, tobit = bit.rshift, bit.lshift, bit.ror, bit.tobit
local byte, char, format, rep = string.byte, string.char, string.format, string.rep
local floor = math.floor

local K = {
	0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
	0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
	0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
	0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
	0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
	0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
	0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
	0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
}
for i = 1, 64 do K[i] = tobit(K[i]) end

local H0 = { 0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19 }
for i = 1, 8 do H0[i] = tobit(H0[i]) end

local function pad(msg)
	local len = #msg
	local zeros = (55 - len) % 64
	local bits = len * 8
	local tail = {}
	for i = 7, 0, -1 do
		tail[#tail + 1] = char(floor(bits / 2 ^ (8 * i)) % 256)
	end
	return msg .. "\128" .. rep("\0", zeros) .. table.concat(tail)
end

local w = {}
local function block(h, msg, offset)
	for i = 1, 16 do
		local p = offset + (i - 1) * 4
		local b1, b2, b3, b4 = byte(msg, p + 1, p + 4)
		w[i] = bor(lshift(b1, 24), lshift(b2, 16), lshift(b3, 8), b4)
	end
	for i = 17, 64 do
		local x, y = w[i - 15], w[i - 2]
		local s0 = bxor(ror(x, 7), ror(x, 18), rshift(x, 3))
		local s1 = bxor(ror(y, 17), ror(y, 19), rshift(y, 10))
		w[i] = tobit(w[i - 16] + s0 + w[i - 7] + s1)
	end
	local a, b, c, d, e, f, g, hh = h[1], h[2], h[3], h[4], h[5], h[6], h[7], h[8]
	for i = 1, 64 do
		local S1 = bxor(ror(e, 6), ror(e, 11), ror(e, 25))
		local ch = bxor(band(e, f), band(bnot(e), g))
		local t1 = tobit(hh + S1 + ch + K[i] + w[i])
		local S0 = bxor(ror(a, 2), ror(a, 13), ror(a, 22))
		local maj = bxor(band(a, b), band(a, c), band(b, c))
		local t2 = tobit(S0 + maj)
		hh, g, f, e, d, c, b, a = g, f, e, tobit(d + t1), c, b, a, tobit(t1 + t2)
	end
	h[1], h[2], h[3], h[4] = tobit(h[1] + a), tobit(h[2] + b), tobit(h[3] + c), tobit(h[4] + d)
	h[5], h[6], h[7], h[8] = tobit(h[5] + e), tobit(h[6] + f), tobit(h[7] + g), tobit(h[8] + hh)
end

local M = {}

function M.pureDigest(msg)
	msg = pad(msg)
	local h = { H0[1], H0[2], H0[3], H0[4], H0[5], H0[6], H0[7], H0[8] }
	for offset = 0, #msg - 1, 64 do
		block(h, msg, offset)
	end
	local out = {}
	for i = 1, 8 do
		local v = h[i]
		out[i] = char(band(rshift(v, 24), 255), band(rshift(v, 16), 255), band(rshift(v, 8), 255), band(v, 255))
	end
	return table.concat(out)
end

function M.toHex(raw)
	return (raw:gsub(".", function(c) return format("%02x", byte(c)) end))
end

function M.fromHex(hex)
	return (hex:gsub("%x%x", function(pair) return char(tonumber(pair, 16)) end))
end

-- Wraps util.SHA256 so it returns raw bytes, or nil when it doesn't agree
-- with the pure code on inputs that matter for HMAC.
local function nativeDigest()
	if type(util) ~= "table" or type(util.SHA256) ~= "function" then return nil end
	local function wrap(msg)
		local out = util.SHA256(msg)
		if type(out) ~= "string" then return nil end
		if #out == 64 and out:match("^%x+$") then return M.fromHex(out:lower()) end
		if #out == 32 then return out end
		return nil
	end
	local samples = { "", "abc", "a\0b\0\255", rep("\54\0", 40) .. rep("x", 70) }
	for _, s in ipairs(samples) do
		local ok, got = pcall(wrap, s)
		if not ok or got ~= M.pureDigest(s) then return nil end
	end
	return wrap
end

local native = nativeDigest()
M.native = native ~= nil

function M.digest(msg)
	return (native or M.pureDigest)(msg)
end

function M.hex(msg)
	return M.toHex(M.digest(msg))
end

local function xorPad(key, value)
	local out = {}
	for i = 1, 64 do
		out[i] = char(bxor(byte(key, i), value))
	end
	return table.concat(out)
end

function M.hmac(key, msg)
	if #key > 64 then key = M.digest(key) end
	key = key .. rep("\0", 64 - #key)
	return M.digest(xorPad(key, 0x5c) .. M.digest(xorPad(key, 0x36) .. msg))
end

function M.hmacHex(key, msg)
	return M.toHex(M.hmac(key, msg))
end

return M
