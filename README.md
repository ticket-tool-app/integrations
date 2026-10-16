# Ticket Tool integrations

Open-source game server plugins for [Ticket Tool](https://ticket-tool.app), and the SDKs they are built on.

A game plugin connects your game server to one Discord server. When someone opens a ticket, Ticket Tool asks the plugin about the opener's linked player and shows staff what it says: name, UUID, whether they're online, when they were last seen and any active ban. Players link their game account to Discord by typing a short code in game.

The plugin only makes outbound HTTPS requests. Ticket Tool never connects to your game server, its database or your ban plugin.

## What's here

| Path | What it is |
| --- | --- |
| [`minecraft/`](minecraft/README.md) | **Ticket Tool Link**, the Minecraft plugin for Paper, Folia and Spigot 1.20.6+ |
| [`sdk-java/`](sdk-java/README.md) | `ticket-tool-plugin-sdk`, a dependency-free Java 21 client for the protocol |
| [`sdk-ts/`](sdk-ts/README.md) | TypeScript SDK (planned) |
| [`spec/`](spec/README.md) | The protocol spec and shared test vectors every SDK must pass |

## Quick start for server owners

1. On the Ticket Tool dashboard, go to **Automations → Connections → Game server plugins** and create a Minecraft key. Copy it: it's only shown once.
2. Drop `TicketToolLink-<version>.jar` into your server's `plugins` folder and start the server once.
3. Paste the key into `plugins/TicketToolLink/config.yml`, then run `/ticketlink reload`.

The full guide, including ban plugins and networks, is in [minecraft/README.md](minecraft/README.md).

## Not in version 1

- **Proxy plugin.** On Velocity or BungeeCord, install the plugin on each backend with its own key, not on the proxy. A proxy edition may come later.
- **Bedrock players.** Geyser/Floodgate accounts can't link, because Ticket Tool verifies every UUID with Mojang.
- **Offline-mode servers.** Same reason: offline-mode UUIDs aren't real Java Edition accounts. Behind a proxy that forwards Mojang UUIDs, backends in offline mode are fine.

## Building

Java 21 is required. Everything else is fetched by the Gradle wrapper.

```bash
./gradlew build
```

The plugin jar lands in `minecraft/build/libs/`, with the SDK inside it.

## Contributing and security

See [CONTRIBUTING.md](CONTRIBUTING.md). Please report vulnerabilities privately as described in [SECURITY.md](SECURITY.md), not in a public issue.

## License

[MIT](LICENSE)
