# @ticket-tool/plugin-sdk (planned)

A TypeScript SDK for Node 18+ game servers, bots and test harnesses: no runtime dependencies, just `node:crypto` and global `fetch`.

It isn't written yet. Until it is, [spec/PROTOCOL.md](../spec/PROTOCOL.md) has a working signing sketch under "TypeScript SDK", and [spec/test-vectors/v1.json](../spec/test-vectors/v1.json) has the values its tests must reproduce. The Java SDK in [`sdk-java/`](../sdk-java) shows the full client behavior (poll loop, backoff, reply verification) to mirror.
