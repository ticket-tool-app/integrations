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
  <a href="https://github.com/ticket-tool-app/integrations/releases?q=minecraft"><img src="https://img.shields.io/github/v/release/ticket-tool-app/integrations?filter=minecraft-v*&label=Minecraft&color=62B47A" alt="Minecraft release"></a>
  <a href="https://github.com/ticket-tool-app/integrations/releases?q=fivem"><img src="https://img.shields.io/github/v/release/ticket-tool-app/integrations?filter=fivem-v*&label=FiveM%20%2F%20RedM&color=F40552" alt="FiveM and RedM release"></a>
  <a href="https://github.com/ticket-tool-app/integrations/releases?q=gmod"><img src="https://img.shields.io/github/v/release/ticket-tool-app/integrations?filter=gmod-v*&label=Garry%27s%20Mod&color=1194F0" alt="Garry's Mod release"></a>
</p>

---

## About

A game plugin connects your game server to one Discord server. When someone opens a ticket, Ticket Tool asks the plugin about the opener's player and shows staff what it says: name, whether they're online, when they were last seen, game details like rank or job, and any active ban.

The plugin only makes outbound HTTPS requests. Ticket Tool never connects to your game server, its database or your ban plugin.

[ticket-tool.app](https://ticket-tool.app) · [Dashboard](https://dash.ticket-tool.app) · [Documentation](https://ticket-tool.app/docs)

## Plugins

| Game | Download | Players matched by | Ban sources | Guide |
| --- | --- | --- | --- | --- |
| Minecraft (Paper, Folia, Spigot 1.20.6+) | [`TicketToolLink-<version>.jar`](https://github.com/ticket-tool-app/integrations/releases?q=minecraft) | `/ttlink` code in game | LiteBans, AdvancedBan, LibertyBans, vanilla | [minecraft/](minecraft/README.md) |
| FiveM and RedM | [`ticket-tool-link-fivem-<version>.zip`](https://github.com/ticket-tool-app/integrations/releases?q=fivem) | Discord ID from Cfx.re, no linking | txAdmin, EasyAdmin, QBCore bans | [fivem/](fivem/README.md) |
| Garry's Mod | [`ticket_tool_link-gmod-<version>.zip`](https://github.com/ticket-tool-app/integrations/releases?q=gmod) | Steam account linked to Discord | ULX, SAM, `banned_user.cfg` | [gmod/](gmod/README.md) |

Every plugin needs a key from the dashboard: **Automations → Connections → Game server plugins**. Each guide covers setup, settings and troubleshooting.

## SDKs

| Path | What it is |
| --- | --- |
| [`sdk-java/`](sdk-java/README.md) | Java 21 client, used by the Minecraft plugin |
| [`sdk-ts/`](sdk-ts/README.md) | TypeScript client for Node 16+, used by the FiveM resource |
| [`spec/`](spec/README.md) | The protocol spec and the test vectors every SDK must pass |

Each plugin builds on its own; see its README.

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
