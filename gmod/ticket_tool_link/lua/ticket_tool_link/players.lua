-- Who a SteamID64 is on this server: the online player when there is one,
-- otherwise what this addon recorded when they last played (sv.db), plus
-- rank and playtime from ULX, SAM or UTime when those are installed.

local common = TicketToolLink.load("common")

local M = {}

M.TABLE = "ticket_tool_link_players"

local function q(text)
	return sql.SQLStr(text)
end

-- sql.Query returns nil for "no rows" and false for an error.
function M.query(text)
	local rows = sql.Query(text)
	if rows == false then
		return nil, sql.LastError()
	end
	return rows
end

local function first(text)
	local rows = M.query(text)
	return rows and rows[1] or nil
end

function M.ensureTable()
	local _, err = M.query("CREATE TABLE IF NOT EXISTS " .. M.TABLE .. " ("
		.. "steamid64 TEXT PRIMARY KEY NOT NULL, name TEXT, first_seen INTEGER, last_seen INTEGER, usergroup TEXT)")
	return err == nil, err
end

function M.record(ply, now)
	if not IsValid(ply) or ply:IsBot() then return end
	local sid = ply:SteamID64()
	if not common.isSteamId64(sid) then return end
	local t = string.format("%.0f", math.floor(now))
	local name = common.clip(ply:Nick(), 64)
	local group = ply:GetUserGroup()
	M.query("INSERT OR IGNORE INTO " .. M.TABLE .. " (steamid64, name, first_seen, last_seen, usergroup) VALUES ("
		.. q(sid) .. ", " .. q(name) .. ", " .. t .. ", " .. t .. ", " .. q(group) .. ")")
	M.query("UPDATE " .. M.TABLE .. " SET name = " .. q(name) .. ", last_seen = " .. t
		.. ", usergroup = " .. q(group) .. " WHERE steamid64 = " .. q(sid))
end

function M.stored(sid)
	return first("SELECT name, first_seen, last_seen, usergroup FROM " .. M.TABLE
		.. " WHERE steamid64 = " .. q(sid))
end

function M.findOnline(sid)
	for _, ply in ipairs(player.GetHumans()) do
		if IsValid(ply) and ply:SteamID64() == sid then return ply end
	end
	return nil
end

-- SAM keeps its own player history; read it straight from sv.db when SAM
-- stores there (it can also be on MySQL, which this doesn't reach).
function M.samRow(sid32)
	if not sam or not sql.TableExists("sam_players") then return nil end
	return first("SELECT * FROM sam_players WHERE steamid = " .. q(sid32))
end

-- UTime keys rows by Player:UniqueID(), which is a CRC of the SteamID.
function M.utimeRow(sid32)
	if not sql.TableExists("utime") or not util.CRC then return nil end
	local uid = util.CRC("gm_" .. sid32 .. "_gm")
	return first("SELECT totaltime, lastvisit FROM utime WHERE player = " .. q(uid))
end

function M.ulxGroup(sid32)
	local users = ULib and ULib.ucl and ULib.ucl.users
	local entry = type(users) == "table" and users[sid32]
	return type(entry) == "table" and type(entry.group) == "string" and entry.group or nil
end

local function num(v)
	v = tonumber(v)
	if v and v > 0 then return v end
	return nil
end

local function str(v)
	-- GMod's sql.Query returns SQL NULL as the string "NULL".
	if type(v) ~= "string" or v == "NULL" then return nil end
	v = common.cleanText(v)
	return v ~= "" and v or nil
end

--[[ Returns nil when this server has never seen the player, otherwise
     { steamId, steamId32, name, online, lastSeen, firstSeen, rank, playtime }
     with times as Unix seconds. ]]
function M.read(sid, requestedName, now)
	local sid32 = common.steamId32(sid)
	if not sid32 then return nil end
	local ply = M.findOnline(sid)
	local row = M.stored(sid)
	local samInfo = M.samRow(sid32)
	local ut = M.utimeRow(sid32)
	if not ply and not row and not samInfo and not ut then return nil end

	local info = { steamId = sid, steamId32 = sid32, online = ply ~= nil }
	if ply then
		info.name = ply:Nick()
		info.rank = ply:GetUserGroup()
		info.lastSeen = now
		if ply.GetUTimeTotalTime then
			info.playtime = tonumber(ply:GetUTimeTotalTime())
		end
	end
	info.name = str(info.name) or str(row and row.name) or str(samInfo and samInfo.name) or str(requestedName) or sid
	info.rank = str(info.rank) or M.ulxGroup(sid32) or str(samInfo and samInfo.rank) or str(row and row.usergroup)
	info.firstSeen = num(row and row.first_seen) or num(samInfo and samInfo.first_join)
	info.lastSeen = info.lastSeen or num(row and row.last_seen) or num(samInfo and samInfo.last_join) or num(ut and ut.lastvisit)
	info.playtime = info.playtime or num(ut and ut.totaltime) or num(samInfo and samInfo.play_time)
	if info.firstSeen and info.lastSeen and info.firstSeen > info.lastSeen then info.firstSeen = info.lastSeen end
	return info
end

return M
