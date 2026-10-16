# @ticket-tool/plugin-sdk (TypeScript)

A client for the [Ticket Tool game plugin protocol](../spec/PROTOCOL.md), version 1. Node 16 or newer, no runtime dependencies: it uses `node:crypto` and `node:https` (not `fetch`, which FXServer's bundled Node 16 doesn't have), and builds to CommonJS.

It handles the protocol; you supply the game-specific answers. It behaves like the [Java SDK](../sdk-java):

- Signs every request (HMAC-SHA256 over method, endpoint, timestamp, nonce and body hash) with a fresh nonce, including retries, and hashes the exact bytes it sends.
- Verifies every reply's signature, nonce and timestamp, and refuses unsigned success replies.
- Sends `/hello`, then keeps exactly one long-poll open and polls again straight away after each reply.
- Answers each request off the poll loop, drops expired requests, and sends a timeout error if your handler takes longer than its budget (3 s by default, capped by the request's `expiresAt`).
- Backs off 1, 2, 4 … 60 s with jitter on network errors and `5xx`, waits out `too_many_polls` and `rate_limited`, retries hourly on `plan_not_entitled`, stops for good on `invalid_key`, `wrong_plugin_type` and `unsupported_protocol`, and corrects clock skew from a `stale_timestamp` error's `serverTime`.
- Sends `/goodbye` on `stop()`, waiting at most about 3 s.
- Never logs or exposes the key: `String(key)`, `JSON.stringify` and `util.inspect` all show `ttp_<keyId>`.

## Usage

```ts
import { LookupResult, TicketToolClient } from "@ticket-tool/plugin-sdk";

const client = new TicketToolClient({
  key: process.env.TICKET_TOOL_KEY!,          // the full ttp_... key
  plugin: { type: "fivem", name: "my-plugin", version: "1.0.0", platform: "FiveM b3095" },
  banProviders: () => ["txadmin"],            // informational, sent in /hello
  logger: { debug() {}, info: console.log, warn: console.warn },
  handler: async (request) => {
    // request.player has uuid, steamId, discordId and name; read the one your type uses.
    const player = await findPlayer(request.player.discordId);
    if (!player) return LookupResult.failure("unknown_player");
    return LookupResult.answer(
      request.wantsProfile
        ? { discordId: request.player.discordId, name: player.name, online: true, fields: [{ label: "Job", value: player.job }] }
        : null,
      request.wantsBans ? { banned: false, provider: "server" } : null,
    );
  },
});

client.start();                                         // returns at once
const result = await client.link({ uuid, name }, code); // LinkResult, never rejects
await client.stop();                                    // sends /goodbye
```

`apiUrl` defaults to `https://dash.ticket-tool.app`; it accepts a host root or a full base ending in `/api/plugin/v1`. Plain `http://` is only allowed for `localhost`, for development.

`client.status()` returns a snapshot (state, last error phrased for an admin, the `/hello` reply, lookups answered, clock offset) for a status command.

The SDK applies the protocol's limits when it writes an answer: names up to 64 characters, at most 6 `fields` (labels 32, values 100), ban reasons up to 2,000 characters, and a `steamId` or `discordId` that isn't well formed is left out. Color codes are the plugin's business; `stripCfxColors` and `stripMinecraftColors` are exported for that.

## Tests

```sh
npm ci
npm test            # shared test vectors, message mapping, and client tests against a fake server
npm run build
npm run smoke       # the built dist/ against the test vectors, runnable on Node 16
```

`test/fake-server.ts` is a protocol v1 server that checks signatures, the clock window and nonce reuse the way Ticket Tool does, and signs its replies. The FiveM resource's end-to-end test uses it too.

## Publishing

Not on npm yet. The FiveM resource inlines the SDK at build time, so it doesn't need a published package.
