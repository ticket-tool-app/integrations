# ticket-tool-plugin-sdk (Java)

A client for the [Ticket Tool game plugin protocol](../spec/PROTOCOL.md), version 1. Java 21, no runtime dependencies: it uses `java.net.http`, `javax.crypto` and a small built-in JSON codec, so it won't clash with the Gson or Jackson your platform ships.

It handles the protocol; you supply the game-specific answers.

- Signs every request (HMAC-SHA256 over method, endpoint, timestamp, nonce and body hash) with a fresh nonce, including retries.
- Verifies every reply's signature, nonce and timestamp, and refuses unsigned success replies.
- Sends `/hello`, then keeps exactly one long-poll open on a dedicated thread and polls again straight away after each reply.
- Answers each request on a worker thread (never the poll thread), drops expired requests, and sends a timeout error if your handler takes longer than its budget (3 s by default, capped by the request's `expiresAt`).
- Backs off 1, 2, 4 … 60 s with jitter on network errors and `5xx`, waits out `too_many_polls` and `rate_limited`, retries hourly on `plan_not_entitled`, stops for good on `invalid_key`, and corrects clock skew from a `stale_timestamp` error's `serverTime`.
- Sends `/goodbye` on `stop()`, waiting at most about 3 s.
- Never logs or exposes the key; `PluginKey.toString()` is `ttp_<keyId>`.

## Usage

```java
TicketToolClient client = TicketToolClient.builder()
        .key(config.getString("key"))                     // the full ttp_... key
        .plugin(new PluginInfo("minecraft", "my-plugin", "1.0.0", "Paper 1.21.1"))
        .banProviders(() -> List.of("litebans"))          // informational, sent in /hello
        .logger(mySdkLogger)
        .handler(request -> {
            // Called on an SDK worker thread. Hop onto your game's thread to read state,
            // and complete the future from anywhere.
            CompletableFuture<LookupResult> answer = new CompletableFuture<>();
            game.runOnMainThread(() -> answer.complete(LookupResult.answer(
                    request.wantsProfile() ? readProfile(request.playerUuid()) : null,
                    request.wantsBans() ? readBans(request.playerUuid()) : null)));
            return answer;
        })
        .build();

client.start();                 // returns at once
// ...
client.link(player.uuid(), player.name(), typedCode)   // CompletableFuture<LinkResult>, never fails
        .thenAccept(result -> tell(player, result));
// ...
client.stop();                  // sends /goodbye
```

`apiUrl` defaults to `https://dash.ticket-tool.app`; it accepts a host root or a full base ending in `/api/plugin/v1`. Plain `http://` is only allowed for `localhost`, for development.

### Player actions and reports

Both are off unless the plugin turns them on, and a plugin should only do that when the server owner did:

```java
TicketToolClient.builder()
        // ...as above
        .actionHandler(action -> game.callForPlayer(action.playerUuid(), player -> {
            // action.kind() is message, warn, kick or unban; action.message() is the staff text.
            player.kick(action.message());
            return ActionResult.done();
        }))
        .actions(() -> settings.actions())          // the kinds the owner turned on; read at each /hello
        .reportsEnabled(() -> settings.reports())
        .build();

client.report(player.uuid(), player.name(), "Someone is flying", null, "survival") // never fails
        .thenAccept(result -> tell(player, result.outcome()));
```

The SDK answers `not_allowed` itself for a kind that isn't in `actions()`, without calling the handler, so a bug in the plugin can't run an action the owner left off. Call `client.refreshHello()` after the owner changes either setting.

`client.status()` returns a `ClientStatus` snapshot (state, last error phrased for an admin, the `/hello` reply, lookups answered, clock offset) for a status command.

## Tests

`./gradlew :sdk-java:test` runs the shared [test vectors](../spec/test-vectors/v1.json) plus client tests against a fake Ticket Tool server (`FakeTicketTool`) that checks signatures, the clock window and nonce reuse the way the real one does.
