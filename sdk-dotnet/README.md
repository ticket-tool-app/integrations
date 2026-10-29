# TicketTool.PluginSdk (.NET)

A client for the [Ticket Tool game plugin protocol](../spec/PROTOCOL.md), version 1. It targets **netstandard2.1** (Oxide and Carbon on the Rust server's Mono) and **net8.0** (CounterStrikeSharp), with no dependencies beyond the base class library: it uses `HttpClient` (or `HttpWebRequest`), `HMACSHA256`, `RandomNumberGenerator` and a small built-in JSON codec. It is written in C# 7.3 so the Rust plugin can inline it and Oxide's compiler still accepts it.

It handles the protocol; you supply the game-specific answers.

- Signs every request (HMAC-SHA256 over method, endpoint, timestamp, nonce and body hash) with a fresh 16-byte nonce, including retries.
- Verifies every reply's signature, nonce and timestamp, and refuses unsigned success replies.
- Sends `/hello`, then keeps exactly one long-poll open and polls again straight away after each reply.
- Answers each request on the thread pool (never the poll loop), drops expired requests, and sends a timeout error if your handler takes longer than its budget (3 s by default, capped by the request's `expiresAt`).
- Checks the request carries the identity your plugin type is looked up by (`steamId` for gmod, rust and cs2; `discordId` for fivem; `uuid` for minecraft), fills it into the profile, and refuses to send a profile for a different player.
- Backs off 1, 2, 4 … 60 s with jitter on network errors and `5xx`, waits out `too_many_polls` and `rate_limited`, retries hourly on `plan_not_entitled`, stops for good on `invalid_key`, `wrong_plugin_type` and `unsupported_protocol`, and corrects clock skew from a `stale_timestamp` error's `serverTime`.
- Sends `/goodbye` on `Stop()`, waiting at most about 3 s.
- Never logs or exposes the key; `PluginKey.ToString()` is `ttp_<keyId>`.

`/link` is Minecraft-only in protocol v1, so this SDK doesn't send it.

## Usage

```csharp
var client = new TicketToolClient(new TicketToolClientOptions
{
    Key = config.Key,                                   // the full ttp_... key
    ApiUrl = config.ApiUrl,                             // defaults to https://dash.ticket-tool.app
    Plugin = new PluginInfo("cs2", "my-plugin", "1.0.0", "CounterStrikeSharp 1.0.368"),
    BanProviders = () => new[] { "server" },            // informational, sent in /hello
    Logger = mySdkLogger,
    Handler = (request, cancellation) =>
    {
        // Called on a pool thread. Hop onto the game thread to read state and
        // complete the task from there.
        var answer = new TaskCompletionSource<LookupResult>();
        game.RunOnMainThread(() => answer.SetResult(LookupResult.Answer(
            request.WantsProfile ? ReadProfile(request.Player.SteamId) : null,
            request.WantsBans ? ReadBans(request.Player.SteamId) : null)));
        return answer.Task;
    },
});

client.Start();   // returns at once
// ...
client.Stop();    // sends /goodbye
```

`PlayerProfile.AddField(label, value)` adds up to six lines to the staff card (labels up to 32 characters, values up to 100). `BanInfo` strips Unity rich-text tags and Source chat color codes and caps reasons at 2,000 characters.

Every `await` inside the SDK uses `ConfigureAwait(false)`, so nothing is posted back to a game's synchronization context, and the blocking `Stop()` is safe to call from a game's main thread.

### Player actions and reports

Both are off unless the plugin turns them on, and a plugin should only do that when the server owner did:

```csharp
new TicketToolClientOptions
{
    // ...as above
    ActionHandler = (action, cancellation) =>
    {
        // action.Kind is message, warn, kick or unban; action.Text is the staff text.
        var done = new TaskCompletionSource<ActionResult>();
        game.RunOnMainThread(() => done.SetResult(Kick(action.Player.SteamId, action.Text)));
        return done.Task;
    },
    Actions = () => config.Actions,          // the kinds the owner turned on; read at each /hello
    ReportsEnabled = () => config.Reports,
};

ReportResult result = await client.ReportAsync(new RequestPlayer(null, steamId, null, name), "Someone is flying");
```

The SDK answers `not_allowed` itself for a kind that isn't in `Actions`, without calling the handler, so a bug in the plugin can't run an action the owner left off. Call `client.RefreshHello()` after the owner changes either setting.

`client.Status` returns a `ClientStatus` snapshot (state, last error phrased for an admin, the `/hello` reply, lookups answered, clock offset) for a status command.

### HTTP

The default sender is `HttpClientSender`. `WebRequestSender` uses `HttpWebRequest` instead, which lives in `System.dll`: Oxide compiles plugins against a fixed set of assemblies that may not include `System.Net.Http`, and Oxide's own web request library doesn't expose the reply headers the signature check needs. Pass either as `Sender`.

## Tests

```bash
dotnet test sdk-dotnet/tests/TicketTool.PluginSdk.Tests
dotnet test sdk-dotnet/tests/TicketTool.PluginSdk.Tests -p:SdkTargetFramework=netstandard2.1
```

These run the shared [test vectors](../spec/test-vectors/v1.json) plus client tests against a fake Ticket Tool server (`FakeTicketTool`) that checks signatures, the clock window and nonce reuse the way the real one does, with both senders. The second command runs the same tests against the netstandard2.1 build.
