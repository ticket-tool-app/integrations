# Ticket Tool Link (Counter-Strike 2)

Connects a Counter-Strike 2 server to one Discord server's [Ticket Tool](https://ticket-tool.app). When someone opens a ticket, staff see their CS2 name, SteamID, whether they're online, when they were last seen and when they first joined, and their admin flags, plus any active ban.

- **Servers:** CS2 with [CounterStrikeSharp](https://docs.cssharp.dev) (Metamod:Source). Built against the CounterStrikeSharp API for .NET 8; it also loads on the current .NET 10 builds.
- **Bans:** [CS2-SimpleAdmin](https://github.com/daffyyyy/CS2-SimpleAdmin) when it's installed with a MySQL database, and the server's own ban list (`banned_user.cfg`).
- **Players:** matched by the member's Steam account linked on Ticket Tool and shared with the Discord server.
- **Plans:** game server plugins are included with Ticket Tool's Community and Pro plans.
- **Network:** outbound HTTPS only. No port to open, nothing to change on your firewall.

## Setup

1. On the Ticket Tool dashboard, open **Automations → Connections → Game server plugins** and click **Create a Counter-Strike 2 key**. Name it after the server (for example "Retakes") and copy the key. It's only shown once.
2. Download `TicketToolLink-cs2-<version>.zip` from the [releases page](https://github.com/ticket-tool-app/integrations/releases?q=cs2) and unzip it into the server's `game/csgo` directory. It contains `addons/counterstrikesharp/plugins/TicketToolLink/`.
3. Start the server (or run `css_plugins load TicketToolLink`). This creates `addons/counterstrikesharp/configs/plugins/TicketToolLink/TicketToolLink.json`.
4. Paste the key into that file:

   ```json
   { "key": "ttp_0123456789abcdef.your-key-here" }
   ```

5. Run `css_ticketlink reload` in the server console. Within a few seconds the key shows **Connected now** on the Connections page, and `css_ticketlink status` says "Connected to Ticket Tool."
6. In Ticket Tool, edit a ticket category and switch on **Counter-Strike 2** in its **Lookups** card, its **Ban lookup** card, or both.

Players need their Steam account linked on their Ticket Tool **Linked accounts** page and shared with your Discord server. There's no in-game linking command for CS2.

## Commands and permissions

| Command | Permission | What it does |
| --- | --- | --- |
| `css_ticketlink status` | `@css/root` | Connection state, key id, Discord server, ban sources, last problem |
| `css_ticketlink reload` | `@css/root` | Re-reads `TicketToolLink.json` and reconnects |

Both work from the server console and RCON, and in game as `!ticketlink` for root admins.

When `reports` is on, every player can use `!report [player] <message>` (or `/report`; the name is set by `report-command`).

## Configuration

```json
{
  "key": "",
  "api-url": "https://dash.ticket-tool.app",
  "server-name": "",
  "simpleadmin": "auto",
  "actions": [],
  "reports": false,
  "report-command": "report",
  "report-cooldown-seconds": 60,
  "debug": false,
  "ConfigVersion": 1
}
```

| Key | What it does |
| --- | --- |
| `key` | The full key from the dashboard |
| `api-url` | Leave as is; for testing against a staging server |
| `server-name` | The server name staff see on the card; empty uses the key's name |
| `simpleadmin` | `auto` reads CS2-SimpleAdmin's bans when it's installed; `off` never does |
| `actions` | Staff actions to allow: any of `message`, `warn`, `kick`, `unban` |
| `reports` | `true` turns on the report command |
| `report-command` | Chat command name for reports; a change takes effect when the plugin reloads |
| `report-cooldown-seconds` | Seconds between reports per player |
| `debug` | More detail in the console. The key is never written to the log |

## Staff actions and in-game reports

Staff can act on a player from Discord: from **Run an app action** on a ticket, a flow, or an automation rule. Every action is off until you turn it on, and Ticket Tool only offers the ones you did. Actions and reports need the Pro plan.

| Action | What happens in game |
| --- | --- |
| `message` | The player gets a chat message from the staff member |
| `warn` | The player gets a warning in chat and in the middle of the screen |
| `kick` | The player is kicked with the reason staff typed |
| `unban` | The ban is lifted where it was found: `css_unban` for CS2-SimpleAdmin, or `removeid` and `writeid` for `banned_user.cfg` |

With `reports` on, `!report [player] <message>` sends the report to Ticket Tool, where an automation rule with the trigger **When the game server sends a report** can open a ticket. The first word counts as the reported player only when it's the name of someone online, and each player can send one report every `report-cooldown-seconds`.

## Ban sources

1. **CS2-SimpleAdmin.** The plugin reads the database settings from `configs/plugins/CS2-SimpleAdmin/CS2-SimpleAdmin.json` and runs one read-only query per lookup against its `sa_bans` table (active bans for the player's SteamID), with a 3-second timeout and at most two pooled connections. It reports the newest active ban's reason, admin, start and end, and how many bans are active. Only MySQL/MariaDB is supported; with SimpleAdmin's SQLite storage, this source is skipped. SimpleAdmin stores times in the game server's local time zone, which is how they are read. This plugin never writes to the database.
2. **`banned_user.cfg`.** Permanent bans written by `writeid` (for example after `banid 0 <steamid>`), in SteamID64, `STEAM_X:Y:Z` or `[U:1:N]` form. Bans made with `banid` but not written out live only in the server's memory, which plugins can't read. This file has no reason, issuer or date.

A SimpleAdmin ban wins; otherwise a `banned_user.cfg` ban is reported. If SimpleAdmin's database can't be reached, the lookup falls back to `banned_user.cfg` and the console says so once.

## What the plugin sends

Only what the [protocol](../spec/PROTOCOL.md) defines: for a lookup, the player's SteamID, name, online status, last-seen and first-join times, the `server-name`, and an **Admin** line with the player's CounterStrikeSharp admin flags (for example `@css/ban, @css/kick`) when they have any. For bans, the fields above. No IP addresses, chat or inventories. Ban reasons appear on the private staff card in Ticket Tool, so write them with that in mind.

First-join and last-seen times are recorded by the plugin itself in `plugins/TicketToolLink/seen.json` from when it's installed, because CS2 keeps no such record.

## Threading

Network calls, the SimpleAdmin query and file reads run on background threads, so the plugin never blocks a server frame. Player and admin state is read on the game thread through `Server.NextFrame`. The only blocking call is on unload or reload, which waits at most about 3 seconds to tell Ticket Tool the plugin is going offline.

## Troubleshooting

| `css_ticketlink status` says | Do this |
| --- | --- |
| No key set | Paste the key into `TicketToolLink.json` and run `css_ticketlink reload` |
| Ticket Tool doesn't recognize this key | The key was rotated, revoked or mistyped. Copy the current key from the dashboard |
| Paused: the Discord server's plan doesn't include game lookups | Upgrade to Community or Pro; the plugin checks again every hour |
| Three polls are already open | The same key is in use on another server. Give each server its own key |
| Can't reach Ticket Tool | Check the server can make outbound HTTPS requests to `dash.ticket-tool.app` |

## Building

```bash
dotnet test cs2/tests/TicketToolLink.Tests
cs2/package.sh 1.0.0                 # writes cs2/dist/TicketToolLink-cs2-1.0.0.zip
```

The zip holds only what the server loads: `TicketToolLink.dll`, `TicketTool.PluginSdk.dll` and `MySqlConnector.dll`. The tests cover the seen store and both ban sources; the plugin entry point needs CounterStrikeSharp's native runtime, so test a release on a real server too.

## Not supported in version 1

- Other admin plugins' ban storage (for example SourceBans-style web panels).
- In-game account linking. Players link Steam on Ticket Tool instead.
