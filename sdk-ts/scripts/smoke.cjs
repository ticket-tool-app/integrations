// Checks the built dist/ against the shared test vectors with no test runner,
// so CI can run it on the oldest supported Node (16) as well.
"use strict";
const assert = require("node:assert");
const { readFileSync } = require("node:fs");
const { join } = require("node:path");
const sdk = require("../dist");

const v = JSON.parse(readFileSync(join(__dirname, "../../spec/test-vectors/v1.json"), "utf8"));
assert.strictEqual(sdk.deriveSigningSecret(v.key), v.signingSecret);
for (const b of v.bodyHashes) assert.strictEqual(sdk.sha256Hex(Buffer.from(b.body)), b.sha256);
const signer = new sdk.Signer(v.signingSecret);
for (const r of v.requests) {
  assert.strictEqual(sdk.canonical(r.method, r.endpoint, r.timestamp, r.nonce, Buffer.from(r.body)), r.canonical);
  assert.strictEqual(signer.signRequest(r.method, r.endpoint, r.timestamp, r.nonce, Buffer.from(r.body)), r.signature);
}
for (const r of v.replies) {
  assert.ok(signer.verifyReply(r.endpoint, r.timestamp, r.nonce, Buffer.from(r.body), r.signature));
}
const key = sdk.PluginKey.parse(v.key);
assert.strictEqual(String(key), "ttp_" + v.keyId);
new sdk.TicketToolClient({
  key: v.key,
  plugin: { type: "fivem", name: "smoke", version: "0.0.0" },
  handler: () => sdk.LookupResult.failure("unsupported"),
});
console.log(`sdk-ts smoke test passed on Node ${process.version}`);
