-- Small helpers shared by the other modules: UTF-8 clipping, ISO 8601 times,
-- base64url, and SteamID conversions done on digit strings because a
-- SteamID64 doesn't fit in a double.

local M = {}

-- Clips to at most `max` UTF-8 characters.
function M.clip(s, max)
	if s == nil then return nil end
	s = tostring(s)
	local count, i, len = 0, 1, #s
	while i <= len do
		count = count + 1
		if count > max then return s:sub(1, i - 1) end
		local c = s:byte(i)
		i = i + (c >= 0xF0 and 4 or c >= 0xE0 and 3 or c >= 0xC0 and 2 or 1)
	end
	return s
end

function M.trim(s)
	return (tostring(s or ""):gsub("^%s+", ""):gsub("%s+$", ""))
end

-- Removes control characters and collapses whitespace.
function M.cleanText(s)
	if s == nil then return nil end
	s = tostring(s):gsub("[%z\1-\8\11\12\14-\31\127]", ""):gsub("%s+", " ")
	return M.trim(s)
end

local function daysFromCivil(y, m, d)
	y = m <= 2 and y - 1 or y
	local era = math.floor(y / 400)
	local yoe = y - era * 400
	local mp = (m + 9) % 12
	local doy = math.floor((153 * mp + 2) / 5) + d - 1
	local doe = yoe * 365 + math.floor(yoe / 4) - math.floor(yoe / 100) + doy
	return era * 146097 + doe - 719468
end

-- Parses an ISO 8601 UTC or offset time into Unix seconds (with fraction).
function M.parseIso(s)
	if type(s) ~= "string" then return nil end
	local y, mo, d, h, mi, sec, rest = s:match("^(%d%d%d%d)-(%d%d)-(%d%d)[Tt ](%d%d):(%d%d):(%d%d)(.*)$")
	if not y then return nil end
	local frac = 0
	local f, tz = rest:match("^%.(%d+)(.*)$")
	if f then
		frac = tonumber("0." .. f)
		rest = tz
	end
	local offset = 0
	if rest == "Z" or rest == "z" then
		offset = 0
	else
		local sign, oh, om = rest:match("^([+-])(%d%d):?(%d%d)$")
		if not sign then return nil end
		offset = (tonumber(oh) * 3600 + tonumber(om) * 60) * (sign == "-" and -1 or 1)
	end
	local days = daysFromCivil(tonumber(y), tonumber(mo), tonumber(d))
	return days * 86400 + tonumber(h) * 3600 + tonumber(mi) * 60 + tonumber(sec) + frac - offset
end

function M.iso(t)
	if t == nil then return nil end
	return os.date("!%Y-%m-%dT%H:%M:%SZ", math.floor(t))
end

local B64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"

function M.base64url(raw)
	local out = {}
	for i = 1, #raw, 3 do
		local a, b, c = raw:byte(i, i + 2)
		local n = a * 65536 + (b or 0) * 256 + (c or 0)
		local chars = 2 + (b and 1 or 0) + (c and 1 or 0)
		for j = 1, chars do
			local idx = math.floor(n / 2 ^ (6 * (4 - j))) % 64
			out[#out + 1] = B64:sub(idx + 1, idx + 1)
		end
	end
	return table.concat(out)
end

-- SteamIDs. All arithmetic on decimal strings so nothing passes through a double.

local STEAM64_BASE = "76561197960265728"

local function subDigits(a, b)
	-- a - b for non-negative decimal strings with a >= b.
	local out, borrow = {}, 0
	local la, lb = #a, #b
	for i = 0, la - 1 do
		local da = a:byte(la - i) - 48
		local db = i < lb and (b:byte(lb - i) - 48) or 0
		local d = da - db - borrow
		if d < 0 then d = d + 10 borrow = 1 else borrow = 0 end
		out[la - i] = d
	end
	local s = table.concat(out):gsub("^0+", "")
	return s == "" and "0" or s
end

local function addDigits(a, b)
	local out, carry = {}, 0
	local la, lb = #a, #b
	local n = math.max(la, lb)
	for i = 0, n - 1 do
		local da = i < la and (a:byte(la - i) - 48) or 0
		local db = i < lb and (b:byte(lb - i) - 48) or 0
		local d = da + db + carry
		carry = d >= 10 and 1 or 0
		out[n - i] = d % 10
	end
	local s = table.concat(out)
	if carry > 0 then s = "1" .. s end
	return s
end

function M.isSteamId64(s)
	return type(s) == "string" and s:match("^7656119%d%d%d%d%d%d%d%d%d%d$") ~= nil and s >= STEAM64_BASE
end

-- The 32-bit account id, as a number.
function M.accountId(s64)
	if not M.isSteamId64(s64) then return nil end
	local n = tonumber(subDigits(s64, STEAM64_BASE))
	if not n or n > 4294967295 then return nil end
	return n
end

-- STEAM_0:X:Y, the form ULX, SAM and banned_user.cfg use.
function M.steamId32(s64)
	local id = M.accountId(s64)
	if not id then return nil end
	return string.format("STEAM_0:%d:%d", id % 2, math.floor(id / 2))
end

function M.fromAccountId(id)
	return addDigits(STEAM64_BASE, string.format("%.0f", id))
end

-- Account id from STEAM_X:Y:Z or [U:1:N]; nil for anything else.
function M.accountIdFromText(s)
	if type(s) ~= "string" then return nil end
	local y, z = s:match("^STEAM_%d:([01]):(%d+)$")
	if y then return tonumber(z) * 2 + tonumber(y) end
	local n = s:match("^%[U:1:(%d+)%]$")
	if n then return tonumber(n) end
	return nil
end

-- "3d 4h", "12h 5m", "7m" from seconds.
function M.duration(seconds)
	seconds = math.floor(tonumber(seconds) or 0)
	if seconds < 60 then return "under a minute" end
	local d = math.floor(seconds / 86400)
	local h = math.floor(seconds % 86400 / 3600)
	local m = math.floor(seconds % 3600 / 60)
	if d > 0 then return string.format("%dd %dh", d, h) end
	if h > 0 then return string.format("%dh %dm", h, m) end
	return string.format("%dm", m)
end

return M
