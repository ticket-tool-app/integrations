import { describe, expect, it } from "vitest";
import { onlinePlayer } from "../src/identity";
import { SeenStore } from "../src/seen-store";
import { DISCORD, FakeNatives, STEAM64, janeIdentifiers } from "./fake-natives";

describe("SeenStore", () => {
  it("keeps first seen, moves last seen, and remembers fields", () => {
    const n = new FakeNatives();
    let now = Date.parse("2026-10-01T10:00:00Z");
    const store = new SeenStore(n, () => now);
    n.addPlayer("1", "Jane", janeIdentifiers());
    store.touch(onlinePlayer(n, "1"), [{ label: "Job", value: "Police" }]);
    now = Date.parse("2026-10-02T10:00:00Z");
    store.touch(onlinePlayer(n, "1"));
    const rec = store.get(DISCORD)!;
    expect(rec.firstSeen).toBe("2026-10-01T10:00:00.000Z");
    expect(rec.lastSeen).toBe("2026-10-02T10:00:00.000Z");
    expect(rec.steamId).toBe(STEAM64);
    expect(rec.fields).toEqual([{ label: "Job", value: "Police" }]);
    expect(rec.ids.some((i) => i.startsWith("ip:"))).toBe(false);
    expect([...n.kvp.keys()]).toEqual([`seen:${DISCORD}`]);
  });

  it("skips players without discord and tolerates junk", () => {
    const n = new FakeNatives();
    const store = new SeenStore(n);
    n.addPlayer("1", "Anon", ["license:aaa"]);
    store.touch(onlinePlayer(n, "1"));
    expect(n.kvp.size).toBe(0);
    n.kvp.set(`seen:${DISCORD}`, "{not json");
    expect(store.get(DISCORD)).toBeNull();
    expect(store.get("not-an-id")).toBeNull();
  });
});
