#!/usr/bin/env bash
# Runs the end-to-end test: starts tests/e2e_server.py on a free port and
# points the addon at it. Usage: tests/e2e.sh [lua interpreter]
set -euo pipefail
cd "$(dirname "$0")/.."
lua="${1:-luajit}"

port_file="$(mktemp)"
python3 tests/e2e_server.py > "$port_file" &
server=$!
trap 'kill "$server" 2>/dev/null || true; rm -f "$port_file"' EXIT

for _ in $(seq 50); do
  [[ -s "$port_file" ]] && break
  sleep 0.1
done
port="$(head -n1 "$port_file")"
[[ -n "$port" ]] || { echo "e2e server didn't start" >&2; exit 1; }

TT_E2E_URL="http://127.0.0.1:$port" "$lua" tests/run.lua e2e
