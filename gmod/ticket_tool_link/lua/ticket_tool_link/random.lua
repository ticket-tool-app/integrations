-- Nonce bytes. GMod gives Lua no OS random source, so this is a SHA-256
-- hash chain seeded from every bit of local entropy on hand (clocks, table
-- addresses, math.random) and stirred again on each call. Nonces only need
-- to be unique and hard to guess, which a counter inside the hash guarantees
-- for the life of the process; the wall clock in the seed covers restarts.

local sha = TicketToolLink.load("sha256")

local M = {}

local function sample()
	local parts = {
		tostring(os.time()), tostring(os.clock()), tostring({}), tostring(function() end),
		tostring(math.random()), tostring(math.random(0, 2147483646)),
	}
	if type(SysTime) == "function" then parts[#parts + 1] = string.format("%.9f", SysTime()) end
	if type(RealTime) == "function" then parts[#parts + 1] = string.format("%.9f", RealTime()) end
	if type(engine) == "table" and type(engine.TickCount) == "function" then
		parts[#parts + 1] = tostring(engine.TickCount())
	end
	return table.concat(parts, "|")
end

local state = sha.digest("ticket-tool nonce seed|" .. sample() .. "|" .. tostring(collectgarbage("count")))
local counter = 0

function M.bytes(n)
	local out = {}
	local have = 0
	while have < n do
		counter = counter + 1
		local block = sha.digest(state .. "|out|" .. counter .. "|" .. sample())
		state = sha.digest(state .. "|next|" .. block)
		out[#out + 1] = block
		have = have + #block
	end
	return table.concat(out):sub(1, n)
end

-- Folds outside data (e.g. reply bodies) into the state; never reduces entropy.
function M.stir(data)
	state = sha.digest(state .. "|stir|" .. tostring(data))
end

return M
