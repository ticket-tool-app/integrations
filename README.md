<p align="center">
  <img src="logo.svg" alt="Ticket Tool" width="80" height="80">
  <br><br>
  <strong>Ticket Tool integrations</strong>
  <br>
  Open-source game server plugins for Ticket Tool, and the SDKs they are built on.
  <br><br>
  <a href="https://ticket-tool.app/docs"><img src="https://img.shields.io/badge/Docs-ticket--tool.app-0b0b0b" alt="Documentation"></a>
  <a href="https://ticket-tool.app/discord"><img src="https://img.shields.io/badge/Support-Discord-5865F2?logo=discord&logoColor=white" alt="Support server"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-0b0b0b" alt="MIT license"></a>
  <br>
  <img src="https://img.shields.io/badge/Java%2021-ED8B00?logo=openjdk&logoColor=white" alt="Java 21">
  <img src="https://img.shields.io/badge/Paper-1.20.6%2B-444444" alt="Paper 1.20.6+">
  <img src="https://img.shields.io/badge/Folia-supported-444444" alt="Folia supported">
  <img src="https://img.shields.io/badge/Gradle-02303A?logo=gradle&logoColor=white" alt="Gradle">
</p>

---

## About

A game plugin connects your game server to one Discord server. When someone opens a ticket, Ticket Tool asks the plugin about the opener's linked player and shows staff what it says: name, UUID, whether they're online, when they were last seen and any active ban. Players link their game account to Discord by typing a short code in game.

The plugin only makes outbound HTTPS requests. Ticket Tool never connects to your game server, its database or your ban plugin.

[ticket-tool.app](https://ticket-tool.app) · [Dashboard](https://dash.ticket-tool.app) · [Documentation](https://ticket-tool.app/docs)

## What's here

| Path | What it is |
| --- | --- |
| [`minecraft/`](minecraft/README.md) | **Ticket Tool Link**, the Minecraft plugin for Paper, Folia and Spigot 1.20.6+ |
| [`sdk-java/`](sdk-java/README.md) | `ticket-tool-plugin-sdk`, a dependency-free Java 21 client for the protocol |
| [`sdk-ts/`](sdk-ts/README.md) | TypeScript SDK (planned) |
| [`spec/`](spec/README.md) | The protocol spec and shared test vectors every SDK must pass |

## Quick start for server owners

1. On the Ticket Tool dashboard, go to **Automations → Connections → Game server plugins** and create a Minecraft key. Copy it: it's only shown once.
2. Download `TicketToolLink-<version>.jar` from the [latest release](https://github.com/ticket-tool-app/integrations/releases/latest), drop it into your server's `plugins` folder and start the server once.
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

## Contributing

Pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for how to build, test and propose a change.

## Support

| For | Go to |
| --- | --- |
| Questions and setup help | [Support server](https://ticket-tool.app/discord) or [ticket-tool.app/support](https://ticket-tool.app/support) |
| Plugin bugs and feature requests | [Open an issue](https://github.com/ticket-tool-app/integrations/issues) |
| Bugs in Ticket Tool itself | [ticket-tool-app/community](https://github.com/ticket-tool-app/community/issues) |
| Security vulnerabilities | [SECURITY.md](SECURITY.md). Do not open a public issue. |

## Links

[Website](https://ticket-tool.app) · [Documentation](https://ticket-tool.app/docs) · [Discord](https://ticket-tool.app/discord) · [X](https://x.com/TicketTool11285) · [YouTube](https://www.youtube.com/@TicketTool-App)

## License

MIT. Copyright (c) JTF Labs Pty Ltd. See [LICENSE](LICENSE).
