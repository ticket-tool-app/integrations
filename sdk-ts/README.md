# @ticket-tool/plugin-sdk (TypeScript)

A client for the [Ticket Tool game plugin protocol](../spec/PROTOCOL.md), version 1, for Node 20.3+ game servers, bots and test harnesses. No runtime dependencies: `node:crypto` and global `fetch`.

It handles the protocol the same way the [Java SDK](../sdk-java/README.md) does: signed requests with a fresh nonce each time, verified replies (unsigned successes refused), `/hello` then one long-poll at a time, answers that never hold up the poll and are dropped once expired, backoff with jitter, a one-minute wait on `rate_limited`, hourly retries on `plan_not_entitled`, a hard stop on `invalid_key`, clock-skew correction and `/goodbye` on `stop()`. It never logs the key; `client.keyLabel` is `ttp_<keyId>`.

## Usage

```ts
import { TicketToolClient } from "@ticket-tool/plugin-sdk";

const client = new TicketToolClient({
  key: process.env.TICKET_TOOL_KEY!, // the full ttp_... key
  plugin: { type: "fivem", name: "my-plugin", version: "1.0.0", platform: "FXServer" },
  banProviders: () => ["txadmin"],
  logger: console,
  handler: async (request) => {
    // request.player carries the game's identity fields; request.include says what to send.
    return { ok: true, profile: request.include.includes("profile") ? await readProfile(request.player) : undefined };
  },
});

client.start();
const result = await client.link(typedCode, { discordId: "…" }); // never rejects
await client.stop();
```

`apiUrl` defaults to `https://dash.ticket-tool.app` and accepts a host root or a full base ending in `/api/plugin/v1`. Plain `http://` is only allowed for localhost.

## Tests

```bash
npm install && npm test
```

The tests run the shared [test vectors](../spec/test-vectors/v1.json) and drive the client against a fake Ticket Tool that checks every signature and nonce.
