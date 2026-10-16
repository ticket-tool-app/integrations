import { ChildProcessWithoutNullStreams, execFileSync, spawn } from "node:child_process";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterAll, beforeAll, describe, expect, it } from "vitest";
import { FakeTicketTool, KEY } from "../../sdk-ts/test/fake-server";

const root = join(__dirname, "..");
const DISCORD = "123456789012345678";
const OFFLINE_DISCORD = "223456789012345678";
const BANNED_DISCORD = "323456789012345678";
const nowSec = Math.floor(Date.now() / 1000);

let fake: FakeTicketTool;
let proc: ChildProcessWithoutNullStreams;
let output = "";

async function until(cond: () => boolean, what: string, timeoutMs = 8000) {
  const end = Date.now() + timeoutMs;
  while (!cond()) {
    if (Date.now() > end) throw new Error(`Timed out waiting for ${what}\n--- output ---\n${output}`);
    await new Promise((r) => setTimeout(r, 20));
  }
}

const send = (line: string) => proc.stdin.write(line + "\n");

beforeAll(async () => {
  execFileSync(process.execPath, [join(root, "scripts/build.mjs")], { cwd: root, stdio: "pipe" });
  fake = await FakeTicketTool.start();
  const dir = mkdtempSync(join(tmpdir(), "ttl-e2e-"));
  const txDb = join(dir, "playersDB.json");
  writeFileSync(
    txDb,
    JSON.stringify({
      version: 5,
      players: [],
      actions: [
        {
          id: "BXXX-0001", type: "ban", ids: [`discord:${BANNED_DISCORD}`, "license:deadbeef"],
          playerName: "Cheater", reason: "^1Aimbot", author: "AdminAlex", timestamp: nowSec - 600,
          expiration: false, revocation: { timestamp: null, author: null },
        },
      ],
    }),
  );
  const env = {
    version: "1.4.0",
    kvpFile: join(dir, "kvp.json"),
    convars: {
      ticket_tool_key: KEY,
      ticket_tool_api_url: fake.url,
      ticket_tool_server_name: "rp-1",
      ticket_tool_txadmin_db: txDb,
      gamename: "rdr3",
      sv_enforceGameBuild: "1491",
    },
    resources: { monitor: "started" },
    players: {
      "1": { name: "Jane", identifiers: ["license:aaaa", "steam:1100001000056ba", `discord:${DISCORD}`, "ip:203.0.113.7"] },
      "2": { name: "Sam", identifiers: ["license:bbbb", `discord:${OFFLINE_DISCORD}`] },
    },
  };
  // TTL_E2E_NODE runs the resource on another Node, e.g. FXServer's default 16.
  proc = spawn(process.env.TTL_E2E_NODE || process.execPath, ["-r", join(__dirname, "e2e/stub-natives.cjs"), join(root, "ticket-tool-link/dist/server.js")], {
    env: { ...process.env, TTL_E2E: JSON.stringify(env) },
  });
  proc.stdout.on("data", (d) => (output += d.toString()));
  proc.stderr.on("data", (d) => (output += d.toString()));
});

afterAll(async () => {
  proc?.kill();
  await fake?.close();
});

describe("bundled server.js under plain Node", () => {
  it("connects with a RedM platform string and txadmin ban provider", async () => {
    const hello = await fake.waitFor("/hello", () => true, 8000);
    expect(hello.body).toEqual({
      protocol: 1,
      plugin: { type: "fivem", version: "1.4.0", platform: "RedM b1491" },
      banProviders: ["txadmin"],
    });
    await fake.waitFor("/poll");
  });

  it("answers an online player's lookup", async () => {
    fake.queueLookup(FakeTicketTool.lookup("on", { discordId: DISCORD }, 4500, ["profile", "bans"]));
    const r = await fake.waitFor("/respond", (x) => x.body.requestId === "on");
    expect(r.body.ok).toBe(true);
    expect(r.body.profile).toMatchObject({
      discordId: DISCORD, steamId: "76561197960287930", name: "Jane", online: true, server: "rp-1",
      fields: [{ label: "Server ID", value: "1" }],
    });
    expect(r.body.bans).toEqual({ banned: false, provider: "txadmin", reason: null, createdAt: null, expiresAt: null, actor: null });
  });

  it("answers from the last-seen store after a player leaves", async () => {
    send("drop 2");
    await new Promise((r) => setTimeout(r, 100));
    fake.queueLookup(FakeTicketTool.lookup("off", { discordId: OFFLINE_DISCORD }, 4500, ["profile"]));
    const r = await fake.waitFor("/respond", (x) => x.body.requestId === "off");
    expect(r.body.profile).toMatchObject({ discordId: OFFLINE_DISCORD, name: "Sam", online: false });
    expect(typeof (r.body.profile as Record<string, unknown>).lastSeen).toBe("string");
  });

  it("reports a txAdmin ban from the database file", async () => {
    fake.queueLookup(FakeTicketTool.lookup("ban", { discordId: BANNED_DISCORD }, 4500, ["profile", "bans"]));
    const r = await fake.waitFor("/respond", (x) => x.body.requestId === "ban");
    expect(r.body.ok).toBe(true);
    expect(r.body.profile).toBeUndefined();
    expect(r.body.bans).toMatchObject({ banned: true, provider: "txadmin", reason: "Aimbot", actor: "AdminAlex", expiresAt: null });
  });

  it("says unknown_player for a stranger", async () => {
    fake.queueLookup(FakeTicketTool.lookup("who", { discordId: "999999999999999999" }, 4500, ["profile", "bans"]));
    const r = await fake.waitFor("/respond", (x) => x.body.requestId === "who");
    expect(r.body).toEqual({ requestId: "who", ok: false, error: "unknown_player" });
  });

  it("runs the restricted status command without printing the key", async () => {
    send("cmd status");
    await until(() => output.includes("Ban sources:"), "status output");
    expect(output).toContain("COMMAND restricted=true");
    expect(output).toContain("State: connected to Ticket Tool");
    expect(output).toContain("Key: ttp_0123456789abcdef");
    expect(output).not.toContain(KEY.split(".")[1]);
  });

  it("says goodbye when the resource stops", async () => {
    send("stop");
    await fake.waitFor("/goodbye", () => true, 4000);
    expect(output).not.toContain(KEY.split(".")[1]);
  });
});
