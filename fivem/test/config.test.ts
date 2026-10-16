import { describe, expect, it } from "vitest";
import { platformString, readSettings } from "../src/config";
import { FakeNatives } from "./fake-natives";

describe("settings", () => {
  it("reads convars over config.json", () => {
    const n = new FakeNatives();
    n.files["ticket-tool-link/config.json"] = JSON.stringify({ key: "ttp_file", serverName: "from-file", banProvider: "server", debug: true });
    n.convars.ticket_tool_key = "ttp_convar";
    const s = readSettings(n);
    expect(s.key).toBe("ttp_convar");
    expect(s.serverName).toBe("from-file");
    expect(s.banProvider).toBe("server");
    expect(s.debug).toBe(true);
    expect(s.problems).toEqual([]);
  });

  it("reports bad config without echoing the key", () => {
    const n = new FakeNatives();
    n.files["ticket-tool-link/config.json"] = "{ nope";
    n.convars.ticket_tool_ban_provider = "litebans";
    const s = readSettings(n);
    expect(s.banProvider).toBe("auto");
    expect(s.problems).toHaveLength(2);
    expect(s.key).toBeNull();
  });

  it("names FiveM or RedM with the game build", () => {
    const n = new FakeNatives();
    n.convars.sv_enforceGameBuild = "3095";
    n.convars.version = "FXServer-master SERVER v1.0.0.17000 linux";
    expect(platformString(n)).toBe("FiveM b3095 (server 17000)");
    n.convars.gamename = "rdr3";
    n.convars.sv_enforceGameBuild = "1491";
    expect(platformString(n)).toBe("RedM b1491 (server 17000)");
    delete n.convars.sv_enforceGameBuild;
    delete n.convars.version;
    expect(platformString(n)).toBe("RedM");
  });
});
