# Contributing

Thanks for helping out. Bug reports, fixes and support for more ban plugins are all welcome.

## Setup

Each plugin builds on its own. For the Minecraft plugin and the Java SDK you need Java 21:

```bash
./gradlew build      # compile, test, and build the plugin jar
./gradlew test       # tests only
```

To try the plugin, copy `minecraft/build/libs/TicketToolLink-*.jar` into a Paper 1.20.6+ test server. For local work against a development Ticket Tool, set `api-url` in `config.yml` to `http://localhost:3001`; plain `http://` is only accepted for localhost.

For FiveM/RedM and the TypeScript SDK you need Node 20+: run `npm ci && npm test` in `fivem/` or `sdk-ts/`. For Garry's Mod you need LuaJIT or Lua 5.1: run `luajit tests/run.lua` in `gmod/`.

## Ground rules

- **The SDKs have no runtime dependencies.** The Java SDK uses only the JDK and its own small JSON codec, and the TypeScript SDK only Node's built-ins, so neither can clash with whatever the host platform ships. Test-only dependencies are fine.
- **Match the protocol.** [spec/PROTOCOL.md](spec/PROTOCOL.md) is the contract. Every SDK must pass [spec/test-vectors/v1.json](spec/test-vectors/v1.json), and the plugin must meet the conformance checklist at the end of the spec.
- **Never touch game state off its thread.** Use the `Scheduler` abstraction in `minecraft/.../platform`, which does the right thing on Paper, Folia and Spigot.
- **Never log the key.** Log `ttp_<keyId>` at most.
- **Send only documented fields.** No IP addresses, chat logs or anything else the protocol doesn't define.
- **US English** in code, comments, docs and messages (`color`, not `colour`).
- **Comments explain why**, not what: a non-obvious invariant, a platform quirk, a deliberate trade-off. Skip comments that restate the code or narrate history; that's what commit messages are for.

## Adding a ban plugin

Ban plugins are called through reflection (see `minecraft/.../bans/`), so the build needs none of their APIs and the jar loads whether or not they're installed. Add a `BanProvider`, register it in `BanProviders.choose`, add the plugin to `softdepend` in `plugin.yml`, and use a protocol `provider` id the server knows (`litebans`, `advancedban`, `libertybans`, `vanilla`). A new id needs a matching change on the Ticket Tool side first.

## Pull requests

Keep them focused, include tests for SDK changes, and make sure the build and tests for the folder you changed pass. CI runs the same commands.

## Releases

Push a tag `minecraft-vMAJOR.MINOR.PATCH` on `main` and the Release Minecraft workflow builds the plugin at that version and publishes `TicketToolLink-<version>.jar` with its SHA-256 checksum to GitHub Releases. A tag with a suffix such as `minecraft-v1.1.0-beta.1` publishes as a pre-release. The FiveM/RedM resource and the Garry's Mod addon release the same way from `fivem-v*` and `gmod-v*` tags. Each plugin has its own tag prefix, so one repo can release plugins independently.
