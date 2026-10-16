import { describe, expect, it } from "vitest";
import { findOnlineByDiscord, parseIdentifiers, steamHexToId64, steamId64ToHex } from "../src/identity";
import { DISCORD, FakeNatives, LICENSE, STEAM64, STEAM_HEX, janeIdentifiers } from "./fake-natives";

describe("identity", () => {
  it("converts Cfx.re steam hex to SteamID64 and back", () => {
    expect(steamHexToId64(STEAM_HEX)).toBe(STEAM64);
    expect(steamHexToId64(STEAM_HEX.toUpperCase())).toBe(STEAM64);
    expect(steamId64ToHex(STEAM64)).toBe(STEAM_HEX);
    expect(steamHexToId64("zz")).toBeNull();
    expect(steamHexToId64("1")).toBeNull();
    expect(steamHexToId64("ffffffffffffffffff")).toBeNull();
  });

  it("parses identifiers and never keeps ip", () => {
    const id = parseIdentifiers([...janeIdentifiers(), "discord:999999999999999999", "junk", null as unknown as string]);
    expect(id.discordId).toBe(DISCORD);
    expect(id.steamId).toBe(STEAM64);
    expect(id.license).toBe(LICENSE.slice("license:".length));
    expect(id.ids).toContain(`discord:${DISCORD}`);
    expect(id.ids).toContain(`steam:${STEAM_HEX}`);
    expect(id.ids.some((i) => i.startsWith("ip:"))).toBe(false);
  });

  it("matches the online player by their discord identifier", () => {
    const n = new FakeNatives();
    n.addPlayer("3", "Someone", ["license:aaa", "discord:111111111111111111"]);
    n.addPlayer("7", "Jane", janeIdentifiers());
    n.addPlayer("9", "NoDiscord", ["license:bbb"]);
    const found = findOnlineByDiscord(n, DISCORD);
    expect(found?.source).toBe("7");
    expect(found?.name).toBe("Jane");
    expect(findOnlineByDiscord(n, "222222222222222222")).toBeNull();
  });
});
