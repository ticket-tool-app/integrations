# Security policy

## Reporting a vulnerability

Please email **security@ticket-tool.app** with the details. Don't open a public issue or pull request for a security problem.

Include what you found, how to reproduce it, and the version of the plugin or SDK. We'll confirm we received it, keep you posted while we fix it, and credit you in the release notes if you'd like.

## Scope

- The Minecraft plugin and the SDKs in this repository.
- The plugin protocol as described in [spec/PROTOCOL.md](spec/PROTOCOL.md), for example a way to forge a signature or replay a request.

Issues in the Ticket Tool service itself are welcome at the same address.

## Integration keys

An integration key lets whoever holds it answer lookups and report account links for one Discord server. If a key leaks, **rotate** it on the Connections page of the Ticket Tool dashboard: the old key stops working at once. The plugin and SDKs never send the key itself and never log it; they log only its public id (`ttp_` followed by 16 characters).
