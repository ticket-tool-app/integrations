import * as path from "node:path";
import { describe, expect, it } from "vitest";
import { activeSources, lookupBans, mergeHits, providerNames } from "../src/bans";
import { EasyAdminBans } from "../src/bans/easyadmin";
import { QbBans } from "../src/bans/qbcore";
import { TxAdminBans, txAdminDbCandidates } from "../src/bans/txadmin";
import { BanSource } from "../src/bans/types";
import { DISCORD, FakeNatives, LICENSE } from "./fake-natives";

const NOW = Date.parse("2026-10-16T00:00:00Z");
const nowSec = NOW / 1000;

const txAction = (over: Record<string, unknown>) => ({
  id: "B1",
  type: "ban",
  ids: [`discord:${DISCORD}`, LICENSE],
  playerName: "Jane",
  reason: "^1RDM^0 in the city",
  author: "AdminAlex",
  timestamp: nowSec - 3600,
  expiration: false,
  revocation: { timestamp: null, author: null },
  ...over,
});

function txWithDb(n: FakeNatives, actions: unknown[], file = "/srv/txData/default/data/playersDB.json") {
  let content = JSON.stringify({ version: 5, players: [], actions });
  let mtime = 1;
  const tx = new TxAdminBans(n, {
    overridePath: file,
    now: () => NOW,
    readFile: async (f) => {
      if (f !== path.normalize(file)) throw new Error("ENOENT");
      return content;
    },
    stat: async (f) => {
      if (f !== path.normalize(file)) throw new Error("ENOENT");
      return { mtimeMs: mtime, size: content.length };
    },
  });
  return {
    tx,
    write(next: unknown[]) {
      content = JSON.stringify({ version: 5, players: [], actions: next });
      mtime++;
    },
  };
}

describe("txAdmin", () => {
  it("finds active bans by any identifier and maps them", async () => {
    const n = new FakeNatives();
    n.resources.monitor = "started";
    const { tx } = txWithDb(n, [
      txAction({}),
      txAction({ id: "B2", type: "warn" }),
      txAction({ id: "B3", expiration: nowSec - 10 }),
      txAction({ id: "B4", revocation: { timestamp: nowSec - 5, author: "x" } }),
      txAction({ id: "B5", ids: ["license:someoneelse"] }),
    ]);
    expect(tx.available()).toBe(true);
    const hits = await tx.lookup([LICENSE]);
    expect(hits).toHaveLength(1);
    expect(hits[0]).toMatchObject({ provider: "txadmin", reason: "^1RDM^0 in the city", actor: "AdminAlex", expiresAt: null });
    expect(hits[0].createdAt?.toISOString()).toBe(new Date((nowSec - 3600) * 1000).toISOString());
    expect(await tx.lookup(["discord:000000000000000001"])).toEqual([]);
  });

  it("maps temporary bans and rereads the file when it changes", async () => {
    const n = new FakeNatives();
    const db = txWithDb(n, []);
    expect(await db.tx.lookup([`discord:${DISCORD}`])).toEqual([]);
    db.write([txAction({ expiration: nowSec + 86400 })]);
    const hits = await db.tx.lookup([`discord:${DISCORD}`]);
    expect(hits[0].expiresAt?.getTime()).toBe((nowSec + 86400) * 1000);
  });

  it("layers ban and revoke events over the not-yet-flushed file", async () => {
    const n = new FakeNatives();
    const db = txWithDb(n, []);
    db.tx.listen();
    n.emit("txAdmin:events:playerBanned", "0", {
      author: "AdminAlex", reason: "Combat logging", actionId: "LIVE1", expiration: false,
      targetIds: [`discord:${DISCORD}`, LICENSE], targetName: "Jane",
    });
    expect((await db.tx.lookup([`discord:${DISCORD}`]))[0].reason).toBe("Combat logging");
    n.emit("txAdmin:events:actionRevoked", "0", { actionId: "LIVE1", actionType: "ban" });
    expect(await db.tx.lookup([`discord:${DISCORD}`])).toEqual([]);
  });

  it("resolves the database the way txAdmin does", () => {
    const n = new FakeNatives();
    n.convars.citizen_root = "/opt/fx/alpine/opt/cfx-server";
    n.convars.serverProfile = "rp";
    const linux = txAdminDbCandidates(n, { env: {}, platform: "linux" });
    expect(linux).toEqual([path.normalize("/opt/fx/txData/rp/data/playersDB.json")]);
    const withEnv = txAdminDbCandidates(n, { env: { TXHOST_DATA_PATH: "/data/tx" }, platform: "linux", overridePath: "/custom/playersDB.json" });
    expect(withEnv[0]).toBe(path.normalize("/custom/playersDB.json"));
    expect(withEnv[1]).toBe(path.normalize("/data/tx/rp/data/playersDB.json"));
  });

  it("fails loudly when txAdmin runs but no database is found", async () => {
    const n = new FakeNatives();
    n.resources.monitor = "started";
    const tx = new TxAdminBans(n, { env: {}, now: () => NOW, stat: async () => { throw new Error("ENOENT"); } });
    await expect(tx.lookup([LICENSE])).rejects.toThrow(/ticket_tool_txadmin_db/);
  });
});

describe("EasyAdmin", () => {
  it("reads banlist.json and honors its permanent marker", async () => {
    const n = new FakeNatives();
    n.resources.EasyAdmin = "started";
    n.files["EasyAdmin/banlist.json"] = JSON.stringify([
      { banid: 1, username: "Jane", identifiers: [`discord:${DISCORD}`], banner: "Mod", reason: "Hacking", expire: 10444633200, time: nowSec - 60 },
      { banid: 2, username: "Jane", identifiers: [LICENSE], banner: "Mod", reason: "Old", expire: nowSec - 1, time: nowSec - 999 },
      { banid: 3, username: "Other", identifiers: ["license:zzz"], banner: "Mod", reason: "x", expire: 10444633200 },
    ]);
    const ea = new EasyAdminBans(n, () => NOW);
    expect(ea.available()).toBe(true);
    const hits = await ea.lookup([`discord:${DISCORD}`, LICENSE]);
    expect(hits).toHaveLength(1);
    expect(hits[0]).toMatchObject({ provider: "server", reason: "Hacking", actor: "Mod", expiresAt: null });
  });
});

describe("QBCore bans table", () => {
  it("queries oxmysql by discord and license", async () => {
    const n = new FakeNatives();
    n.resources["qb-core"] = "started";
    n.resources.oxmysql = "started";
    let seen: { sql: string; params: string[] } | null = null;
    n.exportsTable.oxmysql = {
      query: (sql: string, params: string[], cb: (rows: unknown) => void) => {
        seen = { sql, params };
        cb([
          { reason: "Exploiting", expire: 2147483647, bannedby: "Admin" },
          { reason: "Expired", expire: nowSec - 5, bannedby: "Admin" },
          { reason: "Temp", expire: nowSec + 60, bannedby: "Admin" },
        ]);
      },
    };
    const qb = new QbBans(n, () => NOW);
    const hits = await qb.lookup([`discord:${DISCORD}`, LICENSE, "steam:abc"]);
    expect(seen!.params).toEqual([`discord:${DISCORD}`, LICENSE]);
    expect(seen!.sql).toContain("FROM bans");
    expect(hits.map((h) => h.reason)).toEqual(["Exploiting", "Temp"]);
    expect(hits[0].expiresAt).toBeNull();
  });
});

describe("merging", () => {
  const fake = (provider: "txadmin" | "server", available: boolean, result: () => Promise<any>): BanSource => ({
    provider, name: provider, available: () => available, lookup: result,
  });

  it("chooses sources per setting", () => {
    const a = fake("txadmin", true, async () => []);
    const b = fake("server", true, async () => []);
    const c = fake("server", false, async () => []);
    expect(activeSources([a, b, c], "auto")).toEqual([a, b]);
    expect(activeSources([a, b, c], "server")).toEqual([b]);
    expect(activeSources([a, b, c], "none")).toEqual([]);
    expect(providerNames([a, b, b])).toEqual(["txadmin", "server"]);
  });

  it("returns null with no source, clean when nothing matches, and the newest ban otherwise", async () => {
    expect(await lookupBans([], [LICENSE])).toBeNull();
    expect(await lookupBans([fake("txadmin", true, async () => [])], [LICENSE])).toEqual({ banned: false, provider: "txadmin" });
    const merged = mergeHits(
      [
        { provider: "server", source: "EasyAdmin", reason: "old", createdAt: new Date(1000), expiresAt: null, actor: "a" },
        { provider: "txadmin", source: "txAdmin", reason: "^1new", createdAt: new Date(5000), expiresAt: null, actor: "^2b" },
      ],
      "txadmin",
    );
    expect(merged).toMatchObject({ banned: true, provider: "txadmin", reason: "new", actor: "b", activeCount: 2 });
  });

  it("skips a failing source unless all fail", async () => {
    const errors: string[] = [];
    const ok = fake("server", true, async () => []);
    const bad = fake("txadmin", true, async () => { throw new Error("boom"); });
    expect(await lookupBans([bad, ok], [LICENSE], (s) => errors.push(s.name))).toEqual({ banned: false, provider: "txadmin" });
    expect(errors).toEqual(["txadmin"]);
    await expect(lookupBans([bad], [LICENSE])).rejects.toThrow("boom");
  });
});
