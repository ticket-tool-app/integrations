import { BanProviderSetting } from "./bans";
import { Natives } from "./natives";

export interface Settings {
  key: string | null;
  apiUrl: string | null;
  serverName: string | null;
  banProvider: BanProviderSetting;
  txAdminDb: string | null;
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

  const debugRaw = (convar("ticket_tool_debug") ?? pick(file, "debug") ?? "false").toLowerCase();
  return {
    key: convar("ticket_tool_key") ?? pick(file, "key"),
    apiUrl: convar("ticket_tool_api_url") ?? pick(file, "apiUrl", "api-url"),
    serverName: convar("ticket_tool_server_name") ?? pick(file, "serverName", "server-name"),
    banProvider,
    txAdminDb: convar("ticket_tool_txadmin_db") ?? pick(file, "txAdminDb", "txadmin-db"),
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
