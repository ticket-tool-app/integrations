import { ActionKind } from "@ticket-tool/plugin-sdk";
import { SUPPORTED_ACTIONS } from "./actions";
import { BanProviderSetting } from "./bans";
import { Natives } from "./natives";

export interface Settings {
  key: string | null;
  apiUrl: string | null;
  serverName: string | null;
  banProvider: BanProviderSetting;
  txAdminDb: string | null;
  /** Actions staff may take from Discord; empty unless the owner lists them. */
  actions: ActionKind[];
  reportsEnabled: boolean;
  /** Chat command name for reports, without the slash. */
  reportCommand: string;
  reportCooldownSeconds: number;
  debug: boolean;
  /** Problems with the settings themselves, phrased for an admin. Never contains the key. */
  problems: string[];
}

const BAN_PROVIDERS: readonly BanProviderSetting[] = ["auto", "txadmin", "server", "none"];

const pick = (obj: Record<string, unknown>, ...names: string[]): string | null => {
  for (const n of names) {
    const v = obj[n];
    if (typeof v === "string" && v.trim() !== "") return v.trim();
    if (typeof v === "boolean") return String(v);
  }
  return null;
};

const listFromFile = (v: unknown): string | null =>
  Array.isArray(v) ? v.filter((i): i is string => typeof i === "string").join(",") : null;

/**
 * Settings come from convars (`set ticket_tool_key "ttp_..."` in server.cfg)
 * and fall back to the resource's optional `config.json`. A convar that is set
 * wins, so a host panel's startup variables override a file shipped with a pack.
 */
export function readSettings(n: Natives): Settings {
  const problems: string[] = [];
  let file: Record<string, unknown> = {};
  let raw: string | null = null;
  try {
    raw = n.LoadResourceFile(n.GetCurrentResourceName(), "config.json");
  } catch {
    raw = null;
  }
  if (raw && raw.trim() !== "") {
    try {
      const parsed: unknown = JSON.parse(raw);
      if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) file = parsed as Record<string, unknown>;
      else problems.push("config.json isn't a JSON object; ignoring it");
    } catch {
      problems.push("config.json isn't valid JSON; ignoring it");
    }
  }
  const convar = (name: string): string | null => {
    const v = n.GetConvar(name, "").trim();
    return v === "" ? null : v;
  };

  const banRaw = (convar("ticket_tool_ban_provider") ?? pick(file, "banProvider", "ban-provider") ?? "auto").toLowerCase();
  let banProvider: BanProviderSetting = "auto";
  if ((BAN_PROVIDERS as readonly string[]).includes(banRaw)) banProvider = banRaw as BanProviderSetting;
  else problems.push(`ticket_tool_ban_provider "${banRaw}" isn't one of ${BAN_PROVIDERS.join(", ")}; using auto`);

  const actions: ActionKind[] = [];
  const actionsRaw = convar("ticket_tool_actions") ?? pick(file, "actions") ?? listFromFile(file.actions) ?? "";
  for (const item of actionsRaw.split(/[\s,]+/).filter(Boolean).map((s) => s.toLowerCase())) {
    if ((SUPPORTED_ACTIONS as readonly string[]).includes(item)) {
      if (!actions.includes(item as ActionKind)) actions.push(item as ActionKind);
    } else {
      problems.push(`ticket_tool_actions: "${item}" isn't one of ${SUPPORTED_ACTIONS.join(", ")}; ignoring it`);
    }
  }
  const reportsRaw = (convar("ticket_tool_reports") ?? pick(file, "reports") ?? "false").toLowerCase();
  let reportCommand = (convar("ticket_tool_report_command") ?? pick(file, "reportCommand", "report-command") ?? "report")
    .replace(/^\//, "")
    .toLowerCase();
  if (!/^[a-z0-9_]{1,32}$/.test(reportCommand)) {
    problems.push(`ticket_tool_report_command "${reportCommand}" isn't a valid command name; using report`);
    reportCommand = "report";
  }
  const cooldownRaw = Number(convar("ticket_tool_report_cooldown") ?? pick(file, "reportCooldown", "report-cooldown") ?? 60);
  const debugRaw = (convar("ticket_tool_debug") ?? pick(file, "debug") ?? "false").toLowerCase();
  return {
    key: convar("ticket_tool_key") ?? pick(file, "key"),
    apiUrl: convar("ticket_tool_api_url") ?? pick(file, "apiUrl", "api-url"),
    serverName: convar("ticket_tool_server_name") ?? pick(file, "serverName", "server-name"),
    banProvider,
    txAdminDb: convar("ticket_tool_txadmin_db") ?? pick(file, "txAdminDb", "txadmin-db"),
    actions,
    reportsEnabled: reportsRaw === "true" || reportsRaw === "1",
    reportCommand,
    reportCooldownSeconds: Number.isFinite(cooldownRaw) && cooldownRaw >= 0 ? Math.floor(cooldownRaw) : 60,
    debug: debugRaw === "true" || debugRaw === "1",
    problems,
  };
}

/** `FiveM b3095` or `RedM b1491`, plus the server artifact when it can be read. */
export function platformString(n: Natives): string {
  const game = n.GetConvar("gamename", "gta5").trim().toLowerCase();
  let out = game === "rdr3" ? "RedM" : "FiveM";
  const build = n.GetConvar("sv_enforceGameBuild", "").trim();
  if (/^\d+$/.test(build)) out += ` b${build}`;
  const artifact = /v1\.0\.0\.(\d+)/.exec(n.GetConvar("version", ""));
  if (artifact) out += ` (server ${artifact[1]})`;
  return out;
}
