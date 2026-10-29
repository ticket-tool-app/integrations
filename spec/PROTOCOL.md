# Ticket Tool Game Plugin Protocol, v1

Spec for the public `ticket-tool-app/integrations` repo. The protocol sections
below match the public docs page
(https://ticket-tool.app/docs/integrations/plugin-protocol) word for word;
keep them in step when either changes. The SDK notes, test vectors and
conformance checklist at the end are for plugin authors and are not on the
docs page.

Server-side source of truth (ticket-tool monorepo):

- `packages/shared/src/game-plugins/protocol.ts`: constants, headers, Zod schemas
- `packages/shared/src/game-plugins/signing.ts`: key format, secret derivation, signing
- `apps/web/src/app/api/plugin/v1/*`: the endpoints
- `apps/web/src/lib/game-plugins/plugin-auth.ts`: verification order and errors

---

# Game Plugin Protocol

A game server plugin lets your own game server answer for one Discord server: when a ticket opens, Ticket Tool asks the plugin about the opener's linked player and shows staff what it says, and players link their account by typing a code in game. This page describes protocol **version 1** so anyone can build a plugin for any game.

> **Minecraft first, plugin coming soon.** Ticket Tool's side is live: integration keys, the endpoints below, in-game account linking, and the Minecraft source in [Lookups](https://ticket-tool.app/docs/integrations/lookups) and [Ban Lookup](https://ticket-tool.app/docs/integrations/ban-lookup). The official open-source Minecraft plugin is coming soon. Until then you can build your own against this page. See [Minecraft Plugin](https://ticket-tool.app/docs/integrations/minecraft-plugin).

## How it fits together

1. **Admin creates a key.** Dashboard → Automations → Connections → Game server plugins. Shown once.
2. **Plugin polls out.** It holds an HTTPS long-poll open to Ticket Tool. No port to open.
3. **A ticket opens.** Ticket Tool queues a question about the opener's linked player and wakes the poll.
4. **Plugin answers.** It posts the profile and bans back within the lookup's deadline.

- **Outbound only.** The plugin makes every connection. Ticket Tool never connects to your game server or to its database.
- **One key, one server.** A key belongs to one Discord server and one plugin type. It can answer lookups and report account links for that server only. It can't read tickets or change settings.
- **Signed both ways.** Every request is signed with a secret derived from the key, with a timestamp and a nonce. Every successful reply is signed too; some errors are not (see [Unsigned errors](#unsigned-errors)).

### Why long-polling

Version 1 uses HTTPS long-polling rather than a WebSocket. Game hosts often allow outbound HTTPS and little else, and some sit behind proxies that break long-lived upgrades; a long-poll is an ordinary HTTPS request that any client can make (Java's `java.net.http`, Node's `fetch`), it needs no extra library, and a dropped connection is just a poll to retry. A question still reaches the plugin as soon as it is queued, because a waiting poll returns the moment there is work. A WebSocket transport may be added in a later version and would be offered in the `/hello` reply.

## Keys

An integration key looks like this:

```text
ttp_0123456789abcdef.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
```

| Part           | Format                         | Use                                                     |
| -------------- | ------------------------------ | ------------------------------------------------------- |
| `ttp_`         | Fixed prefix                   | Lets secret scanners and humans recognize the key       |
| Key id         | 16 lowercase hex characters    | Public. Sent with every request in `X-TT-Key-Id`        |
| `.` and secret | 43 base64url characters        | Never sent anywhere. Only used to derive the secret     |

The plugin never sends the key itself. It derives a **signing secret** once, at start-up:

```text
signingSecret = lowercase hex( HMAC-SHA256( key = the full key string,
                                            message = "ticket-tool plugin signing v1" ) )
```

The result is 64 hex characters. Use that hex string (its UTF-8 bytes) as the HMAC key when signing.

Ticket Tool stores a SHA-256 hash of the key and the derived signing secret, encrypted. The key itself is shown once, when it is created or rotated. **Rotating** issues new key material on the same entry and stops the old key at once; **revoking** deletes it. Both are on the Connections page, which also shows when each plugin last connected and which version it reported.

## Requests

Base URL: `https://api.ticket-tool.app/api/plugin/v1`. `https://dash.ticket-tool.app/api/plugin/v1` serves the same endpoints, and is what the official plugin uses by default.

Every endpoint takes `POST` with a JSON body (`{}` when there is nothing to send) and `Content-Type: application/json`. Bodies are limited to 32 KB.

### Headers

| Header           | Value                                                                                    |
| ---------------- | ---------------------------------------------------------------------------------------- |
| `X-TT-Key-Id`    | The key id                                                                               |
| `X-TT-Timestamp` | Current Unix time in seconds                                                             |
| `X-TT-Nonce`     | 16 to 64 characters of `A-Z a-z 0-9 _ -`, random and never reused                        |
| `X-TT-Signature` | `v1=` followed by the lowercase hex HMAC described below                                 |
| `X-TT-Protocol`  | `1`                                                                                      |
| `X-TT-Plugin`    | Free text shown on the dashboard, e.g. `ticket-tool-minecraft/1.0.0 (Paper 1.21.1)`      |

Ticket Tool shows the first 100 characters of `X-TT-Plugin` next to the key. `X-TT-Protocol` may be left out; when it is sent, it must be `1`.

### Signing a request

Build the canonical string from six lines joined with `\n` (no trailing newline):

```text
v1
POST
/poll
1760529600
abcdefghijklmnop
44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a
```

1. The literal `v1`
2. The HTTP method in upper case
3. The **endpoint**: the path after the base URL, such as `/poll`. Not the full URL, so proxies and base URL changes don't affect signatures
4. The value of `X-TT-Timestamp`
5. The value of `X-TT-Nonce`
6. The lowercase hex SHA-256 of the exact body bytes you send

Then `X-TT-Signature` is `v1=` + lowercase hex `HMAC-SHA256(signingSecret, canonical string)`.

Ticket Tool rejects a request when:

- a signing header is missing (`missing_headers`) or the nonce has the wrong format (`bad_nonce`)
- the timestamp is more than **300 seconds** from its clock (`stale_timestamp`)
- the signature doesn't match the body, endpoint, method, timestamp and nonce (`bad_signature`)
- the nonce was already used with this key in the last **10 minutes** (`replayed`)

Every signature failure (`missing_headers`, `bad_nonce`, `stale_timestamp` and `bad_signature`) includes `serverTime`, Ticket Tool's current Unix time, so you can correct for clock skew.

### Worked example

Use these values to test an implementation:

| Input               | Value                                                                  |
| ------------------- | ---------------------------------------------------------------------- |
| Key                 | `ttp_0123456789abcdef.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA`     |
| Signing secret      | `20998da3e0f449ee48f630226afec7b5991112ac4233721a30ba008bb50d8a9c`     |
| Request             | `POST /poll`, body `{}`, timestamp `1760529600`, nonce `abcdefghijklmnop` |
| `X-TT-Signature`    | `v1=d71627ae221c55861c68c1e5d230d7faec74072f75d952ccaa58a2b67b608d3d`  |

### Verifying replies

Once the key is recognized, every reply is signed with the same secret. Replies carry `X-TT-Timestamp`, `X-TT-Nonce` (your request's nonce, echoed) and `X-TT-Signature`, over this canonical string:

```text
v1
RESPONSE
<endpoint>
<reply timestamp>
<your request's nonce>
<sha256 hex of the reply body>
```

Accept a reply only when the nonce is the one you sent, the timestamp is within 300 seconds, and the signature matches. Because the nonce is yours, a recorded reply can't be passed off as the answer to a different request. For the example above, a reply body `{"ok":true,"requests":[]}` at timestamp `1760529601` is signed `v1=57e67ded8456ec7d4a98676cfbd5d346e3e0d5d6a1e74f9685fb00496cc3837d`.

### Unsigned errors

Errors from the checks Ticket Tool runs on every request, before the endpoint itself handles it, come back **unsigned**: `invalid_key`, `missing_headers`, `bad_nonce`, `stale_timestamp`, `bad_signature`, `replayed`, `unsupported_protocol`, `too_large`, `rate_limited` (both the per-address and the per-key limit), `plan_not_entitled`, `503 unavailable` and `500 internal`. Errors from the endpoint itself, after those checks pass, are signed like any other reply, so the same code (`unavailable`, for example) can arrive either way.

A plugin must accept an unsigned reply only as an error (`ok: false` with a status of 400 or above), and act on it as the [Errors](#errors) table says. It must never treat an unsigned reply as a success.

## Endpoints

### POST /hello

Send on start-up and after a config reload.

```json
{
  "protocol": 1,
  "plugin": { "type": "minecraft", "version": "1.0.0", "platform": "Paper 1.21.1" },
  "banProviders": ["litebans"],
  "actions": ["message", "kick"],
  "events": ["player.report"]
}
```

`actions` and `events` list what the server owner has turned on in the plugin's config (see [Player actions](#player-actions) and [POST /event](#post-event)). Leave them out, or send empty lists, when nothing is on.

```json
{
  "ok": true,
  "protocol": 1,
  "pluginType": "minecraft",
  "key": { "name": "Survival" },
  "server": { "id": "123456789012345678", "name": "My Discord server" },
  "poll": { "waitSeconds": 25 }
}
```

`plugin.type` must match the key's plugin type, or the reply is `400 wrong_plugin_type` with the key's type in `expected`. A plugin that speaks a newer protocol reads `protocol` here and uses that version.

Send `/hello` again after a config reload, so the dashboard shows the actions and events that are on now.

### POST /poll

Body `{}`. Ticket Tool holds the request open for up to `poll.waitSeconds` (25) and replies as soon as there is something to do:

```json
{
  "ok": true,
  "requests": [
    {
      "id": "8f14e45f-ceea-4e7a-9b2e-1c2d3e4f5a6b",
      "type": "player.lookup",
      "expiresAt": "2026-10-15T12:00:04.000Z",
      "player": { "uuid": "069a79f4-44e9-4726-a5be-fca90e38aaf5", "name": "Notch" },
      "include": ["profile", "bans"]
    }
  ]
}
```

- Poll again straight away after every reply, and answer requests on another thread so the next poll isn't held up.
- Use a read timeout of at least 35 seconds.
- On a network error or a `5xx`, back off (1, 2, 4 … up to 60 seconds, with jitter).
- A request must be answered before `expiresAt`. That is usually 4 to 5 seconds after it was queued, and can be as short as about 1.5 seconds when a support flow checks bans while a member waits. Skip any that have expired.
- A ticket's profile lookup and its ban lookup arrive as **separate requests**, one with `include: ["profile"]` and one with `include: ["bans"]`. Answer each on its own. Read `include` rather than assuming it, since a future request may ask for both.
- The plugin counts as **connected** while a poll is open and for 40 seconds after.
- A request's `type` is `player.lookup` (above) or `player.action` (see [Player actions](#player-actions)).
- Ignore request `type`s and `include` parts you don't know.

### POST /respond

Answer one request. Include each part that was asked for.

```json
{
  "requestId": "8f14e45f-ceea-4e7a-9b2e-1c2d3e4f5a6b",
  "ok": true,
  "profile": {
    "uuid": "069a79f4-44e9-4726-a5be-fca90e38aaf5",
    "name": "Notch",
    "online": true,
    "lastSeen": "2026-10-15T11:58:00Z",
    "firstSeen": "2011-02-03T18:00:00Z",
    "server": "survival-1"
  },
  "bans": {
    "banned": true,
    "provider": "litebans",
    "reason": "X-ray",
    "createdAt": "2026-09-01T20:15:00Z",
    "expiresAt": null,
    "actor": "ModAlex",
    "activeCount": 1
  }
}
```

| Field                | Type                 | Notes                                                                 |
| -------------------- | -------------------- | --------------------------------------------------------------------- |
| `profile.uuid`       | string               | With or without dashes                                                |
| `profile.name`       | string               | Current player name                                                   |
| `profile.online`     | boolean              | Online on this server right now                                       |
| `profile.lastSeen`   | ISO 8601 or `null`   | Last time on this server                                              |
| `profile.firstSeen`  | ISO 8601 or `null`   | First join                                                            |
| `profile.server`     | string or `null`     | Backend or world, on a proxy network                                  |
| `bans.banned`        | boolean              | An active ban right now                                               |
| `bans.provider`      | string               | `litebans`, `advancedban`, `libertybans` or `vanilla`                 |
| `bans.reason`        | string or `null`     | Up to 2,000 characters; the card shows 500                            |
| `bans.createdAt`     | ISO 8601 or `null`   | When the ban started                                                  |
| `bans.expiresAt`     | ISO 8601 or `null`   | `null` means permanent                                                |
| `bans.actor`         | string or `null`     | Who issued it                                                         |
| `bans.activeCount`   | integer, optional    | Active bans on record, when more than one                             |

When the plugin can't answer, send `ok: false` with an error code:

```json
{ "requestId": "8f14e45f-ceea-4e7a-9b2e-1c2d3e4f5a6b", "ok": false, "error": "no_ban_provider" }
```

| `error`           | Meaning                                                                    |
| ----------------- | -------------------------------------------------------------------------- |
| `unknown_player`  | The player has never joined and has no ban on record                       |
| `no_ban_provider` | Bans were asked for but no supported ban plugin is installed               |
| `unsupported`     | The request type or a part isn't supported by this plugin                  |
| `error`           | Anything else; add a short `message` for the server's admins               |

Ticket Tool replies `{"ok": true}`, or `410 unknown_request` when the request expired, was already answered, or was never sent to this key. Send only what staff should see: reasons appear on the private staff card. IP addresses are not part of the protocol, and fields it doesn't define are dropped.

### Player actions

Staff can ask the plugin to act on a player in game. Actions arrive through `/poll` like lookups, with `type: "player.action"`:

```json
{
  "id": "2c4f8a10-6b1e-4d3a-9f0e-7a8b9c0d1e2f",
  "type": "player.action",
  "expiresAt": "2026-10-15T12:00:10.000Z",
  "player": { "uuid": "069a79f4-44e9-4726-a5be-fca90e38aaf5", "name": "Notch" },
  "action": { "kind": "kick", "message": "Please read the rules before rejoining." },
  "actor": { "name": "alex" }
}
```

| `action.kind` | What the plugin does                               | Player must be online |
| ------------- | -------------------------------------------------- | --------------------- |
| `message`     | Shows `message` to the player in chat              | Yes                   |
| `warn`        | Shows `message` to the player as a staff warning   | Yes                   |
| `kick`        | Kicks the player, with `message` as the reason     | Yes                   |
| `unban`       | Lifts the player's active ban                      | No                    |

- `player` has the same shape as a lookup's, and the plugin reads the identity for its type.
- `message` is up to 500 characters of plain text, or `null`. `unban` never has one. Show it as text; never run it as a command.
- `actor.name` is the Discord username of the staff member who asked, or `Ticket Tool` when an automation did. Use it in the in-game text and the server log.
- **Every action is off until the server owner turns it on** in the plugin's config. Answer `not_allowed` for an action that is off, and list the ones that are on in `/hello` `actions`.
- Actions expire like lookups, usually 10 seconds after they are queued. Skip expired ones: an action carried out late would surprise staff.

Answer with `/respond`: `{ "requestId": "…", "ok": true }` once the action is done, or `ok: false` with one of these errors:

| `error`          | Meaning                                                                         |
| ---------------- | ------------------------------------------------------------------------------- |
| `player_offline` | `message`, `warn` or `kick` for a player who isn't online on this server        |
| `not_banned`     | `unban` for a player with no active ban                                         |
| `not_allowed`    | The server owner hasn't turned this action on                                   |
| `unknown_player` | The player has never joined                                                     |
| `unsupported`    | This plugin can't do the action here, for example `unban` with no ban plugin    |
| `error`          | Anything else; add a short `message` for the server's admins                    |

On a network of several backends, every connected plugin for the server is asked, and the action counts as done when any one of them answers `ok: true`.

### POST /event

A player did something in game that the Discord server wants to hear about. Version 1 has one event, `player.report`: a player ran the report command (`/report` by default) with a message.

```json
{
  "type": "player.report",
  "player": { "uuid": "069a79f4-44e9-4726-a5be-fca90e38aaf5", "name": "Notch" },
  "message": "Someone is griefing spawn",
  "target": { "name": "Griefer123" },
  "server": "survival-1"
}
```

| Field     | Type             | Notes                                                                                                                       |
| --------- | ---------------- | --------------------------------------------------------------------------------------------------------------------------- |
| `type`    | string           | `player.report`                                                                                                             |
| `player`  | object           | Who sent it: `name`, plus `uuid` for Minecraft, `steamId` for Garry's Mod, Rust and CS2, or `discordId` for FiveM           |
| `message` | string           | 1 to 1,000 characters                                                                                                       |
| `target`  | object or `null` | The player the report is about, when the command names one: `name`, plus the same identity field when the plugin knows it   |
| `server`  | string or `null` | Backend or world, on a proxy network                                                                                        |

```json
{ "ok": true, "result": "opened" }
```

Ticket Tool opens a ticket when the Discord server has an automation rule for game server reports. `result` is `opened`, or `ignored` when no rule took the report; tell the player it couldn't be sent. The ticket is opened as the player's linked Discord account when they have one, and otherwise as the bot, with a note naming the player.

| Status | `error`        | Meaning                                                  |
| ------ | -------------- | -------------------------------------------------------- |
| 400    | `invalid_body` | Fix the message. `issues` says which fields              |
| 429    | `rate_limited` | More than 10 `/event` calls this minute with this key    |

Take `player` from the player who ran the command, never from command arguments, and accept the command only from players. Cool down per player (one report every 60 seconds) on top of the server-side limit. Like actions, reports are off until the server owner turns them on; list `player.report` in `/hello` `events` when they are.

### POST /link

A player typed `/ttlink <code>` in game.

```json
{ "code": "ABCD-EFGH", "player": { "uuid": "069a79f4-44e9-4726-a5be-fca90e38aaf5", "name": "Notch" } }
```

```json
{ "ok": true, "result": "linked", "discord": { "username": "alex" }, "player": { "name": "Notch" } }
```

`result` is `relinked` when the player was already linked to that Discord account. Codes are 8 characters, single-use and valid for 10 minutes. Ticket Tool ignores case, spaces and dashes in the code, so `ABCD-EFGH` and `abcdefgh` are the same.

Ticket Tool looks the UUID up with Mojang before linking, and `player.name` in the reply is the name Mojang holds; the `name` you send is only used to read the request. Only real Java Edition accounts can link, so offline-mode UUIDs and Geyser/Floodgate UUIDs are refused.

| Status | `error`          | Meaning                                                                                      |
| ------ | ---------------- | -------------------------------------------------------------------------------------------- |
| 404    | `invalid_code`   | Unknown, used or expired code                                                                |
| 422    | `unknown_player` | Mojang has no account with this UUID. The code is used up                                    |
| 503    | `unavailable`    | Mojang didn't answer. `retryable` is `true` and the code still works: retry in a few seconds |
| 429    | `rate_limited`   | More than 20 `/link` calls this minute with this key. The code is not used up                |
| 400    | `unsupported`    | The key isn't a Minecraft key, so it can't link accounts                                     |

These replies are signed. Any other failed attempt uses the code up.

The plugin must take the UUID and name from the player who ran the command, never from command arguments, and should only accept the command from players (not the console). The new link is shared with this Discord server only; the member can share it with others from their Linked accounts page.

A link counts as verified only on the Discord server whose key reported it. A plugin is its server owner's word, so on any other server the member shares the account with, staff see it as linked in game on another server, not verified there, and its ban answers aren't counted as a clean record or a ban.

### POST /goodbye

Body `{}`. Send when the plugin shuts down so lookups show "unavailable" straight away instead of after 40 seconds.

## Errors

| Status | `error`                  | What to do                                                                 |
| ------ | ------------------------ | -------------------------------------------------------------------------- |
| 400    | `unsupported_protocol`   | The `X-TT-Protocol` version isn't served here. Read `protocol` and use it  |
| 400    | `invalid_body`           | Fix the message. On `/respond`, `issues` says which fields                 |
| 401    | `invalid_key`            | The key is unknown, revoked or rotated. Stop and tell the admin            |
| 401    | `missing_headers`, `bad_nonce`, `bad_signature` | A signing bug. Check against the worked example      |
| 401    | `stale_timestamp`        | Clock skew. Adjust by `serverTime` and retry once                          |
| 401    | `replayed`               | A nonce was reused. Generate a fresh one per request                       |
| 403    | `plan_not_entitled`      | The Discord server's plan no longer includes game lookups. Retry hourly    |
| 413    | `too_large`              | Body over 32 KB                                                            |
| 429    | `rate_limited`           | Back off for a minute. Unsigned, except the signed `/link` and `/event` limits |
| 429    | `too_many_polls`         | Three polls with this key are already open. Keep one poll at a time        |
| 500    | `internal`               | A fault on Ticket Tool's side. Retry with backoff                          |
| 503    | `unavailable`            | Retry with backoff                                                         |

## Limits

- 240 requests a minute per key, 20 `/link` calls and 10 `/event` calls a minute per key. Only correctly signed requests count against a key's limit. Each address also has a limit of its own, checked before the key is looked up
- 3 open polls at a time per key
- Up to 20 requests in one poll reply
- Up to 10 keys per Discord server. A lookup asks at most 5 connected plugins and merges their answers: the backend where the player is online wins the profile, and a ban on any backend counts

## Versioning

The protocol version is a whole number, sent in `X-TT-Protocol` and `/hello`. Within version 1, changes are additive only: new optional fields, new request `type`s, new `include` parts and new error codes. Plugins must ignore fields and types they don't recognize. A change that would break a version 1 plugin ships as version 2 under `/api/plugin/v2`, with version 1 kept running alongside it and the change announced in the changelog.

## Plans

Game server plugins use the same plan feature as lookups and ban lookup, so they work on Community and Pro. If a server's plan changes, the plugin's requests are refused with `plan_not_entitled` until it is back on a plan that includes them.

Player actions are sent from automations and staff actions, which need Pro: on Community the plugin still connects and answers lookups but receives no actions. Reports work on Community and Pro, the same as lookups.

---

# SDK notes

Both SDKs are thin: a signer, a client with one poll loop, and typed
messages. Game-specific code (reading players and bans) lives in the plugin,
not the SDK.

## Shared rules for every SDK

- Derive the signing secret once at start-up and keep only it in memory. Never
  log the key or the secret; log `ttp_<keyId>` at most.
- Hash the **exact bytes** you send. Serialize the body once, hash those bytes,
  send those bytes. Don't let an HTTP library re-serialize.
- Nonce: 16 random bytes, base64url without padding (22 chars), fresh per
  request, including retries. Never reuse a nonce, even for a retried request.
- Verify every signed reply before trusting it (nonce echoed, timestamp within
  300 s, signature). Treat an unsigned 401 `invalid_key` as fatal: stop polling
  and tell the admin; do not spin.
- Keep an offset `serverTime - localTime` from a `stale_timestamp` error and
  apply it to later timestamps.
- One poll loop at a time per key. Answer each request on a worker, then
  `/respond`; never block the poll loop on game code.
- Drop requests whose `expiresAt` has passed. Answering late gets
  `410 unknown_request`, which is harmless; ignore it.
- Unknown `type`, unknown `include` parts, unknown fields: ignore.
- On shutdown, send `/goodbye` with a short timeout (2 s) and don't wait longer.

## Java SDK (`ticket-tool-plugin-sdk`)

Target Java 17 (Paper 1.20.5+ needs 21; build with `--release 17` so Spigot
1.17+ also loads it). No dependencies beyond the JDK and a JSON library the
platform already ships (Gson is bundled with Bukkit/Paper).

- **HTTP:** `java.net.http.HttpClient` with `connectTimeout(10s)`; per-request
  `timeout(Duration.ofSeconds(35))` for `/poll`, 10 s for the rest. Use
  `HttpRequest.BodyPublishers.ofByteArray(bytes)` so the hashed bytes are the
  sent bytes.
- **Crypto:** `javax.crypto.Mac.getInstance("HmacSHA256")`,
  `MessageDigest.getInstance("SHA-256")`, `HexFormat.of()` (Java 17) for hex,
  `SecureRandom` + `Base64.getUrlEncoder().withoutPadding()` for nonces. Compare
  signatures with `MessageDigest.isEqual`.
- **Secret derivation:**
  `hex(HmacSHA256(key = fullKey.getBytes(UTF_8), "ticket-tool plugin signing v1".getBytes(UTF_8)))`,
  then use `secretHex.getBytes(UTF_8)` (the hex string's bytes, not the decoded
  bytes) as the HMAC key for messages.
- **Threads:** one dedicated daemon thread (or a virtual thread on 21+) runs the
  poll loop. Never touch Bukkit API from it. To read game state, hop onto the
  right scheduler and complete a `CompletableFuture`:
  - Paper/Spigot: `Bukkit.getScheduler().runTask(plugin, ...)`.
  - Folia: `Bukkit.getGlobalRegionScheduler().execute(plugin, ...)` for global
    state; a player's region via `player.getScheduler().run(...)`. Detect Folia
    with `Class.forName("io.papermc.paper.threadedregions.RegionizedServer")`.
  - Offline players: `Bukkit.getOfflinePlayer(uuid)` gives `getLastPlayed()` /
    `getFirstPlayed()` (epoch ms, 0 when unknown). On Paper prefer
    `getLastSeen()`.
  - Give each answer a budget of about 3 s from receipt, capped by `expiresAt`,
    which is usually 4 to 5 s after queueing and can be about 1.5 s.
- **Ban providers** (soft-depend on all three; detect in `onEnable`, prefer in
  this order, report the found ones in `/hello` `banProviders`):
  - **LiteBans:** `litebans.api.Database.get().prepareStatement(...)` against
    `{bans}` with `uuid = ? AND active AND (until < 1 OR until > ?)`;
    columns `reason`, `banned_by_name`, `time`, `until` (ms, `-1`/`0` =
    permanent). Run off the main thread. `Database.get().isPlayerBanned(uuid, null)`
    is the cheap yes/no.
  - **AdvancedBan:** `me.leoko.advancedban.manager.PunishmentManager.get()`;
    `getBan(uuidString)` returns a `Punishment` with `getReason()`,
    `getOperator()`, `getStart()`, `getEnd()` (`-1` permanent). AdvancedBan keys
    players by UUID without dashes in online mode; use
    `UUIDManager.get().getUUID(name)` if unsure.
  - **LibertyBans:** `space.arim.libertybans.api.LibertyBans` via Bukkit
    `ServicesManager`; `getSelector().selectionBuilder().victim(PlayerVictim.of(uuid)).type(PunishmentType.BAN).build().getAllSpecificPunishments()`
    (returns a `ReactionStage`; convert with `toCompletableFuture()`). Fields:
    `getReason()`, `getOperator()` (resolve console to `"Console"`),
    `getStartDate()`, `getEndDate()` (`Instant.MAX`/permanent check via
    `isPermanent()`).
  - **Vanilla:** `Bukkit.getBanList(BanList.Type.PROFILE)` (1.20.2+;
    `Type.NAME` before) and `getBanEntry(...)`: `getReason()`, `getSource()`,
    `getCreated()`, `getExpiration()`.
  - Map to `bans` with `provider` set to `litebans`, `advancedban`,
    `libertybans` or `vanilla`. Cap `reason` at 2,000 characters and strip
    color codes (`ChatColor.stripColor` / `§.` and `&.`).
- **`/ttlink <code>`:** register as a players-only command (reject
  `ConsoleCommandSender` and command blocks). Take `uuid` and `name` from the
  `Player` object, never from arguments. Accept the code with or without the
  dash, case-insensitive; send it as typed (the server normalizes). Cool down
  per player (one attempt per 5 s) on top of the server-side limit. Messages:
  linked → "Linked to Discord user <username>."; `invalid_code` → "That code is
  wrong or has expired. Make a new one on your Linked accounts page.";
  `unknown_player` → "Only Java Edition accounts can be linked.";
  `unavailable` with `retryable` → retry once after 3 s, then "Couldn't reach
  Mojang. Try /ttlink again in a minute." (the code is still good).
- **Proxies:** on Velocity/BungeeCord, install on each backend (one key each),
  not the proxy, so bans and last-seen come from where they're stored. Set
  `profile.server` from config (`server-name:`), default to the key name. A
  proxy edition can come later; protocol-wise it is the same client.
- **Config (`plugins/TicketTool/config.yml`):** `key:`, optional
  `server-name:`, optional `base-url:` (default
  `https://api.ticket-tool.app/api/plugin/v1`, for staging), `debug: false`.
  `/tickettool reload` re-reads config, sends `/goodbye` for the old key if it
  changed, then `/hello`. `/tickettool status` prints connected or the last
  error. Permission `tickettool.admin` for both.
- **Shading:** relocate anything shaded (`com.tickettool.sdk.shaded`); prefer
  not to shade at all.

Sketch:

```java
final class Signer {
  private final byte[] secret; // hex string bytes
  Signer(String fullKey) {
    this.secret = hex(hmac(fullKey.getBytes(UTF_8),
        "ticket-tool plugin signing v1".getBytes(UTF_8))).getBytes(UTF_8);
  }
  String sign(String method, String endpoint, long ts, String nonce, byte[] body) {
    String canonical = String.join("\n", "v1", method, endpoint,
        Long.toString(ts), nonce, hex(sha256(body)));
    return "v1=" + hex(hmac(secret, canonical.getBytes(UTF_8)));
  }
}
```

## TypeScript SDK (`@ticket-tool/plugin-sdk`)

For Node 18+ servers (custom game servers, bots, test harnesses). No runtime
dependencies: `node:crypto` and global `fetch`.

```ts
import { createHash, createHmac, randomBytes, timingSafeEqual } from "node:crypto";

const BASE = "https://api.ticket-tool.app/api/plugin/v1";
const hex = (b: Buffer) => b.toString("hex");

export function signingSecret(fullKey: string): string {
  return hex(createHmac("sha256", fullKey).update("ticket-tool plugin signing v1").digest());
}

function canonical(method: string, endpoint: string, ts: string, nonce: string, body: Buffer) {
  return ["v1", method, endpoint, ts, nonce, hex(createHash("sha256").update(body).digest())].join("\n");
}

export async function call(key: string, endpoint: string, payload: unknown, signal?: AbortSignal) {
  const keyId = key.slice(4, 20);
  const secret = signingSecret(key);
  const body = Buffer.from(JSON.stringify(payload ?? {}));
  const ts = String(Math.floor(Date.now() / 1000));
  const nonce = randomBytes(16).toString("base64url");
  const sig = hex(createHmac("sha256", secret).update(canonical("POST", endpoint, ts, nonce, body)).digest());
  const res = await fetch(BASE + endpoint, {
    method: "POST",
    body,
    signal,
    headers: {
      "content-type": "application/json",
      "x-tt-key-id": keyId,
      "x-tt-timestamp": ts,
      "x-tt-nonce": nonce,
      "x-tt-signature": `v1=${sig}`,
      "x-tt-protocol": "1",
      "x-tt-plugin": "my-plugin/0.1.0",
    },
  });
  const raw = Buffer.from(await res.arrayBuffer());
  const rs = res.headers.get("x-tt-signature");
  if (rs) {
    const rts = res.headers.get("x-tt-timestamp") ?? "";
    if (res.headers.get("x-tt-nonce") !== nonce) throw new Error("reply nonce mismatch");
    if (Math.abs(Date.now() / 1000 - Number(rts)) > 300) throw new Error("stale reply");
    const want = Buffer.from(`v1=${hex(createHmac("sha256", secret).update(canonical("RESPONSE", endpoint, rts, nonce, raw)).digest())}`);
    const got = Buffer.from(rs);
    if (want.length !== got.length || !timingSafeEqual(want, got)) throw new Error("bad reply signature");
  }
  return { status: res.status, json: JSON.parse(raw.toString("utf8")) as Record<string, unknown> };
}
```

The poll loop is `while (running) { const r = await call(key, "/poll", {}, AbortSignal.timeout(35_000)); for (const q of r.json.requests ?? []) void answer(q); }`
with the backoff from the docs on errors. Export the message types generated
from the server's Zod schemas (`pluginRespondSchema`, `pluginLinkSchema`,
`pluginHelloSchema`) so the two never drift; the repo should vendor a JSON
Schema export of them per protocol version.

## Test vectors

All from `packages/shared/src/game-plugins/__tests__/signing.test.ts`; every
SDK should ship these as unit tests.

| Case | Input | Expected |
| --- | --- | --- |
| Signing secret | key `ttp_0123456789abcdef.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA` | `20998da3e0f449ee48f630226afec7b5991112ac4233721a30ba008bb50d8a9c` |
| Body hash | `{}` | `44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a` |
| Request signature | `POST /poll`, body `{}`, ts `1760529600`, nonce `abcdefghijklmnop` | `v1=d71627ae221c55861c68c1e5d230d7faec74072f75d952ccaa58a2b67b608d3d` |
| Reply signature | `RESPONSE /poll`, body `{"ok":true,"requests":[]}`, ts `1760529601`, nonce `abcdefghijklmnop` | `v1=57e67ded8456ec7d4a98676cfbd5d346e3e0d5d6a1e74f9685fb00496cc3837d` |

## Conformance checklist

A plugin is ready to list when it:

- [ ] Passes the four test vectors above.
- [ ] Uses a fresh nonce per request, including retries, and never logs the key.
- [ ] Verifies reply signatures and nonces, and refuses unsigned success replies.
- [ ] Sends `/hello` on start and reload, `/goodbye` on shutdown.
- [ ] Keeps exactly one poll open, re-polls immediately, backs off 1 → 60 s with jitter on errors.
- [ ] Stops on `invalid_key`; retries hourly on `plan_not_entitled`; corrects clock skew from `serverTime`.
- [ ] Answers on worker threads (or the right Folia scheduler), within about 3 s, and drops expired requests.
- [ ] Never touches game API off the main/region thread.
- [ ] Reports bans from LiteBans, AdvancedBan, LibertyBans or vanilla in the documented shape, reasons stripped of color codes and capped at 2,000 characters.
- [ ] Sends no IP addresses, chat logs or anything beyond the documented fields.
- [ ] Runs `/ttlink` for players only, with uuid and name from the player object, and a per-player cooldown.
- [ ] Ignores unknown fields, request types and `include` parts.
- [ ] Carries out `player.action` only for actions the owner turned on, on the main or region thread, and answers `not_allowed`, `player_offline` or `not_banned` instead of guessing.
- [ ] Shows staff messages and kick reasons as plain text, never as a command or with color codes applied.
- [ ] Sends `/event` only for players who ran the report command, with the player taken from the command's sender, and a per-player cooldown.
- [ ] Opens no inbound port and makes no connection except to the configured base URL.
