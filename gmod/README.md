# Ticket Tool Link (Garry's Mod)

Connects a Garry's Mod server to one Discord server's [Ticket Tool](https://ticket-tool.app). When someone opens a ticket, staff see the in-game name of the Steam account they linked to Discord, whether they're online, when they were last seen and first joined, their rank and playtime, plus any active ban.

- **Servers:** any Garry's Mod dedicated or listen server. Pure Lua: no binary modules to install.
- **Ban sources:** ULX, SAM, or the server's own ban list (`cfg/banned_user.cfg`).
- **Ranks and playtime:** usergroups from ULX, SAM or any admin mod that sets them; playtime from UTime or SAM.
- **Plans:** game server plugins are included with Ticket Tool's Community and Pro plans.
- **Network:** outbound HTTPS only. No port to open, nothing to change on your firewall.

Download the latest `ticket_tool_link-gmod-<version>.zip` from the [Garry's Mod releases](https://github.com/ticket-tool-app/integrations/releases?q=gmod). Each release has a `.sha256` checksum next to it.

## Setup

1. On the Ticket Tool dashboard, open **Automations → Connections → Game server plugins** and click **Create a Garry's Mod key**. Name it after the server (for example "DarkRP") and copy the key. It's only shown once.
2. Unzip the release into `garrysmod/addons/`, so you have `garrysmod/addons/ticket_tool_link/lua/...`, and start the server once. This creates `garrysmod/data/ticket_tool_link/config.json`.
3. Paste the key into `config.json`:

   ```json
   { "key": "ttp_0123456789abcdef.your-key-here" }
   ```

   Or put it in `server.cfg` instead: `ticket_tool_key "ttp_0123456789abcdef.your-key-here"`.

4. Run `ticket_tool_link reload` in the server console. Within a few seconds the key shows **Connected now** on the Connections page, and `ticket_tool_link status` says "Connected to Ticket Tool."
5. In Ticket Tool, edit a ticket category and switch on **Garry's Mod** in its **Lookups** card, its **Ban lookup** card, or both.

Players need a Steam account linked to their Discord account and shared with your Discord server. There's no in-game link command: Steam accounts are linked on the member's **Linked accounts** page.

## How players are matched

Ticket Tool sends the SteamID64 of the Steam account the ticket opener linked. The addon looks for that player online (`Player:SteamID64()`), then in its own record of everyone who has joined since it was installed (the `ticket_tool_link_players` table in `sv.db`: name, first and last seen, usergroup). SAM's player history and UTime's records fill in players from before the addon was installed. A player found in none of these and with no ban is reported as never having joined.

## Commands

| Command | Who | What it does |
| --- | --- | --- |
| `ticket_tool_link status` | server console, RCON, superadmins | Connection state, key id, Discord server, ban sources, last problem |
| `ticket_tool_link reload` | server console, RCON, superadmins | Re-reads the settings and reconnects |

## Configuration

`garrysmod/data/ticket_tool_link/config.json`, created on first start:

```json
{
  "key": "",
  "server-name": "",
  "ban-provider": "auto",
  "api-url": "https://dash.ticket-tool.app",
  "keep-awake": true,
  "debug": false
}
```

| Field | Convar | Default | What it does |
| --- | --- | --- | --- |
| `key` | `ticket_tool_key` | | The full key from the dashboard |
| `server-name` | `ticket_tool_server_name` | the key's name | Server name staff see on the ticket card |
| `ban-provider` | `ticket_tool_ban_provider` | `auto` | `auto`, `ulx`, `sam` or `server` |
| `api-url` | `ticket_tool_api_url` | `https://dash.ticket-tool.app` | Only change this for testing |
| `keep-awake` | `ticket_tool_keep_awake` | `true` | Sets `sv_hibernate_think 1` (see below) |
| `debug` | `ticket_tool_debug` | `false` | More detail in the console |

A convar that is set wins over `config.json`, so use one or the other. `ticket_tool_key` is a protected convar: its value is never sent to players or shown by the console. Run `ticket_tool_link reload` after changing either.

### Empty servers

An empty dedicated server hibernates, and while it does no Lua runs, so it can't answer lookups. Tickets are often about players who aren't online, so by default the addon sets `sv_hibernate_think 1` to keep the server awake. Set `keep-awake` to `false` if you'd rather it hibernate; lookups then only work while someone is playing.

## Ban sources

| `ban-provider` | Reads | Notes |
| --- | --- | --- |
| `ulx` | `ULib.bans` | Reason, start, expiry and the admin's name |
| `sam` | `sam.player.is_banned`, or the `sam_bans` table in `sv.db` | Reason, expiry and the admin's name |
| `server` | `cfg/banned_user.cfg` | Permanent `banid` bans only: timed bans aren't written to that file, and it holds no reason or admin |

With `auto`, the addon uses SAM if it's installed, otherwise ULX, and in both cases also checks `banned_user.cfg`, which catches bans issued with the console's `banid`. Without either admin mod it uses the server list alone.

## What the addon sends

Only what the [protocol](../spec/PROTOCOL.md) defines: for a lookup, the player's SteamID64, name, online status, last-seen and first-join times and the server name, plus up to three card fields (rank, playtime and the `STEAM_0:` SteamID); for bans, whether there's an active ban, its reason (at most 2,000 characters), start, expiry and who issued it. No IP addresses, chat or inventories. Ban reasons appear on the private staff card in Ticket Tool, so write them with that in mind.

## Limits

- Everything runs on the server's main Lua thread, but the work is small: one SQLite read or two per lookup, and HTTP requests that never block a tick.
- Each lookup is answered within about 3 seconds. A SAM install that keeps its data on MySQL answers through SAM's own API; if that is slow, staff see "took too long".
- When the server shuts down the addon tells Ticket Tool it's leaving, but a process that exits at once may not finish that request. Ticket Tool then shows the server as unavailable within 40 seconds.
- `HTTP()` refuses private addresses, so pointing `api-url` at a test server on your own machine needs the `-allowlocalhttp` launch option.

## Troubleshooting

| `ticket_tool_link status` says | Do this |
| --- | --- |
| No key set | Put the key in `config.json` or `ticket_tool_key` and run `ticket_tool_link reload` |
| Ticket Tool doesn't recognize this key | The key was rotated, revoked or mistyped. Copy the current key from the dashboard |
| Key was made for a different game | Create a Garry's Mod key; a Minecraft or FiveM key won't work here |
| The Discord server's plan doesn't include game lookups | Upgrade to Community or Pro; the addon checks again every hour |
| Three polls are already open | The same key is in use on another server. Give each server its own key |
| Can't reach Ticket Tool | Check the server can make outbound HTTPS requests to `dash.ticket-tool.app` |

Set `debug` to `true` for more detail in the console. The key is never written to the console or the logs; only its public id (`ttp_0123456789abcdef`) is.

## Development

The addon is plain Lua 5.1 plus GMod's `bit` library. The tests run outside the game with a small stub of the GMod API (`tests/gmod_stub.lua`); they need `luajit` or `lua5.1`, `python3` (for SQLite) and `curl` (for the end-to-end test):

```sh
cd gmod
luajit tests/run.lua         # unit tests, including spec/test-vectors/v1.json
lua5.1 tests/run.lua         # the same on stock Lua, with a pure-Lua bit library
tests/e2e.sh luajit          # against a signing-enforcing fake Ticket Tool
./build.sh 1.0.0             # build/ticket_tool_link-gmod-1.0.0.zip
```

Push a tag `gmod-vMAJOR.MINOR.PATCH` on `main` to publish a release. A tag with a suffix such as `gmod-v1.1.0-beta.1` publishes as a pre-release.
