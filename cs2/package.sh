#!/usr/bin/env bash
# Builds the CounterStrikeSharp plugin and zips it in the layout servers unpack
# into the game directory: addons/counterstrikesharp/plugins/TicketToolLink/.
#
#   cs2/package.sh [version]   ->  cs2/dist/TicketToolLink-cs2-<version>.zip
set -euo pipefail

version="${1:-0.0.0-dev}"
if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$ ]]; then
  echo "Version $version is not MAJOR.MINOR.PATCH[-suffix]" >&2
  exit 1
fi

here="$(cd "$(dirname "$0")" && pwd)"
out="$here/bin/package"
stage="$here/dist/stage"
plugin_dir="$stage/addons/counterstrikesharp/plugins/TicketToolLink"

rm -rf "$out" "$stage"
dotnet build "$here/src/TicketToolLink/TicketToolLink.csproj" -c Release -p:Version="$version" -o "$out" --nologo

mkdir -p "$plugin_dir"
# Only what the server loads. CounterStrikeSharp provides its own API and
# Microsoft.Extensions assemblies; second copies would split their types.
for f in TicketToolLink.dll TicketTool.PluginSdk.dll MySqlConnector.dll; do
  cp "$out/$f" "$plugin_dir/"
done

zip_name="TicketToolLink-cs2-$version.zip"
rm -f "$here/dist/$zip_name"
(cd "$stage" && zip -qrX "$here/dist/$zip_name" addons)
rm -rf "$stage"
echo "Wrote cs2/dist/$zip_name"
