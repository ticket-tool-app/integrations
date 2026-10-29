# Ticket Tool Link (Minecraft)

Connects a Minecraft server to one Discord server's [Ticket Tool](https://ticket-tool.app). When someone opens a ticket, staff see their Minecraft name, UUID, whether they're online, when they were last seen and when they first joined, plus any active ban. Players link their Minecraft account to Discord with `/ttlink <code>`.

- **Servers:** Paper, Folia and Spigot, Minecraft 1.20.6 or newer, Java 21.
- **Ban plugins:** LiteBans, AdvancedBan, LibertyBans, or the server's own ban list when none is installed.
- **Plans:** game server plugins are included with Ticket Tool's Community and Pro plans.
- **Network:** outbound HTTPS only. No port to open, nothing to change on your firewall.

## Setup

1. On the Ticket Tool dashboard, open **Automations → Connections → Game server plugins** and click **Create a Minecraft key**. Name it after the server (for example "Survival") and copy the key. It's only shown once.
2. Download `TicketToolLink-<version>.jar` from the [latest Minecraft release](https://github.com/ticket-tool-app/integrations/releases?q=minecraft). Put it in the server's `plugins` folder and start the server once. This creates `plugins/TicketToolLink/config.yml` and `messages.yml`.
3. Paste the key into `config.yml`:

   ```yaml
   key: "ttp_0123456789abcdef.your-key-here"
   ```

4. Run `/ticketlink reload`. Within a few seconds the key shows **Connected now** on the Connections page, and `/ticketlink status` says "Connected to Ticket Tool."
5. In Ticket Tool, edit a ticket category and switch on **Minecraft** in its **Lookups** card, its **Ban lookup** card, or both.

### Networks (Velocity, BungeeCord)

Install the plugin on **each backend**, not the proxy, with one key per backend. That way bans and last-seen times come from where they're stored. Set `server-name` on each backend so staff can tell them apart; Ticket Tool shows the backend where the player is online.

## Commands and permissions

| Command | Permission | Default | What it does |
| --- | --- | --- | --- |
| `/ttlink <code>` | `tickettool.link` | everyone | Links the player's Minecraft account to the Discord account that made the code |
| `/report [player] <message>` | `tickettool.report` | everyone | Sends a report to the staff team on Discord, when `reports.enabled` is on |
| `/ticketlink status` | `tickettool.admin` | op | Connection state, key id, Discord server, ban source, last problem |
| `/ticketlink reload` | `tickettool.admin` | op | Re-reads `config.yml` and `messages.yml` and reconnects |

`/tickettool` works as an alias for `/ticketlink`, and `/ttreport` for `/report` (use it when another plugin owns `/report`).

`/ttlink` only works for players, never the console or command blocks, and always sends the UUID and name of the player who typed it. A player can try once every 5 seconds (`link-cooldown-seconds`). If Mojang is briefly unreachable, the plugin retries once after 3 seconds before telling the player to try again; the code stays valid.

## Configuration

```yaml
key: ""                      # the full key from the dashboard
server-name: ""              # backend name staff see; empty uses the key's name
ban-provider: auto           # auto, litebans, advancedban, libertybans or vanilla
link-cooldown-seconds: 5
actions:                     # what staff can do from Discord; each is off until set to true
  message: false
  warn: false
  kick: false
  unban: false
reports:
  enabled: false             # turns on /report
  cooldown-seconds: 60
api-url: "https://dash.ticket-tool.app"
debug: false
```

All player-facing text is in `messages.yml`, with `&` color codes and `{placeholders}`. Set a message to `""` to turn it off.

## Staff actions and in-game reports

Staff can act on a player from Discord: from **Run an app action** on a ticket, a flow, or an automation rule. Every action is off until you turn it on, and Ticket Tool only offers the ones you did. Actions need the Pro plan; reports work on Community and Pro.

| Action | What happens in game |
| --- | --- |
| `message` | The player gets a chat message from the staff member (`action.message` in `messages.yml`) |
| `warn` | The player gets a warning in chat (`action.warn`) |
| `kick` | The player is kicked with the reason staff typed |
| `unban` | The ban is lifted through the ban plugin in use (`litebans:unban`, `advancedban:unban`, `libertybans:unban` or `minecraft:pardon`, run as the console) |

Staff text is shown as typed: its `&` and `§` codes aren't applied. A player who isn't online gets "Player offline" back on Discord, and unbanning someone who isn't banned says so.

With `reports.enabled: true`, `/report [player] <message>` sends the report to Ticket Tool, where an automation rule with the trigger **When the game server sends a report** can open a ticket. The first word counts as the reported player only when it's the name of someone online. Each player can send one report every `cooldown-seconds`; the reporter is always the player who typed the command.

## What the plugin sends

Only what the [protocol](../spec/PROTOCOL.md) defines: for a lookup, the player's UUID, name, online status, last-seen and first-join times and the `server-name`; for bans, whether there's an active ban, its reason (color codes removed, at most 2,000 characters), start, expiry, who issued it and how many are active. For a report, the reporter's UUID and name, the message, the reported player's name and the `server-name`. No IP addresses, chat or inventories. Ban reasons appear on the private staff card in Ticket Tool, so write them with that in mind.

Playtime isn't sent: version 1 of the protocol has no field for it.

## Threading

Everything that reads game state runs on the right thread: the main thread on Paper and Spigot, the global region (or the player's region) on Folia. Network calls and ban-plugin database queries run on background threads, so the plugin never blocks a tick.

## Troubleshooting

| `/ticketlink status` says | Do this |
| --- | --- |
| No key set | Paste the key into `config.yml` and run `/ticketlink reload` |
| Ticket Tool doesn't recognize this key | The key was rotated, revoked or mistyped. Copy the current key from the dashboard |
| The Discord server's plan doesn't include game lookups | Upgrade to Community or Pro; the plugin checks again every hour |
| Three polls are already open | The same key is in use on another server. Give each server its own key |
| Can't reach Ticket Tool | Check the server can make outbound HTTPS requests to `dash.ticket-tool.app` |

Set `debug: true` for more detail in the console. The key is never written to the log.

## Not supported in version 1

- A proxy (Velocity/BungeeCord) edition. Install on the backends instead.
- Bedrock players through Geyser/Floodgate, and offline-mode servers: Ticket Tool verifies every UUID with Mojang before linking, so only real Java Edition accounts can link.
