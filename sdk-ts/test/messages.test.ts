import { describe, expect, it } from "vitest";
import * as Messages from "../src/messages";
import { ApiError, LookupResult, apiBase, stripCfxColors, stripMinecraftColors, clip } from "../src";

const request = (include: string[]) =>
  Messages.pollRequests({
    requests: [
      {
        id: "r1",
        type: "player.lookup",
        expiresAt: "2030-01-01T00:00:00.000Z",
        player: { discordId: "123456789012345678", name: null, extra: 1 },
        include,
        unknownField: true,
      },
    ],
  })[0];

describe("messages", () => {
  it("reads poll requests, dropping unknown parts and malformed entries", () => {
    const list = Messages.pollRequests({
      requests: [
        { id: "a", type: "player.lookup", expiresAt: "2030-01-01T00:00:00Z", player: { steamId: "76561198000000000" }, include: ["profile", "inventory", "profile"] },
        { id: "b", type: "player.lookup", expiresAt: "not a date", include: [] },
        { type: "player.lookup", expiresAt: "2030-01-01T00:00:00Z" },
        "junk",
      ],
    });
    expect(list).toHaveLength(1);
    expect(list[0].include).toEqual(["profile"]);
    expect(list[0].wantsProfile).toBe(true);
    expect(list[0].wantsBans).toBe(false);
    expect(list[0].player).toEqual({ uuid: null, steamId: "76561198000000000", discordId: null, name: null });
    expect(Messages.pollRequests({})).toEqual([]);
  });

  it("sends only the parts asked for, with limits applied", () => {
    const req = request(["profile"]);
    const body = Messages.respond(
      req,
      LookupResult.answer(
        {
          discordId: "123456789012345678",
          steamId: "not-a-steam-id",
          name: "x".repeat(80),
          online: true,
          lastSeen: new Date("2026-10-15T11:58:00Z"),
          firstSeen: null,
          server: "  ",
          fields: [
            { label: "Character", value: "Jane Doe" },
            { label: "", value: "dropped" },
            ...Array.from({ length: 8 }, (_, i) => ({ label: "L".repeat(40), value: String(i).repeat(120) })),
          ],
        },
        { banned: true, provider: "txadmin", reason: "r" },
      ),
    );
    expect(body.bans).toBeUndefined();
    const p = body.profile as Record<string, unknown>;
    expect(p.steamId).toBeUndefined();
    expect(p.discordId).toBe("123456789012345678");
    expect((p.name as string).length).toBe(64);
    expect(p.lastSeen).toBe("2026-10-15T11:58:00.000Z");
    expect(p.server).toBeNull();
    const fields = p.fields as { label: string; value: string }[];
    expect(fields).toHaveLength(6);
    expect(fields[0]).toEqual({ label: "Character", value: "Jane Doe" });
    expect(fields[1].label.length).toBe(32);
    expect(fields[1].value.length).toBe(100);
  });

  it("blanks ban details when not banned and caps the reason", () => {
    const req = request(["bans"]);
    expect(Messages.respond(req, LookupResult.answer(null, { banned: false, provider: "TXADMIN", reason: "old", actor: "a", activeCount: 3 })).bans).toEqual({
      banned: false, provider: "txadmin", reason: null, createdAt: null, expiresAt: null, actor: null,
    });
    const banned = Messages.respond(req, LookupResult.answer(null, { banned: true, provider: "server", reason: "y".repeat(3000), activeCount: 2, expiresAt: null })).bans as Record<string, unknown>;
    expect((banned.reason as string).length).toBe(2000);
    expect(banned.activeCount).toBe(2);
    expect(banned.expiresAt).toBeNull();
  });

  it("writes failures with an optional short message", () => {
    const req = request(["bans"]);
    expect(Messages.respond(req, LookupResult.failure("no_ban_provider"))).toEqual({ requestId: "r1", ok: false, error: "no_ban_provider" });
    expect((Messages.respond(req, LookupResult.failure("error", "m".repeat(300))).message as string).length).toBe(200);
  });

  it("builds hello and user agent", () => {
    const info = { type: "fivem", name: "ticket-tool-fivem", version: "1.0.0", platform: "FiveM b3095" };
    expect(Messages.hello(info, ["txadmin", "server"])).toEqual({ protocol: 1, plugin: { type: "fivem", version: "1.0.0", platform: "FiveM b3095" }, banProviders: ["txadmin", "server"] });
    expect(Messages.hello({ ...info, platform: null }, [])).toEqual({ protocol: 1, plugin: { type: "fivem", version: "1.0.0" } });
    expect(Messages.userAgent(info)).toBe("ticket-tool-fivem/1.0.0 (FiveM b3095)");
    expect(Messages.helloReply({ poll: { waitSeconds: 999 } }).pollWaitSeconds).toBe(120);
  });

  it("maps link errors", () => {
    expect(Messages.linkError(new ApiError(404, "invalid_code")).outcome).toBe("invalid_code");
    expect(Messages.linkError(new ApiError(503, "unavailable", { retryable: true })).outcome).toBe("unavailable");
    expect(Messages.linkError(new ApiError(503, "unavailable", {})).outcome).toBe("failed");
    expect(Messages.linkError(new ApiError(502, null)).error).toBe("http_502");
  });

  it("strips color codes and clips without splitting surrogate pairs", () => {
    expect(stripCfxColors("^1Cheating^0 is ^*bad")).toBe("Cheating is bad");
    expect(stripMinecraftColors("§cX-ray &#ff0000hex &lbold")).toBe("X-ray hex bold");
    expect(clip("ab😀", 3)).toBe("ab");
  });

  it("only allows https, or http on loopback", () => {
    expect(apiBase(null).toString()).toBe("https://dash.ticket-tool.app/api/plugin/v1");
    expect(apiBase("https://staging.example.com/").toString()).toBe("https://staging.example.com/api/plugin/v1");
    expect(apiBase("https://x.example.com/api/plugin/v1").toString()).toBe("https://x.example.com/api/plugin/v1");
    expect(apiBase("http://127.0.0.1:8080").toString()).toBe("http://127.0.0.1:8080/api/plugin/v1");
    expect(() => apiBase("http://example.com")).toThrow(/https/);
    expect(() => apiBase("not a url")).toThrow(/valid URL/);
  });
});
