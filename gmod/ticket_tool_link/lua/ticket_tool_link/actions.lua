-- Carries out player.action requests on this server, and the in-game report
-- chat command. The client has already refused any action the owner left off.

local common = TicketToolLink.load("common")
local bans = TicketToolLink.load("bans")

local M = {}

local function online(steamId)
	local ply = player.GetBySteamID64(steamId)
	if ply and IsValid(ply) then return ply end
	return nil
end

-- Lifts the ban in the source that reported it. ULX and SAM are called
-- through their own functions so their logs and caches stay right.
local UNBAN = {
	ulx = function(sid32)
		if type(ULib.unban) ~= "function" then return false end
		ULib.unban(sid32)
		return true
	end,
	sam = function(sid32)
		if type(sam.player.unban) ~= "function" then return false end
		sam.player.unban(sid32)
		return true
	end,
	server = function(sid32)
		RunConsoleCommand("removeid", sid32)
		RunConsoleCommand("writeid")
		return true
	end,
}

-- deps: banChain(), log
function M.newHandler(deps)
	return function(action, reply)
		if action.kind == "unban" then
			return bans.lookup(deps.banChain(), action.steamId, os.time(), function(ban, err)
				if err then return reply({ ok = false, error = "error", message = tostring(err) }) end
				if not ban or not ban.banned then return reply({ ok = false, error = "not_banned" }) end
				local unban = UNBAN[ban.provider]
				local sid32 = common.steamId32(action.steamId)
				if not unban or not sid32 or not unban(sid32) then
					return reply({ ok = false, error = "unsupported",
						message = "This addon can't lift " .. tostring(ban.provider) .. " bans" })
				end
				deps.log.info(action.actorName .. " (Ticket Tool) unbanned " .. sid32 .. " through " .. ban.provider)
				reply({ ok = true })
			end)
		end
		local ply = online(action.steamId)
		if not ply then return reply({ ok = false, error = "player_offline" }) end
		local message = action.message or ""
		if action.kind == "message" then
			ply:ChatPrint("[Staff] " .. action.actorName .. ": " .. message)
		elseif action.kind == "warn" then
			ply:ChatPrint("Warning from staff: " .. message)
			ply:PrintMessage(HUD_PRINTCENTER, "Warning from staff: " .. message)
		elseif action.kind == "kick" then
			ply:Kick(message ~= "" and ("Kicked by staff: " .. message) or "Kicked by staff")
		else
			return reply({ ok = false, error = "unsupported" })
		end
		deps.log.info(action.actorName .. " (Ticket Tool) sent a " .. action.kind .. " to " .. ply:Nick())
		reply({ ok = true })
	end
end

local REPLIES = {
	opened = "Thanks! Your report reached the staff team.",
	ignored = "The staff team isn't taking reports this way right now. Try their Discord server instead.",
	rate_limited = "Lots of reports are coming in right now. Try again in a minute.",
	not_enabled = "Reports aren't turned on for this server.",
	failed = "Your report couldn't be sent. Try again in a minute.",
}

-- The player on this server with exactly that name, ignoring case.
local function byName(name)
	name = name:lower()
	for _, p in ipairs(player.GetHumans()) do
		if p:Nick():lower() == name then return p end
	end
	return nil
end

--[[ Handles "!report [player] <message>" and "/report ..." typed in chat. The
     reporter is always the player who typed it. Returns true when the text
     was the command, so the caller can keep it out of chat.
     deps: settings(), client(), serverName(), log, cooldowns (steamId -> time) ]]
function M.onSay(deps, ply, text)
	local s = deps.settings()
	local word, rest = common.trim(text):match("^[!/](%S+)%s*(.*)$")
	if not word or word:lower() ~= s.reportCommand then return false end
	local function tell(line) ply:ChatPrint("[Ticket Tool] " .. line) end
	local c = deps.client()
	if not s.reports or not c then
		tell(REPLIES.not_enabled)
		return true
	end
	local first, after = rest:match("^(%S+)%s+(.+)$")
	local target = first and byName(first) or nil
	local message = common.trim(target and after or rest)
	if message == "" then
		tell("Type !" .. s.reportCommand .. " <message>, or !" .. s.reportCommand
			.. " <player> <message> to report someone.")
		return true
	end
	local sid = ply:SteamID64()
	local now = os.time()
	local last = deps.cooldowns[sid]
	if last and now - last < s.reportCooldown then
		tell("You just sent a report. You can send another in " .. (s.reportCooldown - (now - last)) .. "s.")
		return true
	end
	deps.cooldowns[sid] = now
	tell("Sending your report...")
	local name = ply:Nick()
	c:report({ steamId = sid, name = name }, message,
		target and { steamId = target:SteamID64(), name = target:Nick() } or nil, deps.serverName(),
		function(result)
			if result.outcome == "failed" then
				deps.cooldowns[sid] = nil
				if result.error == "plan_not_entitled" then
					deps.log.warn("Reports need the Community or Pro plan on the Discord server.")
				elseif result.error ~= "not_running" then
					deps.log.warn("Sending " .. name .. "'s report failed: " .. tostring(result.error))
				end
			end
			if IsValid(ply) then tell(REPLIES[result.outcome] or REPLIES.failed) end
		end)
	return true
end

return M
