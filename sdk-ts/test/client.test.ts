import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { LookupRequest, LookupResult, SdkLogger, TicketToolClient, ClientOptions } from "../src";
import { FakeTicketTool, KEY, ok, signedError, unsigned } from "./fake-server";

const DISCORD_ID = "123456789012345678";

let fake: FakeTicketTool;
let clients: TicketToolClient[] = [];
let sleeps: number[] = [];
let logs: string[] = [];

const logger: SdkLogger = {
  debug: (m) => logs.push("debug " + m),
  info: (m) => logs.push("info " + m),
  warn: (m) => logs.push("warn " + m),
};

function client(overrides: Partial<ClientOptions> = {}): TicketToolClient {
  const c = new TicketToolClient({
    key: KEY,
    apiUrl: fake.url,
    plugin: { type: "fivem", name: "ticket-tool-fivem", version: "1.0.0", platform: "FiveM b3095" },
    banProviders: () => ["txadmin"],
    logger,
    // Records the wait, then yields briefly so retry loops don't spin the event loop.
    sleep: (ms, signal) => {
      sleeps.push(ms);
      return new Promise((r) => {
        const t = setTimeout(r, 5);
        signal.addEventListener("abort", () => {
          clearTimeout(t);
          r();
        });
      });
    },
    handler: (req: LookupRequest) =>
      LookupResult.answer(
        { discordId: req.player.discordId, name: "Jane", online: true, fields: [{ label: "Job", value: "Police" }] },
        { banned: false, provider: "txadmin" },
      ),
    ...overrides,
  });
  clients.push(c);
  return c;
}

async function until(cond: () => boolean, what: string, timeoutMs = 5000): Promise<void> {
  const end = Date.now() + timeoutMs;
  while (!cond()) {
    if (Date.now() > end) throw new Error("Timed out waiting for " + what);
    await new Promise((r) => setTimeout(r, 10));
  }
}

beforeEach(async () => {
  fake = await FakeTicketTool.start();
  sleeps = [];
  logs = [];
});

afterEach(async () => {
  await Promise.all(clients.map((c) => c.stop()));
  clients = [];
  await fake.close();
});

describe("TicketToolClient", () => {
  it("says hello, polls and answers", async () => {
    const c = client();
    c.start();
    const hello = await fake.waitFor("/hello");
    expect(hello.body).toEqual({
      protocol: 1,
      plugin: { type: "fivem", version: "1.0.0", platform: "FiveM b3095" },
      banProviders: ["txadmin"],
    });
    fake.queueLookup(FakeTicketTool.lookup("r1", { discordId: DISCORD_ID }, 4000, ["profile", "bans"]));
    const answer = await fake.waitFor("/respond");
    expect(answer.body).toEqual({
      requestId: "r1",
      ok: true,
      profile: {
        discordId: DISCORD_ID,
        name: "Jane",
        online: true,
        lastSeen: null,
        firstSeen: null,
        server: null,
        fields: [{ label: "Job", value: "Police" }],
      },
      bans: { banned: false, provider: "txadmin", reason: null, createdAt: null, expiresAt: null, actor: null },
    });
    await until(() => c.status().answered === 1, "answered count");
    expect(c.status().state).toBe("connected");
    expect(c.status().hello?.keyName).toBe("Roleplay 1");
    expect(logs.join("\n")).not.toContain(KEY.split(".")[1]);
  });

  it("sends only the parts asked for", async () => {
    client().start();
    fake.queueLookup(FakeTicketTool.lookup("r2", { discordId: DISCORD_ID }, 4000, ["bans", "inventory"]));
    const answer = await fake.waitFor("/respond");
    expect(answer.body.profile).toBeUndefined();
    expect(answer.body.bans).toBeDefined();
  });

  it("skips expired and unknown requests, and answers an empty include as unsupported", async () => {
    client().start();
    fake.queueLookup(FakeTicketTool.lookup("old", { discordId: DISCORD_ID }, -1000, ["profile"]));
    fake.queueLookup({ ...FakeTicketTool.lookup("weird", {}, 4000, ["profile"]), type: "server.restart" });
    fake.queueLookup(FakeTicketTool.lookup("empty", { discordId: DISCORD_ID }, 4000, ["inventory"]));
    const answer = await fake.waitFor("/respond");
    expect(answer.body).toEqual({ requestId: "empty", ok: false, error: "unsupported" });
    await new Promise((r) => setTimeout(r, 300));
    expect(fake.calls("/respond")).toHaveLength(1);
  });

  it("reports a slow handler as an error before expiry", async () => {
    client({ answerBudgetMs: 200, handler: () => new Promise(() => {}) }).start();
    fake.queueLookup(FakeTicketTool.lookup("slow", { discordId: DISCORD_ID }, 4000, ["profile"]));
    const answer = await fake.waitFor("/respond");
    expect(answer.body).toEqual({ requestId: "slow", ok: false, error: "error", message: "The game server took too long to answer" });
  });

  it("reports a throwing handler as an error", async () => {
    client({ handler: () => { throw new Error("boom"); } }).start();
    fake.queueLookup(FakeTicketTool.lookup("t", { discordId: DISCORD_ID }, 4000, ["profile"]));
    const answer = await fake.waitFor("/respond");
    expect(answer.body.error).toBe("error");
  });

  it("corrects clock skew from serverTime", async () => {
    fake.serverClockOffset = 1000;
    const c = client();
    c.start();
    await fake.waitFor("/poll");
    expect(c.status().clockOffsetSeconds).toBeGreaterThanOrEqual(999);
    expect(c.status().clockOffsetSeconds).toBeLessThanOrEqual(1001);
  });

  it("retries a stale timestamp only once", async () => {
    let calls = 0;
    fake.override("/hello", () => {
      calls++;
      return unsigned(401, "stale_timestamp", { serverTime: fake.now() + 5000 });
    });
    const c = client();
    c.start();
    await until(() => calls >= 2, "two hello attempts");
    await until(() => c.status().state === "reconnecting", "reconnecting");
    expect(sleeps.length).toBeGreaterThanOrEqual(1);
  });

  it("backs off on server errors and resets after success", async () => {
    let failures = 3;
    fake.override("/poll", () => (failures-- > 0 ? unsigned(503, "unavailable") : ok({ ok: true, requests: [] })));
    const c = client();
    c.start();
    await until(() => failures < 0 && c.status().state === "connected", "recovery");
    expect(sleeps.slice(0, 3).every((ms, i) => ms >= 500 * 2 ** i && ms <= 1000 * 2 ** i)).toBe(true);
    expect(logs.filter((l) => l.startsWith("warn")).length).toBe(1);
    expect(logs).toContain("info Reconnected to Ticket Tool");
  });

  it("waits out poll lease and rate limit errors", async () => {
    let n = 0;
    fake.override("/poll", () => (n++ === 0 ? unsigned(429, "too_many_polls") : n === 2 ? unsigned(429, "rate_limited") : ok({ ok: true, requests: [] })));
    client().start();
    await until(() => sleeps.length >= 2, "two waits");
    expect(sleeps[0]).toBeGreaterThanOrEqual(15000);
    expect(sleeps[1]).toBeGreaterThanOrEqual(60000);
  });

  it("stops for good on invalid_key and sends no goodbye", async () => {
    fake.override("/poll", () => unsigned(401, "invalid_key"));
    const c = client();
    c.start();
    await until(() => c.status().state === "stopped_invalid_key", "stop");
    const polls = fake.calls("/poll").length;
    await new Promise((r) => setTimeout(r, 200));
    expect(fake.calls("/poll").length).toBe(polls);
    expect(c.status().lastError).toContain("ttp_0123456789abcdef");
    await c.stop();
    expect(fake.calls("/goodbye")).toHaveLength(0);
  });

  it("stops on wrong_plugin_type with the expected type in the message", async () => {
    fake.pluginType = "gmod";
    const c = client();
    c.start();
    await until(() => c.status().state === "stopped_error", "stop");
    expect(c.status().lastError).toContain("gmod");
  });

  it("pauses an hour when the plan lapses, then says hello again", async () => {
    let n = 0;
    fake.override("/poll", () => (n++ === 0 ? signedError(403, "plan_not_entitled") : ok({ ok: true, requests: [] })));
    client().start();
    await until(() => fake.calls("/hello").length >= 2, "second hello");
    expect(sleeps[0]).toBe(60 * 60 * 1000);
  });

  it("refuses unsigned and tampered success replies", async () => {
    fake.override("/poll", () => ({ status: 200, body: { ok: true, requests: [{ id: "x" }] }, signed: false }));
    const c = client();
    c.start();
    await until(() => (c.status().lastError ?? "").includes("Unsigned success"), "unsigned refusal");
    fake.clearOverride("/poll");
    fake.tamperReplies = true;
    await until(() => (c.status().lastError ?? "").includes("signature doesn't match"), "tamper refusal");
    expect(fake.calls("/respond")).toHaveLength(0);
  });

  it("uses a fresh nonce per request, including retries", async () => {
    let failures = 3;
    fake.override("/poll", () => (failures-- > 0 ? unsigned(503, "unavailable") : ok({ ok: true, requests: [] })));
    client().start();
    await until(() => fake.calls("/poll").length >= 5, "polls");
    const nonces = fake.received.map((r) => r.nonce);
    expect(new Set(nonces).size).toBe(nonces.length);
    expect(nonces.every((n) => /^[A-Za-z0-9_-]{22}$/.test(n))).toBe(true);
  });

  it("sends goodbye on stop", async () => {
    const c = client();
    c.start();
    await fake.waitFor("/poll");
    const started = Date.now();
    await c.stop();
    expect(Date.now() - started).toBeLessThan(3500);
    expect(fake.calls("/goodbye")).toHaveLength(1);
    expect(c.status().state).toBe("stopped");
  });

  it("links and maps link errors", async () => {
    const c = client();
    c.start();
    const linked = await c.link({ discordId: DISCORD_ID, name: "Jane" }, "ABCD-EFGH");
    expect(linked).toMatchObject({ outcome: "linked", success: true, discordUsername: "alex" });
    expect(fake.calls("/link")[0].body).toEqual({ code: "ABCD-EFGH", player: { discordId: DISCORD_ID, name: "Jane" } });
    fake.override("/link", () => signedError(404, "invalid_code"));
    expect((await c.link({ name: "Jane" }, "x")).outcome).toBe("invalid_code");
    await c.stop();
    expect((await c.link({ name: "Jane" }, "x")).error).toBe("not_running");
  });

  it("refuses plain http except for localhost", () => {
    expect(() => new TicketToolClient({ key: KEY, apiUrl: "http://example.com", plugin: { type: "fivem", name: "n", version: "1" }, handler: () => LookupResult.failure("error") })).toThrow(/https/);
  });
});
