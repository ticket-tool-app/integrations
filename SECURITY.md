# Security Policy

We support the latest release of each plugin and SDK in this repository.

## Reporting a vulnerability

Report it privately rather than opening a public issue or pull request. Either:

- Use the **Report a vulnerability** button on this repository's
  [Security tab](https://github.com/ticket-tool-app/integrations/security), or
- Email [security@ticket-tool.app](mailto:security@ticket-tool.app) with
  `SECURITY` in the subject.

Tell us what you found, how to reproduce it, and the version of the plugin or
SDK. A proof of concept helps.

## What to expect

We'll acknowledge inside 3 business days and come back with an assessment within
a week. We'll tell you when the fix is released, and we're happy to credit you
in the release notes if you want that.

## Scope

Covered:

- The Minecraft plugin and the SDKs in this repository.
- The plugin protocol as described in [spec/PROTOCOL.md](spec/PROTOCOL.md), for
  example a way to forge a signature or replay a request.

Vulnerabilities in the hosted Ticket Tool service are covered by the
[community repository's security policy](https://github.com/ticket-tool-app/community/blob/main/SECURITY.md).

## Integration keys

An integration key lets whoever holds it answer lookups and report account links
for one Discord server. If a key leaks, **rotate** it on the Connections page of
the Ticket Tool dashboard: the old key stops working at once. The plugin and
SDKs never send the key itself and never log it; they log only its public id
(`ttp_` followed by 16 characters).

## Testing

Only test against servers and keys you control. We don't run a paid bounty. We
won't come after anyone who follows this policy in good faith.
