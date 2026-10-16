-- Integration keys and request/reply signing (protocol v1). A parsed key
-- keeps its signing secret in closures only, so PrintTable or a debug dump of
-- the addon's tables can never show it.

local sha = TicketToolLink.load("sha256")

local M = {}

M.SIGNING_LABEL = "ticket-tool plugin signing v1"

function M.deriveSigningSecret(fullKey)
	return sha.hmacHex(fullKey, M.SIGNING_LABEL)
end

function M.bodyHash(body)
	return sha.hex(body)
end

function M.canonical(method, endpoint, ts, nonce, body)
	return table.concat({ "v1", method, endpoint, tostring(ts), nonce, M.bodyHash(body) }, "\n")
end

function M.signWith(secretHex, method, endpoint, ts, nonce, body)
	return "v1=" .. sha.hmacHex(secretHex, M.canonical(method, endpoint, ts, nonce, body))
end

-- Compares without returning early, so timing doesn't reveal how much matched.
function M.constantEquals(a, b)
	if type(a) ~= "string" or type(b) ~= "string" or #a ~= #b then return false end
	local diff = 0
	for i = 1, #a do
		diff = bit.bor(diff, bit.bxor(a:byte(i), b:byte(i)))
	end
	return diff == 0
end

function M.looksLikeKey(text)
	return type(text) == "string" and text:match("^ttp_[0-9a-f]+%.[A-Za-z0-9_%-]+$") ~= nil
		and #text:match("^ttp_([0-9a-f]+)%.") == 16 and #text:match("%.(.+)$") == 43
end

-- Returns a key object, or nil and a message that never repeats the text.
function M.parseKey(text)
	if type(text) ~= "string" or M.trimmed(text) == "" then
		return nil, "No integration key set"
	end
	local trimmed = M.trimmed(text)
	if not M.looksLikeKey(trimmed) then
		return nil, "That isn't an integration key. Keys look like ttp_0123456789abcdef.<43 characters>"
	end
	local keyId = trimmed:sub(5, 20)
	local secret = M.deriveSigningSecret(trimmed)
	local fingerprint = sha.hex("fingerprint\n" .. secret)
	local key = { keyId = keyId, display = "ttp_" .. keyId, fingerprint = fingerprint }

	function key.signRequest(method, endpoint, ts, nonce, body)
		return M.signWith(secret, method, endpoint, ts, nonce, body)
	end

	function key.verifyReply(endpoint, ts, nonce, body, signature)
		return M.constantEquals(M.signWith(secret, "RESPONSE", endpoint, ts, nonce, body), signature)
	end

	return setmetatable(key, { __tostring = function() return "ttp_" .. keyId end })
end

function M.trimmed(s)
	return (s:gsub("^%s+", ""):gsub("%s+$", ""))
end

return M
