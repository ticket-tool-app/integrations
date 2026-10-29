import { clip, emptyToNull } from "./text";
import { ApiError } from "./errors";
import {
  ACTION_KINDS,
  ActionKind,
  ActionRequest,
  ActionResult,
  BanInfo,
  HelloReply,
  LinkOutcome,
  LinkResult,
  LookupPart,
  LookupRequest,
  LookupResult,
  PROTOCOL_VERSION,
  PlayerProfile,
  PluginInfo,
  ReportOutcome,
  ReportResult,
} from "./types";

export type Json = Record<string, unknown>;

const KNOWN_PARTS: readonly string[] = ["profile", "bans"];
const STEAM_ID64 = /^7656119\d{10}$/;
const SNOWFLAKE = /^\d{15,21}$/;
export const MAX_REASON = 2000;
export const MAX_FIELDS = 6;
export const MAX_ACTION_MESSAGE = 500;
export const MAX_REPORT_MESSAGE = 1000;
export const REPORT_EVENT = "player.report";

const obj = (v: unknown): Json => (v != null && typeof v === "object" && !Array.isArray(v) ? (v as Json) : {});
const str = (v: unknown): string | null => (typeof v === "string" ? v : null);
const num = (v: unknown): number | null => (typeof v === "number" && Number.isFinite(v) ? v : null);

export function userAgent(info: PluginInfo): string {
  const base = `${info.name}/${clip(info.version, 32)}`;
  return clip(info.platform ? `${base} (${clip(info.platform, 64)})` : base, 100);
}

export function hello(
  info: PluginInfo,
  banProviders: readonly string[],
  actions: readonly ActionKind[] = [],
  events: readonly string[] = [],
): Json {
  const plugin: Json = { type: info.type, version: clip(info.version, 32) };
  if (info.platform) plugin.platform = clip(info.platform, 64);
  const body: Json = { protocol: PROTOCOL_VERSION, plugin };
  if (banProviders.length > 0) body.banProviders = banProviders.slice(0, 8).map((p) => clip(p, 32));
  const on = ACTION_KINDS.filter((k) => actions.includes(k));
  if (on.length > 0) body.actions = on;
  if (events.includes(REPORT_EVENT)) body.events = [REPORT_EVENT];
  return body;
}

export function helloReply(body: Json): HelloReply {
  const key = obj(body.key);
  const server = obj(body.server);
  const wait = num(obj(body.poll).waitSeconds);
  return {
    protocol: num(body.protocol) ?? PROTOCOL_VERSION,
    pluginType: str(body.pluginType),
    keyName: str(key.name),
    serverId: str(server.id),
    serverName: str(server.name),
    pollWaitSeconds: wait == null ? 25 : Math.max(1, Math.min(120, Math.floor(wait))),
  };
}

/** Requests the SDK can act on; malformed entries are skipped, not fatal. */
export function pollRequests(body: Json): LookupRequest[] {
  const out: LookupRequest[] = [];
  const list = Array.isArray(body.requests) ? body.requests : [];
  for (const item of list) {
    const req = obj(item);
    const id = str(req.id);
    const type = str(req.type);
    const expiresRaw = str(req.expiresAt);
    const expiresAt = expiresRaw ? new Date(expiresRaw) : null;
    if (!id || !type || !expiresAt || Number.isNaN(expiresAt.getTime())) continue;
    const include: LookupPart[] = [];
    for (const part of Array.isArray(req.include) ? req.include : []) {
      if (typeof part === "string" && KNOWN_PARTS.includes(part) && !include.includes(part as LookupPart)) {
        include.push(part as LookupPart);
      }
    }
    out.push({
      id,
      type,
      expiresAt,
      player: player(obj(req.player)),
      include,
      wantsProfile: include.includes("profile"),
      wantsBans: include.includes("bans"),
    });
  }
  return out;
}

function player(p: Json) {
  return {
    uuid: str(p.uuid),
    steamId: str(p.steamId),
    discordId: str(p.discordId),
    name: str(p.name),
  };
}

/** `player.action` requests; malformed entries and unknown kinds are skipped. */
export function pollActions(body: Json): ActionRequest[] {
  const out: ActionRequest[] = [];
  const list = Array.isArray(body.requests) ? body.requests : [];
  for (const item of list) {
    const req = obj(item);
    if (req.type !== "player.action") continue;
    const id = str(req.id);
    const expiresRaw = str(req.expiresAt);
    const expiresAt = expiresRaw ? new Date(expiresRaw) : null;
    const action = obj(req.action);
    const kind = str(action.kind);
    if (!id || !expiresAt || Number.isNaN(expiresAt.getTime())) continue;
    if (!kind || !(ACTION_KINDS as readonly string[]).includes(kind)) continue;
    out.push({
      id,
      expiresAt,
      player: player(obj(req.player)),
      kind: kind as ActionKind,
      message: kind === "unban" ? null : clip(emptyToNull(str(action.message)), MAX_ACTION_MESSAGE),
      actorName: clip(emptyToNull(str(obj(req.actor).name)), 64) ?? "Ticket Tool",
    });
  }
  return out;
}

export function respondAction(request: ActionRequest, result: ActionResult): Json {
  if (result.ok) return { requestId: request.id, ok: true };
  const body: Json = { requestId: request.id, ok: false, error: result.error };
  const message = emptyToNull(result.message);
  if (message) body.message = clip(message, 200);
  return body;
}

export interface ReportPlayer {
  uuid?: string | null;
  steamId?: string | null;
  discordId?: string | null;
  name: string;
}

function reportPlayer(p: ReportPlayer): Json {
  const m: Json = {};
  if (p.uuid) m.uuid = p.uuid;
  if (p.steamId && STEAM_ID64.test(p.steamId)) m.steamId = p.steamId;
  if (p.discordId && SNOWFLAKE.test(p.discordId)) m.discordId = p.discordId;
  m.name = clip(emptyToNull(p.name) ?? "Unknown", 64);
  return m;
}

export function report(
  from: ReportPlayer,
  message: string,
  target?: ReportPlayer | null,
  server?: string | null,
): Json {
  return {
    type: REPORT_EVENT,
    player: reportPlayer(from),
    message: clip(message.trim(), MAX_REPORT_MESSAGE),
    target: target && emptyToNull(target.name) ? reportPlayer(target) : null,
    server: clip(emptyToNull(server), 64),
  };
}

export function reportReply(body: Json): ReportResult {
  return body.result === "opened"
    ? { outcome: "opened", success: true, error: null }
    : { outcome: "ignored", success: false, error: null };
}

export function reportError(e: ApiError): ReportResult {
  const error = e.error ?? `http_${e.status}`;
  return reportFailed(error === "rate_limited" ? "rate_limited" : "failed", error);
}

export function reportFailed(outcome: ReportOutcome, error: string): ReportResult {
  return { outcome, success: false, error };
}

export function respond(request: LookupRequest, result: LookupResult): Json {
  if (!result.ok) {
    const body: Json = { requestId: request.id, ok: false, error: result.error };
    const message = emptyToNull(result.message);
    if (message) body.message = clip(message, 200);
    return body;
  }
  const body: Json = { requestId: request.id, ok: true };
  if (result.profile && request.wantsProfile) body.profile = profile(result.profile);
  if (result.bans && request.wantsBans) body.bans = bans(result.bans);
  return body;
}

export function profile(p: PlayerProfile): Json {
  const m: Json = {};
  if (p.uuid) m.uuid = p.uuid;
  if (p.steamId && STEAM_ID64.test(p.steamId)) m.steamId = p.steamId;
  if (p.discordId && SNOWFLAKE.test(p.discordId)) m.discordId = p.discordId;
  m.name = clip(emptyToNull(p.name) ?? "Unknown", 64);
  m.online = p.online;
  m.lastSeen = iso(p.lastSeen);
  m.firstSeen = iso(p.firstSeen);
  m.server = clip(emptyToNull(p.server), 64);
  const fields = (p.fields ?? [])
    .map((f) => ({ label: clip(emptyToNull(f.label), 32), value: clip(emptyToNull(f.value), 100) }))
    .filter((f): f is { label: string; value: string } => f.label != null && f.value != null)
    .slice(0, MAX_FIELDS);
  if (fields.length > 0) m.fields = fields;
  return m;
}

export function bans(b: BanInfo): Json {
  const banned = b.banned;
  const m: Json = {
    banned,
    provider: clip(b.provider.trim().toLowerCase(), 32),
    reason: banned ? clip(emptyToNull(b.reason), MAX_REASON) : null,
    createdAt: banned ? iso(b.createdAt) : null,
    expiresAt: banned ? iso(b.expiresAt) : null,
    actor: banned ? clip(emptyToNull(b.actor), 64) : null,
  };
  const count = b.activeCount;
  if (banned && count != null && Number.isInteger(count) && count > 1 && count <= 100_000) {
    m.activeCount = count;
  }
  return m;
}

export interface LinkPlayer {
  uuid?: string | null;
  steamId?: string | null;
  discordId?: string | null;
  name: string;
}

export function link(code: string, player: LinkPlayer): Json {
  const p: Json = {};
  if (player.uuid) p.uuid = player.uuid;
  if (player.steamId) p.steamId = player.steamId;
  if (player.discordId) p.discordId = player.discordId;
  p.name = player.name;
  return { code: clip(code.trim(), 32), player: p };
}

export function linkReply(body: Json): LinkResult {
  const outcome: LinkOutcome = body.result === "relinked" ? "relinked" : "linked";
  return {
    outcome,
    success: true,
    discordUsername: str(obj(body.discord).username),
    playerName: str(obj(body.player).name),
    error: null,
  };
}

export function linkError(e: ApiError): LinkResult {
  const error = e.error ?? `http_${e.status}`;
  let outcome: LinkOutcome;
  switch (error) {
    case "invalid_code":
      outcome = "invalid_code";
      break;
    case "unknown_player":
      outcome = "unknown_player";
      break;
    case "unavailable":
      outcome = e.body.retryable === true ? "unavailable" : "failed";
      break;
    case "rate_limited":
      outcome = "rate_limited";
      break;
    default:
      outcome = "failed";
  }
  return linkFailed(outcome, error);
}

export function linkFailed(outcome: LinkOutcome, error: string): LinkResult {
  return { outcome, success: false, discordUsername: null, playerName: null, error };
}

export function iso(t: Date | null | undefined): string | null {
  return t == null || Number.isNaN(t.getTime()) ? null : t.toISOString();
}
