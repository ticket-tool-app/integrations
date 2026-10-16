-- Answers player.lookup: the profile from this server, bans from the
-- configured ban sources. "Never joined and not banned" is unknown_player,
-- so the profile is read even when only bans were asked for.

local json = TicketToolLink.load("json")
local common = TicketToolLink.load("common")
local players = TicketToolLink.load("players")
local bans = TicketToolLink.load("bans")

local M = {}

local function orNull(v)
	if v == nil then return json.null end
	return v
end

function M.profileJson(info, serverName)
	local fields = {}
	local function add(label, value)
		if value == nil or value == "" or #fields >= 6 then return end
		fields[#fields + 1] = json.object("label", common.clip(label, 32), "value", common.clip(value, 100))
	end
	add("Rank", info.rank)
	if info.playtime then add("Playtime", common.duration(info.playtime)) end
	add("SteamID", info.steamId32)
	return json.object(
		"steamId", info.steamId,
		"name", common.clip(info.name, 64),
		"online", info.online,
		"lastSeen", orNull(common.iso(info.lastSeen)),
		"firstSeen", orNull(common.iso(info.firstSeen)),
		"server", orNull(serverName and common.clip(serverName, 64) or nil),
		"fields", #fields > 0 and json.array(fields) or nil
	)
end

function M.bansJson(ban)
	local banned = ban.banned == true
	return json.object(
		"banned", banned,
		"provider", ban.provider,
		"reason", orNull(banned and ban.reason or nil),
		"createdAt", orNull(banned and common.iso(ban.createdAt) or nil),
		"expiresAt", orNull(banned and common.iso(ban.expiresAt) or nil),
		"actor", orNull(banned and ban.actor or nil)
	)
end

-- deps: banChain() -> provider names, serverName() -> string|nil, now() -> seconds
function M.newHandler(deps)
	return function(request, reply)
		local now = deps.now()
		local info = players.read(request.steamId, request.name, now)
		local function finish(ban, err)
			if err then
				return reply({ ok = false, error = "error", message = tostring(err) })
			end
			local banned = ban ~= nil and ban.banned == true
			if not info and not banned then
				return reply({ ok = false, error = "unknown_player" })
			end
			reply({
				ok = true,
				profile = request.wantsProfile and info and M.profileJson(info, deps.serverName()) or nil,
				bans = ban and M.bansJson(ban) or nil,
			})
		end
		if request.wantsBans then
			bans.lookup(deps.banChain(), request.steamId, now, finish)
		else
			finish(nil)
		end
	end
end

return M
