import { ActionRequest, ReportResult, TicketToolClient } from "@ticket-tool/plugin-sdk";
import { describe, expect, it, vi } from "vitest";
import { createActionHandler } from "../src/actions";
import { readSettings } from "../src/config";
import { registerReportCommand } from "../src/report";
import { DISCORD, FakeNatives, janeIdentifiers } from "./fake-natives";

const request = (kind: ActionRequest["kind"], message: string | null, discordId = DISCORD): ActionRequest => ({
  id: "act-1",
  expiresAt: new Date(Date.now() + 5000),
  player: { uuid: null, steamId: null, discordId, name: "Jane" },
  kind,
  message,
  actorName: "mod.alex",
});

describe("actions", () => {
  it("messages, warns and kicks the online player with that Discord account", async () => {
    const n = new FakeNatives();
    n.addPlayer("7", "Jane", janeIdentifiers());
    const handle = createActionHandler(n, () => {});
    expect(await handle(request("message", "Hi there"))).toEqual({ ok: true });
    expect(await handle(request("warn", "Stop that"))).toEqual({ ok: true });
    expect(n.chat.map((c) => c.target)).toEqual(["7", "7"]);
    expect((n.chat[0].args[0] as { args: string[] }).args).toEqual(["[Staff] mod.alex", "Hi there"]);
    expect(await handle(request("kick", "Griefing"))).toEqual({ ok: true });
    expect(n.dropped).toEqual([{ source: "7", reason: "Kicked by staff: Griefing" }]);
  });

  it("answers player_offline and unsupported", async () => {
    const n = new FakeNatives();
    const handle = createActionHandler(n, () => {});
    expect(await handle(request("kick", "x"))).toMatchObject({ ok: false, error: "player_offline" });
    expect(await handle(request("unban", null))).toMatchObject({ ok: false, error: "unsupported" });
  });

  it("reads the allowlist and drops kinds the resource can't do", () => {
    const n = new FakeNatives();
    n.convars.ticket_tool_actions = "kick, message unban";
    const s = readSettings(n);
    expect(s.actions).toEqual(["kick", "message"]);
    expect(s.problems).toHaveLength(1);
    expect(readSettings(new FakeNatives()).actions).toEqual([]);
    expect(readSettings(new FakeNatives()).reportsEnabled).toBe(false);
  });
});

describe("report command", () => {
  const setup = (reports: boolean) => {
    const n = new FakeNatives();
    n.convars.ticket_tool_reports = String(reports);
    n.addPlayer("7", "Jane", janeIdentifiers());
    n.addPlayer("9", "Flyer", ["license:ffff"]);
    const report = vi.fn(async (): Promise<ReportResult> => ({ outcome: "opened", success: true, error: null }));
    const client = { report } as unknown as TicketToolClient;
    const name = registerReportCommand(n, () => readSettings(n), () => client, () => {});
    const run = (source: number, ...args: string[]) => n.commands.get(name)!.handler(source, args, "");
    return { n, report, run, name };
  };

  it("sends the reporter's own identity and an online target by server id", async () => {
    const { n, report, run, name } = setup(true);
    expect(name).toBe("report");
    run(7, "9", "is", "flying");
    await vi.waitFor(() => expect(report).toHaveBeenCalledOnce());
    expect(report).toHaveBeenCalledWith(
      { discordId: DISCORD, name: "Jane" },
      "is flying",
      { discordId: null, name: "Flyer" },
      null,
    );
    await vi.waitFor(() => expect(JSON.stringify(n.chat.at(-1))).toContain("reached the staff team"));
  });

  it("enforces the cooldown and stays quiet when reports are off", async () => {
    const on = setup(true);
    on.run(7, "help");
    on.run(7, "again");
    await vi.waitFor(() => expect(on.report).toHaveBeenCalledOnce());
    expect(JSON.stringify(on.n.chat)).toContain("You just sent a report");

    const off = setup(false);
    off.run(7, "help");
    expect(off.report).not.toHaveBeenCalled();
    expect(JSON.stringify(off.n.chat)).toContain("aren't turned on");
  });
});
