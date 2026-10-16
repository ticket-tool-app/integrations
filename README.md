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
  <a href="https://github.com/ticket-tool-app/integrations/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/ticket-tool-app/integrations/ci.yml?branch=main&label=CI" alt="CI"></a>
  <a href="https://github.com/ticket-tool-app/integrations/releases?q=minecraft"><img src="https://img.shields.io/github/v/release/ticket-tool-app/integrations?filter=minecraft-v*&label=Minecraft&color=444444" alt="Minecraft release"></a>
  <a href="https://github.com/ticket-tool-app/integrations/releases?q=fivem"><img src="https://img.shields.io/github/v/release/ticket-tool-app/integrations?filter=fivem-v*&label=FiveM%20%2F%20RedM&color=444444" alt="FiveM and RedM release"></a>
  <a href="https://github.com/ticket-tool-app/integrations/releases?q=gmod"><img src="https://img.shields.io/github/v/release/ticket-tool-app/integrations?filter=gmod-v*&label=Garry%27s%20Mod&color=444444" alt="Garry's Mod release"></a>
</p>

---

## About

A game plugin connects your game server to one Discord server. When someone opens a ticket, Ticket Tool asks the plugin about the opener's linked player and shows staff what it says: name, UUID, whether they're online, when they were last seen and any active ban. Minecraft players link their account by typing a short code in game; FiveM and RedM players are matched by their Discord account, and Garry's Mod, Rust and CS2 players by the Steam account they linked to Discord.

The plugin only makes outbound HTTPS requests. Ticket Tool never connects to your game server, its database or your ban plugin.

[ticket-tool.app](https://ticket-tool.app) · [Dashboard](https://dash.ticket-tool.app) · [Documentation](https://ticket-tool.app/docs)

## What's here

| Path | What it is |
| --- | --- |
| [`minecraft/`](minecraft/README.md) | **Ticket Tool Link** for Paper, Folia and Spigot 1.20.6+ |
| [`fivem/`](fivem/README.md) | **Ticket Tool Link** for FiveM and RedM, one resource for both |
| [`gmod/`](gmod/README.md) | **Ticket Tool Link** for Garry's Mod, a pure Lua addon |
| [`rust/`](rust/README.md) | **Ticket Tool Link** for Rust, a single-file Oxide and Carbon plugin |
| [`cs2/`](cs2/README.md) | **Ticket Tool Link** for Counter-Strike 2 on CounterStrikeSharp |
| [`sdk-java/`](sdk-java/README.md) | `ticket-tool-plugin-sdk`, a dependency-free Java 21 client for the protocol |
| [`sdk-ts/`](sdk-ts/README.md) | `@ticket-tool/plugin-sdk`, a dependency-free Node client for the protocol |
| [`sdk-dotnet/`](sdk-dotnet/README.md) | `TicketTool.PluginSdk`, a dependency-free .NET client for the protocol |
| [`spec/`](spec/README.md) | The protocol spec and shared test vectors every SDK must pass |

## Quick start for server owners

1. On the Ticket Tool dashboard, go to **Automations → Connections → Game server plugins** and create a key for your game. Copy it: it's only shown once.
2. Download the plugin and follow its guide:

| Game | Download | Guide |
| --- | --- | --- |
| Minecraft | [`TicketToolLink-<version>.jar`](https://github.com/ticket-tool-app/integrations/releases?q=minecraft) | [minecraft/README.md](minecraft/README.md) |
| FiveM and RedM | [`ticket-tool-link-fivem-<version>.zip`](https://github.com/ticket-tool-app/integrations/releases?q=fivem) | [fivem/README.md](fivem/README.md) |
| Garry's Mod | [`ticket_tool_link-gmod-<version>.zip`](https://github.com/ticket-tool-app/integrations/releases?q=gmod) | [gmod/README.md](gmod/README.md) |
| Rust (Oxide, Carbon) | [`TicketToolLink-rust-<version>.cs`](https://github.com/ticket-tool-app/integrations/releases?q=rust) | [rust/README.md](rust/README.md) |
| Counter-Strike 2 | [`TicketToolLink-cs2-<version>.zip`](https://github.com/ticket-tool-app/integrations/releases?q=cs2) | [cs2/README.md](cs2/README.md) |

Every release has a `.sha256` checksum next to it. What each plugin doesn't support yet is listed in its guide.

## Building

| Path | Needs | Build and test |
| --- | --- | --- |
| `minecraft/`, `sdk-java/` | Java 21 (Gradle comes with the wrapper) | `./gradlew build` |
| `fivem/`, `sdk-ts/` | Node 20+ | `npm ci && npm test` in the folder |
| `gmod/` | LuaJIT or Lua 5.1 | `luajit tests/run.lua` in `gmod/` |
| `sdk-dotnet/`, `rust/`, `cs2/` | .NET 8 SDK | see each folder's README |

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
