-- A small JSON codec of our own instead of util.TableToJSON/JSONToTable:
-- the request signature covers the exact body bytes, so key order and number
-- formatting have to be ours, and the GMod decoder turns numeric strings and
-- big integers (SteamID64s) into lossy floats.

local M = {}

M.null = setmetatable({}, { __tostring = function() return "null" end })

local OBJECT = {}
local ARRAY = {}

-- Builds an object that encodes its keys in the order given:
-- json.object("a", 1, "b", json.null). Nil values are skipped.
function M.object(...)
	local keys, values = {}, {}
	local n = select("#", ...)
	for i = 1, n, 2 do
		local k, v = select(i, ...)
		if v ~= nil then
			keys[#keys + 1] = k
			values[k] = v
		end
	end
	return setmetatable({ keys = keys, values = values }, OBJECT)
end

function M.array(list)
	return setmetatable({ items = list or {} }, ARRAY)
end

local escapes = {
	['"'] = '\\"', ["\\"] = "\\\\", ["\b"] = "\\b", ["\f"] = "\\f",
	["\n"] = "\\n", ["\r"] = "\\r", ["\t"] = "\\t",
}

-- Replaces bytes that aren't valid UTF-8 with "?", so a mangled player name
-- can't make the server reject the whole body.
function M.sanitizeUtf8(s)
	local out, i, len = {}, 1, #s
	while i <= len do
		local c = s:byte(i)
		local need = c < 0x80 and 0 or (c >= 0xC2 and c <= 0xDF) and 1 or (c >= 0xE0 and c <= 0xEF) and 2
			or (c >= 0xF0 and c <= 0xF4) and 3 or -1
		local ok = need >= 0 and i + need <= len
		if ok then
			for j = 1, need do
				local cc = s:byte(i + j)
				if cc < 0x80 or cc > 0xBF then ok = false break end
			end
		end
		if ok and need == 2 then
			local c2 = s:byte(i + 1)
			if (c == 0xE0 and c2 < 0xA0) or (c == 0xED and c2 > 0x9F) then ok = false end
		elseif ok and need == 3 then
			local c2 = s:byte(i + 1)
			if (c == 0xF0 and c2 < 0x90) or (c == 0xF4 and c2 > 0x8F) then ok = false end
		end
		if ok then
			out[#out + 1] = s:sub(i, i + need)
			i = i + need + 1
		else
			out[#out + 1] = "?"
			i = i + 1
		end
	end
	return table.concat(out)
end

local function encodeString(s)
	s = M.sanitizeUtf8(s)
	return '"' .. s:gsub('[%c"\\]', function(c)
		return escapes[c] or string.format("\\u%04x", c:byte())
	end) .. '"'
end

local function encodeNumber(n)
	if n ~= n or n == math.huge or n == -math.huge then
		error("json: can't encode " .. tostring(n))
	end
	if n == math.floor(n) and math.abs(n) < 2 ^ 53 then
		return string.format("%.0f", n)
	end
	return string.format("%.14g", n)
end

local encode

local function isSequence(t)
	local n = 0
	for _ in pairs(t) do n = n + 1 end
	for i = 1, n do
		if t[i] == nil then return false end
	end
	return n > 0, n
end

encode = function(v, out)
	local tv = type(v)
	if v == nil or v == M.null then
		out[#out + 1] = "null"
	elseif tv == "boolean" then
		out[#out + 1] = v and "true" or "false"
	elseif tv == "number" then
		out[#out + 1] = encodeNumber(v)
	elseif tv == "string" then
		out[#out + 1] = encodeString(v)
	elseif tv == "table" then
		local mt = getmetatable(v)
		if mt == OBJECT then
			out[#out + 1] = "{"
			for i, k in ipairs(v.keys) do
				if i > 1 then out[#out + 1] = "," end
				out[#out + 1] = encodeString(k)
				out[#out + 1] = ":"
				encode(v.values[k], out)
			end
			out[#out + 1] = "}"
		elseif mt == ARRAY then
			out[#out + 1] = "["
			for i, item in ipairs(v.items) do
				if i > 1 then out[#out + 1] = "," end
				encode(item, out)
			end
			out[#out + 1] = "]"
		else
			local seq, n = isSequence(v)
			if seq then
				out[#out + 1] = "["
				for i = 1, n do
					if i > 1 then out[#out + 1] = "," end
					encode(v[i], out)
				end
				out[#out + 1] = "]"
			else
				local keys = {}
				for k in pairs(v) do
					if type(k) ~= "string" then error("json: object keys must be strings") end
					keys[#keys + 1] = k
				end
				table.sort(keys)
				out[#out + 1] = "{"
				for i, k in ipairs(keys) do
					if i > 1 then out[#out + 1] = "," end
					out[#out + 1] = encodeString(k)
					out[#out + 1] = ":"
					encode(v[k], out)
				end
				out[#out + 1] = "}"
			end
		end
	else
		error("json: can't encode a " .. tv)
	end
end

function M.encode(v)
	local out = {}
	encode(v, out)
	return table.concat(out)
end

-- Decoding. Objects and arrays come back as plain tables; null as json.null.
-- Integers too long for a double come back as their digit string.

local function decodeError(s, i, what)
	error({ json = true, message = string.format("json: %s at byte %d", what, i) }, 0)
end

local function skip(s, i)
	return (s:find("[^ \t\r\n]", i)) or #s + 1
end

local function utf8char(cp)
	if cp < 0x80 then return string.char(cp) end
	if cp < 0x800 then return string.char(0xC0 + math.floor(cp / 64), 0x80 + cp % 64) end
	if cp < 0x10000 then
		return string.char(0xE0 + math.floor(cp / 4096), 0x80 + math.floor(cp / 64) % 64, 0x80 + cp % 64)
	end
	return string.char(0xF0 + math.floor(cp / 262144), 0x80 + math.floor(cp / 4096) % 64,
		0x80 + math.floor(cp / 64) % 64, 0x80 + cp % 64)
end

local unescapes = { ['"'] = '"', ["\\"] = "\\", ["/"] = "/", b = "\b", f = "\f", n = "\n", r = "\r", t = "\t" }

local decodeValue

local function decodeString(s, i)
	local out = {}
	local j = i + 1
	while true do
		local k = s:find('["\\]', j)
		if not k then decodeError(s, i, "unterminated string") end
		out[#out + 1] = s:sub(j, k - 1)
		if s:sub(k, k) == '"' then
			return table.concat(out), k + 1
		end
		local e = s:sub(k + 1, k + 1)
		if e == "u" then
			local hex = s:sub(k + 2, k + 5)
			if not hex:match("^%x%x%x%x$") then decodeError(s, k, "bad \\u escape") end
			local cp = tonumber(hex, 16)
			local nextAt = k + 6
			if cp >= 0xD800 and cp <= 0xDBFF and s:sub(nextAt, nextAt + 1) == "\\u" then
				local lo = tonumber(s:sub(nextAt + 2, nextAt + 5), 16)
				if lo and lo >= 0xDC00 and lo <= 0xDFFF then
					cp = 0x10000 + (cp - 0xD800) * 0x400 + (lo - 0xDC00)
					nextAt = nextAt + 6
				end
			end
			out[#out + 1] = utf8char(cp)
			j = nextAt
		elseif unescapes[e] then
			out[#out + 1] = unescapes[e]
			j = k + 2
		else
			decodeError(s, k, "bad escape")
		end
	end
end

local function decodeNumber(s, i)
	local text = s:match("^-?%d+%.?%d*[eE]?[-+]?%d*", i)
	if not text or text == "" or text == "-" then decodeError(s, i, "bad number") end
	local n = tonumber(text)
	if not n then decodeError(s, i, "bad number") end
	if text:match("^-?%d+$") and #text:gsub("^-", "") > 15 then
		return text, i + #text
	end
	return n, i + #text
end

decodeValue = function(s, i, depth)
	if depth > 64 then decodeError(s, i, "nested too deep") end
	i = skip(s, i)
	local c = s:sub(i, i)
	if c == "{" then
		local obj = {}
		i = skip(s, i + 1)
		if s:sub(i, i) == "}" then return obj, i + 1 end
		while true do
			if s:sub(i, i) ~= '"' then decodeError(s, i, "expected a key") end
			local key
			key, i = decodeString(s, i)
			i = skip(s, i)
			if s:sub(i, i) ~= ":" then decodeError(s, i, "expected ':'") end
			local val
			val, i = decodeValue(s, i + 1, depth + 1)
			obj[key] = val
			i = skip(s, i)
			local d = s:sub(i, i)
			if d == "}" then return obj, i + 1 end
			if d ~= "," then decodeError(s, i, "expected ',' or '}'") end
			i = skip(s, i + 1)
		end
	elseif c == "[" then
		local arr = {}
		i = skip(s, i + 1)
		if s:sub(i, i) == "]" then return arr, i + 1 end
		while true do
			local val
			val, i = decodeValue(s, i, depth + 1)
			arr[#arr + 1] = val
			i = skip(s, i)
			local d = s:sub(i, i)
			if d == "]" then return arr, i + 1 end
			if d ~= "," then decodeError(s, i, "expected ',' or ']'") end
			i = i + 1
		end
	elseif c == '"' then
		return decodeString(s, i)
	elseif c == "t" and s:sub(i, i + 3) == "true" then
		return true, i + 4
	elseif c == "f" and s:sub(i, i + 4) == "false" then
		return false, i + 5
	elseif c == "n" and s:sub(i, i + 3) == "null" then
		return M.null, i + 4
	elseif c:match("[-%d]") then
		return decodeNumber(s, i)
	end
	decodeError(s, i, "unexpected character")
end

-- Returns the value, or nil and a message.
function M.decode(s)
	if type(s) ~= "string" then return nil, "json: not a string" end
	local ok, value, i = pcall(decodeValue, s, 1, 0)
	if not ok then
		return nil, type(value) == "table" and value.message or tostring(value)
	end
	if skip(s, i) <= #s then return nil, "json: trailing data" end
	return value
end

return M
