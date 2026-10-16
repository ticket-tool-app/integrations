# Protocol spec

- [PROTOCOL.md](PROTOCOL.md): Ticket Tool game plugin protocol, version 1. The protocol sections match the public docs page at https://ticket-tool.app/docs/integrations/plugin-protocol.
- [test-vectors/v1.json](test-vectors/v1.json): signing secret, body hash, request and reply signatures. Every SDK loads this file in its tests.

## Where this repo's implementation differs from the SDK notes

The "SDK notes" section of PROTOCOL.md was written before the repo existed. The protocol itself is followed exactly; these implementation choices differ from the notes:

| Notes say | This repo does | Why |
| --- | --- | --- |
| Target Java 17, JSON via the platform's Gson | Java 21, no JSON library: a small codec in `sdk-java` | Paper 1.20.6+ requires Java 21 anyway, and zero dependencies keeps the SDK usable outside Bukkit |
| Default base URL `https://api.ticket-tool.app/api/plugin/v1` | Default `https://dash.ticket-tool.app`, with `/api/plugin/v1` appended | Both hosts serve the endpoints; `api-url` accepts either form |
| Config in `plugins/TicketTool/`, commands `/tickettool reload\|status` | Plugin named `TicketToolLink`, so `plugins/TicketToolLink/`; `/ticketlink status\|reload` with `/tickettool` as an alias | The plugin's name is "Ticket Tool Link" |
| Relocate shaded code to `com.tickettool.sdk.shaded` | SDK classes included as-is | The SDK is our own code with no dependencies; there is nothing third-party to relocate |
