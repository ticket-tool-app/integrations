import { ReportResult, TicketToolClient } from "@ticket-tool/plugin-sdk";
import { Settings } from "./config";
import { onlinePlayer } from "./identity";
import { Natives } from "./natives";

/**
 * `/report [server id] <message>`. The reporter is always the player who ran
 * it; a first word that is an online player's server id becomes the target.
 */
export function registerReportCommand(
  n: Natives,
  settings: () => Settings,
  client: () => TicketToolClient | null,
  warn: (m: string) => void,
): string {
  const last = new Map<string, number>();
  const name = settings().reportCommand;
  const tell = (source: string, text: string) =>
    n.emitNet("chat:addMessage", source, { color: [80, 140, 255], args: ["Ticket Tool", text] });

  n.RegisterCommand(
    name,
    (rawSource, args) => {
      const source = String(rawSource);
      if (rawSource === 0) {
        n.print("^5[Ticket Tool]^7 Only players can send a report.");
        return;
      }
      const c = client();
      if (!settings().reportsEnabled || !c) {
        tell(source, "Reports aren't turned on for this server.");
        return;
      }
      const targetSource = args.length > 1 && /^\d+$/.test(args[0]) && n.GetPlayers().includes(args[0]) ? args[0] : null;
      const text = (targetSource ? args.slice(1) : args).join(" ").trim();
      if (!text) {
        tell(source, `Type /${name} <message>, or /${name} <player id> <message> to report someone.`);
        return;
      }
      const me = onlinePlayer(n, source);
      if (!me.identity.discordId) {
        tell(source, "Link Discord to FiveM (Settings > Account in the FiveM client) to send reports.");
        return;
      }
      const cooldownMs = settings().reportCooldownSeconds * 1000;
      const now = Date.now();
      const prev = last.get(me.identity.discordId);
      if (prev !== undefined && now - prev < cooldownMs) {
        tell(source, `You just sent a report. You can send another in ${Math.ceil((cooldownMs - (now - prev)) / 1000)}s.`);
        return;
      }
      last.set(me.identity.discordId, now);
      const target = targetSource ? onlinePlayer(n, targetSource) : null;
      tell(source, "Sending your report...");
      void c
        .report(
          { discordId: me.identity.discordId, name: me.name },
          text,
          target ? { discordId: target.identity.discordId, name: target.name } : null,
          settings().serverName,
        )
        .then((result: ReportResult) => {
          if (result.outcome === "failed" && me.identity.discordId) last.delete(me.identity.discordId);
          tell(source, replyFor(result, me.name, warn));
        });
    },
    false,
  );
  return name;
}

function replyFor(result: ReportResult, who: string, warn: (m: string) => void): string {
  switch (result.outcome) {
    case "opened":
      return "Thanks! Your report reached the staff team.";
    case "ignored":
      return "The staff team isn't taking reports this way right now. Try their Discord server instead.";
    case "rate_limited":
      return "Lots of reports are coming in right now. Try again in a minute.";
    case "not_enabled":
      return "Reports aren't turned on for this server.";
    default:
      if (result.error === "plan_not_entitled") warn("Reports need the Community or Pro plan on the Discord server.");
      else if (result.error !== "not_running") warn(`Sending ${who}'s report failed: ${result.error}`);
      return "Your report couldn't be sent. Try again in a minute.";
  }
}
