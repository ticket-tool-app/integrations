-- The LuaJIT `bit` library in plain Lua 5.1, for running the tests on a
-- stock interpreter. Results are signed 32-bit, as LuaJIT returns them, so a
-- bug that only shows with negative values shows here too.

local floor = math.floor
local TWO32, TWO31 = 2 ^ 32, 2 ^ 31

local function u(x) return x % TWO32 end
local function s(x)
	x = floor(x) % TWO32
	if x >= TWO31 then return x - TWO32 end
	return x
end

local AND, OR, XOR = {}, {}, {}
for a = 0, 15 do
	AND[a], OR[a], XOR[a] = {}, {}, {}
	for b = 0, 15 do
		local ra, ro, rx = 0, 0, 0
		for i = 0, 3 do
			local p = 2 ^ i
			local ba, bb = floor(a / p) % 2, floor(b / p) % 2
			if ba == 1 and bb == 1 then ra = ra + p end
			if ba == 1 or bb == 1 then ro = ro + p end
			if ba ~= bb then rx = rx + p end
		end
		AND[a][b], OR[a][b], XOR[a][b] = ra, ro, rx
	end
end

local function op2(t, a, b)
	a, b = u(a), u(b)
	local r, p = 0, 1
	for _ = 1, 8 do
		r = r + t[a % 16][b % 16] * p
		a, b, p = floor(a / 16), floor(b / 16), p * 16
	end
	return r
end

local function fold(t)
	return function(a, ...)
		local r = u(a)
		for i = 1, select("#", ...) do r = op2(t, r, select(i, ...)) end
		return s(r)
	end
end

local bit = {}
bit.tobit = s
bit.band = fold(AND)
bit.bor = fold(OR)
bit.bxor = fold(XOR)
function bit.bnot(a) return s(TWO32 - 1 - u(a)) end
function bit.lshift(a, n) return s(u(a) * 2 ^ (n % 32)) end
function bit.rshift(a, n) return s(floor(u(a) / 2 ^ (n % 32))) end
function bit.arshift(a, n) return s(floor(s(a) / 2 ^ (n % 32))) end
function bit.ror(a, n)
	n = n % 32
	a = u(a)
	return s(floor(a / 2 ^ n) + (a % 2 ^ n) * 2 ^ (32 - n))
end
function bit.rol(a, n) return bit.ror(a, 32 - n % 32) end
function bit.tohex(a, n)
	n = n or 8
	return string.format("%0" .. n .. "x", u(a) % 2 ^ (4 * n))
end

return bit
