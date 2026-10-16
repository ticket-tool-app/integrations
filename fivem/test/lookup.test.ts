import { describe, expect, it } from "vitest";
import { LookupRequest } from "@ticket-tool/plugin-sdk";
import { BanSource } from "../src/bans/types";
import { BanProviderSetting } from "../src/bans";
import { Frameworks } from "../src/frameworks";
import { onlinePlayer } from "../src/identity";
import { createLookupHandler } from "../src/lookup";
import { SeenStore } from "../src/seen-store";
import { DISCORD, FakeNatives, STEAM64, janeIdentifiers } from "./fake-natives";

const NOW = Date.parse("2026-10-16T12:00:00Z");

const req = (include: ("profile" | "bans")[], discordId: string | null = DISCORD): LookupRequest => ({
  id: "r1",
  type: "player.lookup",
  expiresAt: new Date(NOW + 4000),
  player: { uuid: null, steamId: null, discordId, name: null },
  include,
  wantsProfile: include.includes("profile"),
  wantsBans: include.includes("bans"),
});

function setup(opts: { sources?: BanSource[]; setting?: BanProviderSetting } = {}) {
  const n = new FakeNatives();
  const store = new SeenStore(n, () => NOW);
  const handler = createLookupHandler({
    natives: n,
    store,
    frameworks: new Frameworks(n),
    banSources: opts.sources ?? [],
    banProvider: () => opts.setting ?? "auto",
    serverName: () => "rp-1",
    now: () => NOW,
  });
  return { n, store, handler };
}

const txSource = (hits: any[] = [], seenIds: string[][] = []): BanSource => ({
  provider: "txadmin",
  name: "txAdmin",
  available: () => true,
  lookup: async (ids) => {
    seenIds.push([...ids]);
    return hits;
  },
});

describe("lookup", () => {
  it("answers for an online player with ESX character and job", async () => {
    const { n, handler } = setup({ sources: [txSource()] });
    n.addPlayer("12", "^1Jane", janeIdentifiers());
    n.resources.es_extended = "started";
    n.exportsTable.es_extended = {
      getSharedObject: () => ({
        GetPlayerFromId: (src: number) =>
          src === 12 ? { getName: () => "Jane Doe", getJob: () => ({ label: "LSPD", grade_label: "Sergeant" }) } : undefined,
      }),
    };
    const result = await handler(req(["profile", "bans"]));
    expect(result).toEqual({
      ok: true,
      profile: {
        discordId: DISCORD,
        steamId: STEAM64,
        name: "Jane",
        online: true,
        lastSeen: new Date(NOW),
        firstSeen: new Date(NOW),
        server: "rp-1",
        fields: [
          { label: "Server ID", value: "12" },
          { label: "Character", value: "Jane Doe" },
          { label: "Job", value: "LSPD (Sergeant)" },
        ],
      },
      bans: { banned: false, provider: "txadmin" },
    });
  });

  it("reads QBCore character data", async () => {
    const { n, handler } = setup();
    n.addPlayer("4", "Jane", janeIdentifiers());
    n.resources["qb-core"] = "started";
    n.exportsTable["qb-core"] = {
      GetCoreObject: () => ({
        Functions: {
          GetPlayer: () => ({
            PlayerData: {
              charinfo: { firstname: "Jane", lastname: "Doe" },
              job: { label: "EMS", grade: { name: "Paramedic" } },
              gang: { name: "none", label: "No Gang" },
            },
          }),
        },
      }),
    };
    const result = await handler(req(["profile"]));
    expect(result.ok && result.profile?.fields).toEqual([
      { label: "Server ID", value: "4" },
      { label: "Character", value: "Jane Doe" },
      { label: "Job", value: "EMS (Paramedic)" },
    ]);
  });

  it("answers for an offline player from the last-seen store and matches bans on their identifiers", async () => {
    const seenIds: string[][] = [];
    const { n, store, handler } = setup({ sources: [txSource([], seenIds)] });
    n.addPlayer("5", "Jane", janeIdentifiers());
    store.touch(onlinePlayer(n, "5"), [{ label: "Job", value: "Police" }]);
    n.players.clear();
    const result = await handler(req(["profile", "bans"]));
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.profile).toMatchObject({ online: false, name: "Jane", steamId: STEAM64, fields: [{ label: "Job", value: "Police" }] });
    expect(seenIds[0]).toContain("license:abcdef0123456789abcdef0123456789abcdef01");
    expect(seenIds[0]).toContain(`discord:${DISCORD}`);
  });

  it("says unknown_player for someone never seen and not banned", async () => {
    const { handler } = setup({ sources: [txSource()] });
    expect(await handler(req(["profile", "bans"]))).toEqual({ ok: false, error: "unknown_player", message: null });
  });

  it("reports a ban for someone never seen on this server, by discord id alone", async () => {
    const hit = { provider: "txadmin", source: "txAdmin", reason: "Cheating", createdAt: new Date(NOW - 1000), expiresAt: null, actor: "Admin" };
    const { handler } = setup({ sources: [txSource([hit])] });
    const result = await handler(req(["profile", "bans"]));
    expect(result).toEqual({ ok: true, profile: null, bans: expect.objectContaining({ banned: true, reason: "Cheating" }) });
  });

  it("says no_ban_provider for a bans-only request with no ban source", async () => {
    const { n, handler } = setup();
    n.addPlayer("1", "Jane", janeIdentifiers());
    expect(await handler(req(["bans"]))).toEqual({ ok: false, error: "no_ban_provider", message: null });
  });

  it("still answers the profile when bans can't be read because no source is installed", async () => {
    const { n, handler } = setup();
    n.addPlayer("1", "Jane", janeIdentifiers());
    const result = await handler(req(["profile", "bans"]));
    expect(result.ok && result.profile?.name).toBe("Jane");
    expect(result.ok && result.bans).toBeNull();
  });

  it("refuses a request without a discord id", async () => {
    const { handler } = setup();
    expect(await handler(req(["profile"], null))).toMatchObject({ ok: false, error: "unsupported" });
    expect(await handler(req(["profile"], "not-a-snowflake"))).toMatchObject({ ok: false, error: "unsupported" });
  });
});
