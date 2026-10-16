-- Ban sources: ULX (ULib.bans), SAM, and the server's own list in
-- cfg/banned_user.cfg. Each lookup calls back with a ban table
-- { banned, provider, reason, createdAt, expiresAt, actor } (times in Unix
-- seconds, expiresAt nil = permanent) or nil and an error message.

local common = TicketToolLink.load("common")
local players = TicketToolLink.load("players")

local M = {}

M.ORDER = { "sam", "ulx", "server" }

local function reason(text)
	text = common.cleanText(text)
	if not text or text == "" then return nil end
	return common.clip(text, 2000)
end

local function notBanned(provider)
	return { banned = false, provider = provider }
end

-- ULX writes the issuing admin as "Name(STEAM_0:1:2)".
local function ulxActor(admin)
	if type(admin) ~= "string" then return nil end
	local name = common.cleanText(admin:gsub("%(STEAM_[%d:]+%)%s*$", ""))
	if name == "" or name == "(Console)" then return "Console" end
	return common.clip(name, 64)
end

local function epoch(v)
	v = tonumber(v)
	if v and v > 0 then return v end
	return nil
end

M.providers = {}

M.providers.ulx = {
	available = function()
		return type(ULib) == "table" and type(ULib.bans) == "table"
	end,
	lookup = function(sid64, now, cb)
		local sid32 = common.steamId32(sid64)
		local entry = ULib.bans[sid32] or ULib.bans[(sid32:gsub("^STEAM_0", "STEAM_1"))]
		if type(entry) ~= "table" then return cb(notBanned("ulx")) end
		local unban = tonumber(entry.unban) or 0
		if unban > 0 and unban <= now then return cb(notBanned("ulx")) end
		cb({
			banned = true, provider = "ulx", reason = reason(entry.reason),
			createdAt = epoch(entry.time), expiresAt = unban > 0 and unban or nil, actor = ulxActor(entry.admin),
		})
	end,
}

local function samActor(admin)
	if type(admin) ~= "string" or admin == "" then return nil end
	if not admin:match("^STEAM_") then return common.clip(common.cleanText(admin), 64) end
	local row = players.samRow(admin)
	if row and type(row.name) == "string" and row.name ~= "" then return common.clip(row.name, 64) end
	local id = common.accountIdFromText(admin)
	local stored = id and players.stored(common.fromAccountId(id))
	if stored and type(stored.name) == "string" and stored.name ~= "" then return common.clip(stored.name, 64) end
	return admin
end

local function samResult(row, now)
	if type(row) ~= "table" then return notBanned("sam") end
	local unban = tonumber(row.unban_date) or 0
	if unban > 0 and unban <= now then return notBanned("sam") end
	return {
		banned = true, provider = "sam", reason = reason(row.reason),
		createdAt = epoch(row.created_at), expiresAt = unban > 0 and unban or nil, actor = samActor(row.admin),
	}
end

M.providers.sam = {
	available = function()
		return type(sam) == "table" and type(sam.player) == "table"
	end,
	-- sam.player.is_banned goes through SAM's own SQL layer, so it works on
	-- MySQL too; the sv.db query is the fallback for builds without it.
	lookup = function(sid64, now, cb)
		local sid32 = common.steamId32(sid64)
		if type(sam.player.is_banned) == "function" then
			local ok, err = pcall(sam.player.is_banned, sid32, function(row)
				if type(row) == "table" and row[1] ~= nil and row.reason == nil then row = row[1] end
				cb(samResult(row, now))
			end)
			if ok then return end
			return cb(nil, "SAM ban check failed: " .. tostring(err))
		end
		if not sql.TableExists("sam_bans") then
			return cb(nil, "SAM stores its bans somewhere this addon can't read")
		end
		local rows = players.query("SELECT * FROM sam_bans WHERE steamid = " .. sql.SQLStr(sid32))
		cb(samResult(rows and rows[1], now))
	end,
}

-- Only permanent bans reach banned_user.cfg (writeid skips timed ones), and
-- the file holds no reason, date or admin.
function M.parseBannedUsers(text, accountId)
	if type(text) ~= "string" then return false end
	for line in text:gmatch("[^\r\n]+") do
		local id = line:match("^%s*banid%s+[%d%.]+%s+(%S+)")
		if id and common.accountIdFromText(id) == accountId then return true end
	end
	return false
end

M.providers.server = {
	available = function() return true end,
	lookup = function(sid64, now, cb)
		local id = common.accountId(sid64)
		local text = file.Read("cfg/banned_user.cfg", "GAME")
		if M.parseBannedUsers(text, id) then
			return cb({ banned = true, provider = "server" })
		end
		cb(notBanned("server"))
	end,
}

-- The admin mod's list first, then the server list, which ULX and SAM bans
-- don't always reach but a console banid does.
function M.resolve(setting)
	setting = common.trim(setting):lower()
	local warning
	if setting ~= "auto" and setting ~= "" then
		local p = M.providers[setting]
		if p and p.available() then
			return { setting }
		elseif p then
			return { "server" }, setting .. " isn't installed; using the server ban list"
		end
		warning = "Unknown ban-provider \"" .. setting .. "\"; using auto"
	end
	local chain = {}
	for _, name in ipairs(M.ORDER) do
		if M.providers[name].available() then chain[#chain + 1] = name end
	end
	if #chain > 2 then chain = { chain[1], "server" } end
	return chain, warning
end

function M.lookup(chain, sid64, now, cb)
	local primary
	local function at(i)
		local name = chain[i]
		if not name then return cb(primary or notBanned("server")) end
		local done = false
		local ok, err = pcall(M.providers[name].lookup, sid64, now, function(ban, lookupErr)
			if done then return end
			done = true
			if not ban then
				if i == 1 then return cb(nil, lookupErr) end
				return cb(primary)
			end
			primary = primary or ban
			if ban.banned then return cb(ban) end
			at(i + 1)
		end)
		if not ok and not done then
			done = true
			if i == 1 then return cb(nil, tostring(err)) end
			cb(primary)
		end
	end
	at(1)
end

return M
