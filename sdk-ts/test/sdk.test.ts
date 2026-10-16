import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import { bodyHash, signingSecret, signReply, signRequest, TicketToolClient, type Json } from "../src/index.js";

// build-test/test/ → repo root spec/
const vectors = JSON.parse(
  readFileSync(fileURLToPath(new URL("../../../spec/test-vectors/v1.json", import.meta.url)), "utf8"),
);
const enc = (s: string) => new TextEncoder().encode(s);

test("shared test vectors", () => {
  assert.equal(signingSecret(vectors.key), vectors.signingSecret);
  for (const b of vectors.bodyHashes) assert.equal(bodyHash(enc(b.body)), b.sha256);
  for (const r of vectors.requests) {
    assert.equal(signRequest(vectors.signingSecret, r.method, r.endpoint, r.timestamp, r.nonce, enc(r.body)), r.signature);
  }
  for (const r of vectors.replies) {
    assert.equal(signReply(vectors.signingSecret, r.endpoint, r.timestamp, r.nonce, enc(r.body)), r.signature);
  }
});

/** A fake Ticket Tool: checks request signatures and signs its replies. */
function fakeServer(handle: (endpoint: string, body: Json) => { status?: number; body: Json; unsigned?: boolean }) {
  const secret = vectors.signingSecret as string;
  const seen: { endpoint: string; body: Json }[] = [];
  const nonces = new Set<string>();
  const fetchImpl = (async (url: string, init: RequestInit) => {
    const endpoint = new URL(url).pathname.replace("/api/plugin/v1", "");
    const h = init.headers as Record<string, string>;
    const raw = init.body as Uint8Array;
    assert.equal(h["x-tt-signature"], signRequest(secret, "POST", endpoint, Number(h["x-tt-timestamp"]), h["x-tt-nonce"]!, raw));
    assert.ok(!nonces.has(h["x-tt-nonce"]!), "nonce reused");
    nonces.add(h["x-tt-nonce"]!);
    const body = JSON.parse(new TextDecoder().decode(raw)) as Json;
    seen.push({ endpoint, body });
    const out = handle(endpoint, body);
    // A real empty poll is held open; yield so the loop doesn't spin.
    if (endpoint === "/poll") await new Promise((r) => setTimeout(r, 10));
    const text = enc(JSON.stringify(out.body));
    const ts = String(Math.floor(Date.now() / 1000));
    const headers = new Headers(out.unsigned ? {} : {
      "x-tt-timestamp": ts,
      "x-tt-nonce": h["x-tt-nonce"]!,
      "x-tt-signature": signReply(secret, endpoint, ts, h["x-tt-nonce"]!, text),
    });
    return new Response(text, { status: out.status ?? 200, headers });
  }) as unknown as typeof fetch;
  return { fetchImpl, seen };
}

const plugin = { type: "fivem", name: "test", version: "0.0.0" };
const helloReply = { ok: true, protocol: 1, pluginType: "fivem", key: { name: "k" }, server: { id: "1" }, poll: { waitSeconds: 25 } };

test("hello, poll, answer and goodbye", async () => {
  let polls = 0;
  const { fetchImpl, seen } = fakeServer((endpoint) => {
    if (endpoint === "/hello") return { body: helloReply };
    if (endpoint === "/poll") {
      polls++;
      const requests = polls === 1
        ? [{ id: "r1", type: "player.lookup", expiresAt: new Date(Date.now() + 5000).toISOString(), player: { discordId: "42" }, include: ["profile"] },
           { id: "r2", type: "something.new", expiresAt: new Date(Date.now() + 5000).toISOString(), player: {}, include: [] }]
        : [];
      return { body: { ok: true, requests } };
    }
    return { body: { ok: true } };
  });
  const client = new TicketToolClient({
    key: vectors.key,
    plugin,
    fetch: fetchImpl,
    handler: (r) => ({ ok: true, profile: { name: `p${String(r.player.discordId)}`, online: true } }),
  });
  client.start();
  while (!seen.some((s) => s.endpoint === "/respond")) await new Promise((r) => setTimeout(r, 5));
  await client.stop();
  const respond = seen.find((s) => s.endpoint === "/respond")!;
  assert.deepEqual(respond.body, { requestId: "r1", ok: true, profile: { name: "p42", online: true } });
  assert.equal(seen.filter((s) => s.endpoint === "/respond").length, 1, "unknown request type ignored");
  assert.equal(seen.at(-1)!.endpoint, "/goodbye");
  assert.equal(client.status().answered, 1);
});

test("refuses an unsigned success and stops on invalid_key", async () => {
  let call = 0;
  const { fetchImpl } = fakeServer(() => {
    call++;
    return call === 1
      ? { body: helloReply, unsigned: true }
      : { status: 401, body: { ok: false, error: "invalid_key" }, unsigned: true };
  });
  const client = new TicketToolClient({ key: vectors.key, plugin, fetch: fetchImpl, handler: () => ({ ok: false, error: "unsupported" }), sleep: async () => {} });
  client.start();
  while (client.status().state !== "stopped_invalid_key") await new Promise((r) => setTimeout(r, 5));
  assert.equal(call, 2);
  await client.stop();
});

test("link maps errors", async () => {
  const { fetchImpl } = fakeServer(() => ({ status: 404, body: { ok: false, error: "invalid_code" } }));
  const client = new TicketToolClient({ key: vectors.key, plugin, fetch: fetchImpl, handler: () => ({ ok: true }) });
  assert.deepEqual(await client.link("ABCD-EFGH", { discordId: "1" }), { outcome: "invalid_code", error: "invalid_code" });
});
