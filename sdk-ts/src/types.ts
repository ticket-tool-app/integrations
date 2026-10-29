export const PROTOCOL_VERSION = 1;

/** What the plugin reports about itself in `/hello` and `X-TT-Plugin`. */
export interface PluginInfo {
  /** The key's plugin type: `minecraft`, `fivem`, `gmod`, `rust` or `cs2`. */
  type: string;
  /** Product name for the header, e.g. `ticket-tool-fivem`. */
  name: string;
  version: string;
  /** Server software, e.g. `FiveM b3095`. */
  platform?: string | null;
}

export interface HelloReply {
  protocol: number;
  pluginType: string | null;
  /** The name the admin gave the key, e.g. "Roleplay 1". */
  keyName: string | null;
  serverId: string | null;
  serverName: string | null;
  pollWaitSeconds: number;
}

/** The player a lookup is about. A plugin reads only the identity its type uses. */
export interface RequestPlayer {
  uuid: string | null;
  /** SteamID64 as a decimal string. */
  steamId: string | null;
  /** Discord snowflake as a string. */
  discordId: string | null;
  /** The last-known name; not authoritative. */
  name: string | null;
}

export type LookupPart = "profile" | "bans";

/** One question from `/poll`. Parts the SDK doesn't know are dropped from `include`. */
export interface LookupRequest {
  id: string;
  type: string;
  expiresAt: Date;
  player: RequestPlayer;
  include: LookupPart[];
  wantsProfile: boolean;
  wantsBans: boolean;
}

export interface ProfileField {
  /** At most 32 characters. */
  label: string;
  /** At most 100 characters. */
  value: string;
}

/** The `profile` part of an answer. Send the identity field your plugin type requires. */
export interface PlayerProfile {
  uuid?: string | null;
  steamId?: string | null;
  discordId?: string | null;
  name: string;
  online: boolean;
  lastSeen?: Date | null;
  firstSeen?: Date | null;
  /** Backend or world name; null to leave it out. */
  server?: string | null;
  /** Up to 6 label/value pairs for the staff card. */
  fields?: ProfileField[] | null;
}

/** The `bans` part of an answer. */
export interface BanInfo {
  banned: boolean;
  /** e.g. `txadmin` or `server`. */
  provider: string;
  reason?: string | null;
  createdAt?: Date | null;
  /** Null means permanent. */
  expiresAt?: Date | null;
  actor?: string | null;
  /** Active bans on record; only sent when more than one. */
  activeCount?: number | null;
}

export type LookupError = "unknown_player" | "no_ban_provider" | "unsupported" | "error";

export type LookupResult =
  | { ok: true; profile?: PlayerProfile | null; bans?: BanInfo | null }
  | { ok: false; error: LookupError; message?: string | null };

export const LookupResult = {
  answer(profile: PlayerProfile | null | undefined, bans: BanInfo | null | undefined): LookupResult {
    return { ok: true, profile: profile ?? null, bans: bans ?? null };
  },
  failure(error: LookupError, message?: string | null): LookupResult {
    return { ok: false, error, message: message ?? null };
  },
};

/**
 * Answers lookups. It runs off the poll loop, so it may take its time up to
 * the answer budget; a rejected promise is sent as an `error` answer.
 */
export type RequestHandler = (request: LookupRequest) => Promise<LookupResult> | LookupResult;

/** What staff can ask a plugin to do in game. Each is off until the server owner turns it on. */
export type ActionKind = "message" | "warn" | "kick" | "unban";

export const ACTION_KINDS: readonly ActionKind[] = ["message", "warn", "kick", "unban"];

/** A `player.action` request from `/poll`. */
export interface ActionRequest {
  id: string;
  expiresAt: Date;
  player: RequestPlayer;
  kind: ActionKind;
  /** Plain text for the player, at most 500 characters; null for `unban`. */
  message: string | null;
  /** The staff member's Discord username, or `Ticket Tool` for an automation. */
  actorName: string;
}

export type ActionError =
  | "player_offline"
  | "not_banned"
  | "not_allowed"
  | "unknown_player"
  | "unsupported"
  | "error";

export type ActionResult = { ok: true } | { ok: false; error: ActionError; message?: string | null };

export const ActionResult = {
  done(): ActionResult {
    return { ok: true };
  },
  failure(error: ActionError, message?: string | null): ActionResult {
    return { ok: false, error, message: message ?? null };
  },
};

/**
 * Carries out actions the server owner turned on. The SDK answers
 * `not_allowed` itself for any other kind, so this only sees enabled ones.
 */
export type ActionHandler = (request: ActionRequest) => Promise<ActionResult> | ActionResult;

export type ReportOutcome =
  | "opened"
  /** No automation rule on the Discord server took the report. */
  | "ignored"
  | "rate_limited"
  /** Reports are off in the plugin's config. */
  | "not_enabled"
  | "failed";

export interface ReportResult {
  outcome: ReportOutcome;
  success: boolean;
  /** The server's error code on failure, or a local one (`network`, `not_running`). */
  error: string | null;
}

export type LinkOutcome =
  | "linked"
  | "relinked"
  | "invalid_code"
  | "unknown_player"
  /** The account service didn't answer; the code is still good. */
  | "unavailable"
  | "rate_limited"
  | "failed";

export interface LinkResult {
  outcome: LinkOutcome;
  success: boolean;
  discordUsername: string | null;
  playerName: string | null;
  /** The server's error code on failure, or a local one (`network`, `not_running`). */
  error: string | null;
}

export type ClientState =
  | "starting"
  /** A poll is open or just returned. */
  | "connected"
  /** Network or server trouble; retrying with backoff. */
  | "reconnecting"
  /** The Discord server's plan doesn't include game lookups; retrying hourly. */
  | "paused_plan"
  /** The key was revoked, rotated or never existed. Needs a new key. */
  | "stopped_invalid_key"
  /** Another error that retrying won't fix, e.g. the key is for a different game. */
  | "stopped_error"
  | "stopped";

export function isRunningState(state: ClientState): boolean {
  return state === "starting" || state === "connected" || state === "reconnecting" || state === "paused_plan";
}

export interface ClientStatus {
  state: ClientState;
  /** The last problem, already phrased for an admin. */
  lastError: string | null;
  lastErrorAt: Date | null;
  hello: HelloReply | null;
  lastPollAt: Date | null;
  answered: number;
  clockOffsetSeconds: number;
}
