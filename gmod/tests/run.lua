-- Test runner: `luajit tests/run.lua` or `lua5.1 tests/run.lua` from gmod/.
-- Optional arguments pick test files by name, e.g. `crypto client`.

TESTS_ROOT = (arg and arg[0] or ""):match("^(.*)/tests/run%.lua$") or "."
local stub = dofile(TESTS_ROOT .. "/tests/gmod_stub.lua")

local out = io.write
local t = { stub = stub }

function t.eq(got, want, label)
	if got ~= want then
		error(string.format("%s\n  want: %s\n   got: %s", label or "not equal", tostring(want), tostring(got)), 2)
	end
end

function t.ok(cond, label)
	if not cond then error(label or "expected true", 2) end
end

function t.contains(haystack, needle, label)
	if not tostring(haystack):find(needle, 1, true) then
		error(string.format("%s\n  %q not found in:\n  %s", label or "missing text", needle, tostring(haystack)), 2)
	end
end

function t.readFile(path)
	local f = assert(io.open(path, "rb"))
	local text = f:read("*a")
	f:close()
	return text
end

-- Fresh stub globals and module cache for each test.
function t.fresh()
	stub.loop.reset()
	stub.install()
	return TicketToolLink
end

local files = { "crypto", "json", "common", "identity", "client", "main", "e2e" }
local pick = {}
for i = 1, #(arg or {}) do pick[arg[i]] = true end

local passed, failed = 0, 0
for _, name in ipairs(files) do
	if next(pick) == nil or pick[name] then
		local cases = dofile(TESTS_ROOT .. "/tests/test_" .. name .. ".lua")(t)
		for _, case in ipairs(cases) do
			t.fresh()
			local ok, err = xpcall(case[2], debug.traceback)
			if ok then
				passed = passed + 1
				out("ok   ", name, ": ", case[1], "\n")
			else
				failed = failed + 1
				out("FAIL ", name, ": ", case[1], "\n", tostring(err), "\n")
			end
		end
	end
end

out(string.format("\n%d passed, %d failed (%s)\n", passed, failed, jit and jit.version or _VERSION))
os.exit(failed == 0 and 0 or 1)
