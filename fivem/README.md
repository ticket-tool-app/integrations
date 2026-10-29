# Ticket Tool Link (FiveM and RedM)

Connects a FiveM or RedM server to one Discord server's [Ticket Tool](https://ticket-tool.app). When someone opens a ticket, staff see their in-game name, Steam account, server ID, whether they're online, when they were last seen and first joined, their character and job on ESX, QBCore or Qbox, and any active ban from txAdmin.

- **Servers:** FiveM and RedM (one resource for both), FXServer with its default Node 16 runtime or newer.
- **Ban sources:** txAdmin; EasyAdmin and the QBCore/Qbox `bans` table as the `server` source.
- **Frameworks:** ESX, QBCore, Qbox and ox_core, all optional and detected while the server runs.
- **Plans:** game server plugins are included with Ticket Tool's Community and Pro plans.
- **Network:** outbound HTTPS only. No port to open, nothing to change on your firewall.

No account linking is needed. FXServer gets each player's Discord account from Cfx.re as a `discord:` identifier, and Ticket Tool asks about the ticket opener by their Discord ID, so a player who has Discord connected to FiveM or RedM is matched automatically.

## Setup

1. On the Ticket Tool dashboard, open **Automations → Connections → Game server plugins** and click **Create a FiveM key**. Name it after the server (for example "Roleplay 1") and copy the key. It's only shown once.
2. Download `ticket-tool-link-fivem-<version>.zip` from [the FiveM releases](https://github.com/ticket-tool-app/integrations/releases?q=fivem) and unzip it into your server's `resources` folder, so you have `resources/ticket-tool-link/fxmanifest.lua`. Nothing needs installing: the zip is ready to run.
3. Add this to `server.cfg`:

   ```cfg
   set ticket_tool_key "ttp_0123456789abcdef.your-key-here"
   ensure ticket-tool-link
   ```

   Use `set`, never `sets` or `setr`: those publish the value to the server list or to every player.

4. Restart the server, or run `refresh` then `ensure ticket-tool-link` in the console. Within a few seconds the key shows **Connected now** on the Connections page, and `ticketlink status` says "connected to Ticket Tool".
5. In Ticket Tool, edit a ticket category and switch on **FiveM** in its **Lookups** card, its **Ban lookup** card, or both.

Running several servers? Give each its own key and its own `ticket_tool_server_name`. Ticket Tool shows the server where the player is online.

## Commands

| Command | Who | What it does |
| --- | --- | --- |
| `ticketlink status` | console, or the `command.ticketlink` ace | Connection state, key id, Discord server, ban sources, framework, last problem |
| `ticketlink reload` | console, or the `command.ticketlink` ace | Re-reads the convars and `config.json` and reconnects |

To let an in-game admin group use it: `add_ace group.admin command.ticketlink allow`.

When reports are on, players also get `/report [server id] <message>` (the name is set by `ticket_tool_report_command`).

## Configuration

Set these in `server.cfg` with `set`. Each one can also go in `resources/ticket-tool-link/config.json` (copy `config.example.json`); a convar that is set wins over the file.

| Convar | `config.json` | Default | What it does |
| --- | --- | --- | --- |
| `ticket_tool_key` | `key` | | The full key from the dashboard |
| `ticket_tool_server_name` | `serverName` | | Server name staff see; empty uses the key's name |
| `ticket_tool_ban_provider` | `banProvider` | `auto` | `auto`, `txadmin`, `server` or `none` |
| `ticket_tool_txadmin_db` | `txAdminDb` | | Path to txAdmin's `playersDB.json` (or its txData folder) if it isn't found on its own |
| `ticket_tool_actions` | `actions` | | Staff actions to allow, for example `message,warn,kick`. Empty allows none |
| `ticket_tool_reports` | `reports` | `false` | `true` turns on the report command |
| `ticket_tool_report_command` | `reportCommand` | `report` | Chat command name for reports |
| `ticket_tool_report_cooldown` | `reportCooldown` | `60` | Seconds between reports per player |
| `ticket_tool_api_url` | `apiUrl` | `https://dash.ticket-tool.app` | Only change this if Ticket Tool support asks you to |
| `ticket_tool_debug` | `debug` | `false` | More detail in the console |

## Staff actions and in-game reports

Staff can act on a player from Discord: from **Run an app action** on a ticket, a flow, or an automation rule. Every action is off until you turn it on, and Ticket Tool only offers the ones you did. Actions need the Pro plan; reports work on Community and Pro.

| Action | What happens in game |
| --- | --- |
| `message` | The player gets a chat message from the staff member |
| `warn` | The player gets a warning in chat |
| `kick` | The player is dropped with the reason staff typed |

Players are found by the Discord account Cfx.re reports for them, so only players who connected Discord to FiveM or RedM can be reached. Unban isn't available: txAdmin and the framework ban lists have no supported way for another resource to lift a ban.

With `ticket_tool_reports` on, `/report [server id] <message>` sends the report to Ticket Tool, where an automation rule with the trigger **When the game server sends a report** can open a ticket. The first word counts as the reported player only when it's the server id of someone online. The reporter needs Discord connected to FiveM, and can send one report every `ticket_tool_report_cooldown` seconds.

## Ban sources

`auto` uses every source it finds and shows the newest active ban; staff see how many are active when there's more than one.

| Source | Reported as | How it's read |
| --- | --- | --- |
| txAdmin | `txadmin` | txAdmin has no server-side API for bans, so the resource reads txAdmin's own database file, `txData/<profile>/data/playersDB.json`, read-only. It finds the file the way txAdmin does (`TXHOST_DATA_PATH`, the `txDataPath` convar, or the default next to the server artifacts); set `ticket_tool_txadmin_db` if yours lives elsewhere. txAdmin saves that file every 15 to 60 seconds, so the resource also listens for txAdmin's ban and revoke events to catch the last minute. Bans match on any of the player's identifiers, including their Discord ID, so a banned player is found even if they never joined after this resource was installed. |
| EasyAdmin | `server` | `EasyAdmin/banlist.json`, read through FXServer's resource file API |
| QBCore / Qbox | `server` | The framework's `bans` table, through oxmysql, by Discord ID and license |

With no ban source, ban lookups answer "no ban source installed" and profile lookups still work.

## What the resource sends

Only what the [protocol](../spec/PROTOCOL.md) defines: the player's Discord ID (the one Ticket Tool asked about), SteamID64 when Steam is connected, in-game name, online status, last-seen and first-join times, the server name, and up to six fields: server ID while online, and character name and job (ESX, QBCore, Qbox) or character name (ox_core). For bans: whether there's an active ban, its reason (color codes removed, at most 2,000 characters), start, expiry, who issued it and how many are active. No IP addresses, license keys, chat or inventories. Ban reasons appear on the private staff card in Ticket Tool, so write them with that in mind.

## Last seen

Players who aren't online are answered from a small record the resource keeps in its own KVP store (FXServer's per-resource storage, no database needed): name, Steam ID, the identifiers used for ban matching (never the IP), first and last seen, and the character fields last read. It's updated when a player joins or leaves and every five minutes while they're online. Players without Discord connected can't be looked up and aren't recorded.

## Troubleshooting

| `ticketlink status` says | Do this |
| --- | --- |
| No key set | Add `set ticket_tool_key "ttp_..."` to `server.cfg` and run `ticketlink reload` |
| Ticket Tool doesn't recognize this key | The key was rotated, revoked or mistyped. Copy the current key from the dashboard |
| Key was made for a different game | Create a FiveM key; a Minecraft or Steam-game key won't work here |
| The Discord server's plan doesn't include game lookups | Upgrade to Community or Pro; the resource checks again every hour |
| Three polls are already open | The same key is in use on another server. Give each server its own key |
| Can't reach Ticket Tool | Check the server can make outbound HTTPS requests to `dash.ticket-tool.app` |
| txAdmin database: not found yet | Set `ticket_tool_txadmin_db` to the full path of `playersDB.json` |

The key is never written to the console; only its public id (`ttp_0123456789abcdef`) is.

## Building from source

```sh
cd fivem
npm ci
npm test           # unit tests with stubbed natives, plus an end-to-end run of the bundled resource
npm run package    # build/ticket-tool-link-fivem-<version>.zip
```

The resource is TypeScript in `src/`, bundled by esbuild into `ticket-tool-link/dist/server.js` with the [TypeScript SDK](../sdk-ts) inlined. The end-to-end test runs that bundle under plain Node with stubbed FXServer natives against a fake Ticket Tool server; set `TTL_E2E_NODE` to a Node 16 binary to run it on FXServer's runtime version.

## Not supported in version 1

- Linking accounts in game (`/link` is Minecraft-only; FiveM uses the Discord identifier instead).
- Players who haven't connected Discord to FiveM or RedM.
