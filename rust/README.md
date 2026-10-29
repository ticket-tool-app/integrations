# Ticket Tool Link (Rust)

Connects a Rust server to one Discord server's [Ticket Tool](https://ticket-tool.app). When someone opens a ticket, staff see their Rust name, SteamID, whether they're online, when they were last seen and when they first joined, their playtime and clan when those plugins are installed, plus any active ban.

- **Servers:** Rust with [Oxide/uMod](https://umod.org) or [Carbon](https://carbonmod.gg). It's a single `.cs` file, like any other Oxide plugin.
- **Bans:** the server's own ban list (`banid`, F1 `ban`, and any plugin that bans through it).
- **Players:** matched by the member's Steam account linked on Ticket Tool and shared with the Discord server.
- **Plans:** game server plugins are included with Ticket Tool's Community and Pro plans.
- **Network:** outbound HTTPS only. No port to open, nothing to change on your firewall.

## Setup

1. On the Ticket Tool dashboard, open **Automations → Connections → Game server plugins** and click **Create a Rust key**. Name it after the server (for example "Main") and copy the key. It's only shown once.
2. Download `TicketToolLink-rust-<version>.cs` from the [releases page](https://github.com/ticket-tool-app/integrations/releases?q=rust), rename it to `TicketToolLink.cs` and put it in `oxide/plugins` (or `carbon/plugins`). The plugin loads and creates its config file.
3. Paste the key into `oxide/config/TicketToolLink.json` (or `carbon/configs/TicketToolLink.json`):

   ```json
   { "key": "ttp_0123456789abcdef.your-key-here" }
   ```

4. Run `ticketlink reload` in the server console. Within a few seconds the key shows **Connected now** on the Connections page, and `ticketlink status` says "Connected to Ticket Tool."
5. In Ticket Tool, edit a ticket category and switch on **Rust** in its **Lookups** card, its **Ban lookup** card, or both.

Players need their Steam account linked on their Ticket Tool **Linked accounts** page and shared with your Discord server. There's no in-game linking command for Rust.

## Commands and permissions

| Command | Permission | What it does |
| --- | --- | --- |
| `ticketlink status` | `tickettoollink.admin` | Connection state, key id, Discord server, ban source, last problem |
| `ticketlink reload` | `tickettoollink.admin` | Re-reads the config and reconnects |

Both work from the server console and RCON, or in chat as `/ticketlink` for admins and anyone granted the permission. `tickettool` works as an alias.

When `reports` is on, every player can use `/report [player] <message>` (the name is set by `report-command`).

## Configuration

```json
{
  "key": "",
  "api-url": "https://dash.ticket-tool.app",
  "server-name": "",
  "actions": [],
  "reports": false,
  "report-command": "report",
  "report-cooldown-seconds": 60,
  "debug": false
}
```

| Key | What it does |
| --- | --- |
| `key` | The full key from the dashboard |
| `api-url` | Leave as is; for testing against a staging server |
| `server-name` | The server name staff see on the card; empty uses the key's name |
| `actions` | Staff actions to allow: any of `message`, `warn`, `kick`, `unban` |
| `reports` | `true` turns on the report command |
| `report-command` | Chat command name for reports; a change takes effect when the plugin reloads |
| `report-cooldown-seconds` | Seconds between reports per player |
| `debug` | More detail in the console. The key is never written to the log |

## Staff actions and in-game reports

Staff can act on a player from Discord: from **Run an app action** on a ticket, a flow, or an automation rule. Every action is off until you turn it on, and Ticket Tool only offers the ones you did. Actions need the Pro plan; reports work on Community and Pro.

| Action | What happens in game |
| --- | --- |
| `message` | The player gets a chat message from the staff member |
| `warn` | The player gets a warning in chat |
| `kick` | The player is kicked with the reason staff typed |
| `unban` | The player is removed from the server's ban list (`ServerUsers`), which is then saved |

Staff text is shown as typed, without Rust's rich text tags. With `reports` on, `/report [player] <message>` sends the report to Ticket Tool, where an automation rule with the trigger **When a player sends a report in game** can open a ticket. The first word counts as the reported player only when it's the name of someone online, and each player can send one report every `report-cooldown-seconds`.

## What the plugin sends

Only what the [protocol](../spec/PROTOCOL.md) defines: for a lookup, the player's SteamID, name, online status, last-seen and first-join times and the `server-name`, plus up to three card lines:

- **Role:** Owner or Moderator, from the server's `ownerid`/`moderatorid` list.
- **Playtime:** when [PlaytimeTracker](https://umod.org/plugins/playtime-tracker) is installed.
- **Clan:** the clan tag when [Clans](https://umod.org/plugins/clans) is installed.

For bans: whether the server ban list has an active ban, its reason (rich-text tags removed, at most 2,000 characters) and its expiry. Rust's ban list doesn't record who issued a ban or when, so those stay empty. No IP addresses, chat or inventories. Ban reasons appear on the private staff card in Ticket Tool, so write them with that in mind.

First-join and last-seen times are recorded by the plugin itself in `oxide/data/TicketToolLink_seen.json` from when it's installed, because Rust keeps no such record. A player who joined before the plugin was installed shows those times once they next join.

## Threading

Network calls run on background threads, so the plugin never blocks a server frame. Everything that reads game state (players, the ban list, other plugins) runs on the main thread through `NextTick`. The only blocking call is on unload or reload, which waits at most about 3 seconds to tell Ticket Tool the plugin is going offline.

## Troubleshooting

| `ticketlink status` says | Do this |
| --- | --- |
| No key set | Paste the key into the config file and run `ticketlink reload` |
| Ticket Tool doesn't recognize this key | The key was rotated, revoked or mistyped. Copy the current key from the dashboard |
| Paused: the Discord server's plan doesn't include game lookups | Upgrade to Community or Pro; the plugin checks again every hour |
| Three polls are already open | The same key is in use on another server. Give each server its own key |
| Can't reach Ticket Tool | Check the server can make outbound HTTPS requests to `dash.ticket-tool.app` |

## Building

The release file is `rust/src/TicketToolLink.cs` with the [.NET SDK](../sdk-dotnet) appended, because Oxide only loads plugins that are a single source file:

```bash
python3 rust/build.py 1.0.0          # writes rust/dist/TicketToolLink-rust-1.0.0.cs
dotnet build rust/check              # compiles it as C# 7.3 against stub Oxide/Rust types
dotnet build rust/check -p:Carbon=true
dotnet test rust/tests               # runs it against a fake Ticket Tool server
```

The real Oxide, Carbon and Rust assemblies can't be redistributed, so `rust/check/Stubs` declares just the members the plugin uses. That catches mistakes in the plugin and SDK code, but not a change in the game's own API; test a release on a real server too.

## Not supported in version 1

- Ban plugins with their own storage (for example Enhanced Ban System). Bans they push into the server ban list are seen.
- In-game account linking. Players link Steam on Ticket Tool instead.
