#!/usr/bin/env bash
# Packages the addon as build/ticket_tool_link-gmod-<version>.zip, with the
# version stamped into lua/ticket_tool_link/version.lua.
# Usage: ./build.sh [version]   (default 1.0.0-dev)
set -euo pipefail
cd "$(dirname "$0")"
version="${1:-1.0.0-dev}"
if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$ ]]; then
  echo "Version $version is not MAJOR.MINOR.PATCH[-suffix]" >&2
  exit 1
fi

rm -rf build
mkdir -p build
cp -R ticket_tool_link build/ticket_tool_link
cp ../LICENSE build/ticket_tool_link/LICENSE.txt
cat > build/ticket_tool_link/lua/ticket_tool_link/version.lua <<LUA
return "$version"
LUA

zip_name="ticket_tool_link-gmod-$version.zip"
(
  cd build
  find ticket_tool_link -exec touch -t 202601010000 {} +
  find ticket_tool_link -type f | LC_ALL=C sort | zip -X -q "$zip_name" -@
  sha256sum "$zip_name" > "$zip_name.sha256"
)
echo "build/$zip_name"
