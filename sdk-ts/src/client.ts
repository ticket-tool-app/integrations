import { newNonce, parseKey, signRequest, verifyReply, type ParsedKey } from "./signing.js";

export const PROTOCOL_VERSION = 1;
export const DEFAULT_API_URL = "https://dash.ticket-tool.app";
const API_PATH = "/api/plugin/v1";
const SIGNATURE_WINDOW_SECONDS = 300;
const MAX_REPLY_BYTES = 256 * 1024;
const CALL_TIMEOUT_MS = 10_000;
const GOODBYE_TIMEOUT_MS = 2_000;
const PLAN_RETRY_MS = 60 * 60_000;
const RATE_LIMIT_WAIT_MS = 60_000;
// A crashed process's poll lease lapses on the server within 40 s.
const TOO_MANY_POLLS_MIN_WAIT_MS = 15_000;

export type Json = Record<string, unknown>;

export interface PluginInfo {
  /** The key's plugin type, e.g. "fivem". */
  type: string;
  /** Shown on the dashboard, e.g. "ticket-tool-cfx". */
  name: string;
  version: string;
  /** e.g. "FXServer 12345". */
  platform?: string;
}

export interface LookupRequest {
  id: string;
  type: string;
  expiresAt: string;
  /** Identity fields depend on the game: uuid, steamId, discordId, license… */
  player: Json;
  include: string[];
}

export type LookupAnswer =
  | { ok: true; profile?: Json | null; bans?: Json | null }
  | { ok: false; error: "unknown_player" | "no_ban_provider" | "unsupported" | "error"; message?: string };

export type LinkOutcome = "linked" | "relinked" | "invalid_code" | "unknown_player" | "unavailable" | "rate_limited" | "failed";

export interface LinkResult {
  outcome: LinkOutcome;
  discordUsername?: string;
  playerName?: string;
  /** Server error code, or "network" / "not_running". */
  error?: string;
}

export type ClientState =
  | "stopped"
  | "starting"
  | "connected"
  | "reconnecting"
  | "paused_plan"
  | "stopped_invalid_key"
  | "stopped_error";

export interface ClientStatus {
  state: ClientState;
  lastError: string | null;
  hello: Json | null;
  answered: number;
  clockOffsetSeconds: number;
}

export interface Logger {
  info(message: string): void;
  warn(message: string, error?: unknown): void;
  debug(message: string): void;
}

export interface ClientOptions {
  /** The full ttp_... key. Only the derived signing secret is kept. */
  key: string;
  plugin: PluginInfo;
  /** Called for each lookup. Return within the budget; a late answer is not sent. */
  handler: (request: LookupRequest) => Promise<LookupAnswer> | LookupAnswer;
  /** Host root or a base ending in /api/plugin/v1. */
  apiUrl?: string;
  /** Read at each /hello. */
  banProviders?: () => string[];
  logger?: Logger;
  /** Per-answer budget, capped by the request's expiresAt. Default 3000. */
  answerBudgetMs?: number;
  /** For tests. */
  fetch?: typeof fetch;
  now?: () => number;
  sleep?: (ms: number, signal: AbortSignal) => Promise<void>;
  random?: () => number;
}

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly error: string | null,
    readonly body: Json,
  ) {
    super(`HTTP ${status}${error ? ` ${error}` : ""}`);
  }
}

export class ReplyVerificationError extends Error {}

const SILENT: Logger = { info() {}, warn() {}, debug() {} };

export function apiBase(apiUrl: string | undefined): string {
  let url = (apiUrl ?? "").trim() || DEFAULT_API_URL;
  url = url.replace(/\/+$/, "");
  if (!url.endsWith(API_PATH)) url += API_PATH;
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    throw new Error(`api-url isn't a valid URL: ${apiUrl}`);
  }
  const host = parsed.hostname;
  const loopback = host === "localhost" || host.startsWith("127.") || host === "[::1]" || host === "::1";
  if (parsed.protocol !== "https:" && !(parsed.protocol === "http:" && loopback)) {
    throw new Error("api-url must use https:// (plain http is only allowed for localhost)");
  }
  return url;
}

const defaultSleep = (ms: number, signal: AbortSignal) =>
  new Promise<void>((resolve) => {
    if (signal.aborted) return resolve();
    const t = setTimeout(done, ms);
    function done() {
      clearTimeout(t);
      signal.removeEventListener("abort", done);
      resolve();
    }
    signal.addEventListener("abort", done);
  });

/**
 * A Ticket Tool game plugin client: /hello, one long-poll loop, answers
 * dispatched without blocking the loop, /link on demand and /goodbye on stop().
 */
export class TicketToolClient {
  private readonly key: ParsedKey;
  private readonly base: string;
  private readonly userAgent: string;
  private readonly opts: ClientOptions;
  private readonly log: Logger;
  private readonly fetch: typeof fetch;
  private readonly now: () => number;
  private readonly sleep: (ms: number, signal: AbortSignal) => Promise<void>;
  private readonly random: () => number;

  private readonly stopper = new AbortController();
  private running = false;
  private closed = false;
  private everConnected = false;
  private loop: Promise<void> | null = null;
  private offsetSeconds = 0;
  private backoffAttempt = 0;
  private failureStreak = 0;

  private state: ClientState = "stopped";
  private lastError: string | null = null;
  private hello: Json | null = null;
  private answered = 0;

  constructor(opts: ClientOptions) {
    this.key = parseKey(opts.key);
    this.base = apiBase(opts.apiUrl);
    this.opts = opts;
    const p = opts.plugin;
    this.userAgent = `${p.name}/${p.version}${p.platform ? ` (${p.platform})` : ""}`;
    this.log = opts.logger ?? SILENT;
    this.fetch = opts.fetch ?? globalThis.fetch;
    this.now = opts.now ?? Date.now;
    this.sleep = opts.sleep ?? defaultSleep;
    this.random = opts.random ?? Math.random;
  }

  /** ttp_<keyId>: safe to log. */
  get keyLabel(): string {
    return `ttp_${this.key.keyId}`;
  }

  status(): ClientStatus {
    return {
      state: this.state,
      lastError: this.lastError,
      hello: this.hello,
      answered: this.answered,
      clockOffsetSeconds: this.offsetSeconds,
    };
  }

  /** Starts the poll loop and returns at once. */
  start(): void {
    if (this.closed) throw new Error("A stopped client can't be restarted; create a new one");
    if (this.running) return;
    this.running = true;
    this.state = "starting";
    this.loop = this.runLoop();
  }

  /** Stops polling and sends /goodbye, waiting at most about two seconds. */
  async stop(): Promise<void> {
    if (this.closed) return;
    this.closed = true;
    this.running = false;
    this.stopper.abort();
    await this.loop?.catch(() => {});
    if (this.everConnected && this.state !== "stopped_invalid_key") {
      await this.call("/goodbye", {}, GOODBYE_TIMEOUT_MS, false).catch((e) =>
        this.log.debug(`Goodbye not delivered: ${message(e)}`),
      );
    }
    if (!this.state.startsWith("stopped")) this.state = "stopped";
  }

  /**
   * Reports that a player typed a link code. Take the player's identity from
   * the game, never from the command's arguments. Never rejects.
   */
  async link(code: string, player: Json): Promise<LinkResult> {
    if (this.closed) return { outcome: "failed", error: "not_running" };
    try {
      const body = await this.call("/link", { code, player }, CALL_TIMEOUT_MS, false);
      const discord = body.discord as Json | undefined;
      const p = body.player as Json | undefined;
      return {
        outcome: body.result === "relinked" ? "relinked" : "linked",
        discordUsername: typeof discord?.username === "string" ? discord.username : undefined,
        playerName: typeof p?.name === "string" ? p.name : undefined,
      };
    } catch (e) {
      if (e instanceof ApiError) {
        const known: LinkOutcome[] = ["invalid_code", "unknown_player", "unavailable", "rate_limited"];
        const outcome = known.find((k) => k === e.error) ?? "failed";
        return { outcome, error: e.error ?? `http_${e.status}` };
      }
      this.log.warn(`Couldn't reach Ticket Tool to link a player: ${message(e)}`);
      return { outcome: "failed", error: "network" };
    }
  }

  private async runLoop(): Promise<void> {
    let needHello = true;
    while (this.running) {
      try {
        if (needHello) {
          const reply = await this.call(
            "/hello",
            {
              protocol: PROTOCOL_VERSION,
              plugin: { type: this.opts.plugin.type, version: this.opts.plugin.version, platform: this.opts.plugin.platform },
              banProviders: this.opts.banProviders?.() ?? [],
            },
            CALL_TIMEOUT_MS,
          );
          this.hello = reply;
          needHello = false;
          this.everConnected = true;
          const server = reply.server as Json | undefined;
          const key = reply.key as Json | undefined;
          this.log.info(
            `Connected to Ticket Tool as "${key?.name ?? "?"}" for Discord server ${server?.name ?? server?.id ?? "?"} (${this.keyLabel})`,
          );
        }
        const wait = Number((this.hello?.poll as Json | undefined)?.waitSeconds ?? 25);
        this.state = "connected";
        const body = await this.call("/poll", {}, Math.max(35, wait + 10) * 1000);
        this.recovered();
        const requests = Array.isArray(body.requests) ? (body.requests as LookupRequest[]) : [];
        for (const request of requests) this.dispatch(request);
      } catch (e) {
        if (!this.running) break;
        if (e instanceof ApiError) needHello = (await this.handleApiError(e)) || needHello;
        else if (e instanceof Error && e.name === "TimeoutError") await this.failed("Ticket Tool didn't answer in time", this.nextBackoff());
        else await this.failed(`Can't reach Ticket Tool: ${message(e)}`, this.nextBackoff(), e);
      }
    }
  }

  /** True when the next attempt should start over with /hello. */
  private async handleApiError(e: ApiError): Promise<boolean> {
    const error = e.error ?? `http_${e.status}`;
    switch (error) {
      case "invalid_key":
        this.fatal("stopped_invalid_key", `Ticket Tool doesn't recognize key ${this.keyLabel}. It was revoked, rotated or mistyped: paste the current key from the dashboard and reload.`);
        return false;
      case "wrong_plugin_type":
        this.fatal("stopped_error", `Key ${this.keyLabel} was made for a different game (${String(e.body.expected ?? "?")}). Create a ${this.opts.plugin.type} key on the dashboard.`);
        return false;
      case "unsupported_protocol":
        this.fatal("stopped_error", `Ticket Tool no longer serves protocol ${PROTOCOL_VERSION}. Update the plugin.`);
        return false;
      case "plan_not_entitled":
        this.state = "paused_plan";
        this.note("The Discord server's plan doesn't include game lookups. Checking again in an hour.");
        await this.sleep(PLAN_RETRY_MS, this.stopper.signal);
        return true;
      case "rate_limited":
        await this.failed("Ticket Tool is rate limiting this key; waiting a minute", RATE_LIMIT_WAIT_MS + this.random() * 5000);
        return false;
      case "too_many_polls":
        await this.failed(
          `Three polls are already open for key ${this.keyLabel}. Is the same key in use on another server? Each server needs its own key.`,
          Math.max(this.nextBackoff(), TOO_MANY_POLLS_MIN_WAIT_MS + this.random() * 5000),
        );
        return false;
      case "bad_signature":
      case "missing_headers":
      case "bad_nonce":
        await this.failed(`Ticket Tool rejected the request signature (${error}). Check the key was pasted whole.`, this.nextBackoff());
        return false;
      default:
        await this.failed(`Ticket Tool returned ${error} (HTTP ${e.status})`, this.nextBackoff());
        return false;
    }
  }

  private dispatch(request: LookupRequest): void {
    if (request?.type !== "player.lookup") {
      this.log.debug(`Ignoring request type ${String(request?.type)}`);
      return;
    }
    const received = this.serverNow();
    const expires = Date.parse(request.expiresAt);
    if (!(expires > received)) {
      this.log.debug(`Dropping expired request ${request.id}`);
      return;
    }
    void this.answer(request, received, expires);
  }

  private async answer(request: LookupRequest, received: number, expires: number): Promise<void> {
    let out: LookupAnswer;
    if (!request.player || typeof request.player !== "object" || !Array.isArray(request.include) || request.include.length === 0) {
      out = { ok: false, error: "unsupported" };
    } else {
      const deadline = Math.min(received + (this.opts.answerBudgetMs ?? 3000), expires);
      let timer: ReturnType<typeof setTimeout> | undefined;
      const timeout = new Promise<LookupAnswer>((resolve) => {
        timer = setTimeout(
          () => resolve({ ok: false, error: "error", message: "The game server took too long to answer" }),
          Math.max(1, deadline - received),
        );
      });
      try {
        out = await Promise.race([Promise.resolve().then(() => this.opts.handler(request)), timeout]);
        out ??= { ok: false, error: "error", message: "The plugin had no answer" };
      } catch (e) {
        this.log.warn(`Lookup ${request.id} failed`, e);
        out = { ok: false, error: "error", message: "The plugin hit an error answering this lookup" };
      } finally {
        clearTimeout(timer);
      }
    }
    if (this.serverNow() >= expires) {
      this.log.debug(`Answer for ${request.id} is too late; not sending it`);
      return;
    }
    try {
      await this.call("/respond", { requestId: request.id, ...out }, CALL_TIMEOUT_MS, false);
      this.answered++;
    } catch (e) {
      if (e instanceof ApiError && e.error === "unknown_request") this.log.debug(`Request ${request.id} expired before the answer arrived`);
      else this.log.warn(`Couldn't send the answer to ${request.id}: ${message(e)}`);
    }
  }

  /**
   * One signed request with a verified reply. Returns the body of an ok reply,
   * retries once after correcting the clock from stale_timestamp, and throws
   * ApiError for anything else.
   */
  private async call(endpoint: string, payload: Json, timeoutMs: number, stoppable = true): Promise<Json> {
    for (let attempt = 0; ; attempt++) {
      const { status, body } = await this.send(endpoint, payload, timeoutMs, stoppable);
      if (status >= 200 && status < 300 && body.ok !== false) return body;
      const error = new ApiError(status, typeof body.error === "string" ? body.error : null, body);
      if (attempt === 0 && error.error === "stale_timestamp" && typeof body.serverTime === "number") {
        this.offsetSeconds = body.serverTime - Math.floor(this.now() / 1000);
        continue;
      }
      throw error;
    }
  }

  private async send(endpoint: string, payload: Json, timeoutMs: number, stoppable: boolean) {
    // Serialize once: the hashed bytes are the sent bytes.
    const body = new TextEncoder().encode(JSON.stringify(payload));
    const ts = Math.floor(this.now() / 1000) + this.offsetSeconds;
    const nonce = newNonce();
    const signal = stoppable
      ? AbortSignal.any([AbortSignal.timeout(timeoutMs), this.stopper.signal])
      : AbortSignal.timeout(timeoutMs);
    const res = await this.fetch(this.base + endpoint, {
      method: "POST",
      body,
      signal,
      headers: {
        "content-type": "application/json",
        accept: "application/json",
        "x-tt-key-id": this.key.keyId,
        "x-tt-timestamp": String(ts),
        "x-tt-nonce": nonce,
        "x-tt-signature": signRequest(this.key.secret, "POST", endpoint, ts, nonce, body),
        "x-tt-protocol": String(PROTOCOL_VERSION),
        "x-tt-plugin": this.userAgent,
        "user-agent": this.userAgent,
      },
    });
    const raw = new Uint8Array(await res.arrayBuffer());
    if (raw.length > MAX_REPLY_BYTES) throw new Error(`Reply larger than ${MAX_REPLY_BYTES} bytes`);
    const signature = res.headers.get("x-tt-signature");
    if (signature) {
      if (res.headers.get("x-tt-nonce") !== nonce) throw new ReplyVerificationError("Reply nonce doesn't match the request");
      const rts = res.headers.get("x-tt-timestamp") ?? "";
      if (!/^\d+$/.test(rts)) throw new ReplyVerificationError("Reply timestamp missing or malformed");
      if (Math.abs(Math.floor(this.now() / 1000) + this.offsetSeconds - Number(rts)) > SIGNATURE_WINDOW_SECONDS) throw new ReplyVerificationError("Reply timestamp outside the signature window");
      if (!verifyReply(this.key.secret, endpoint, rts, nonce, raw, signature)) throw new ReplyVerificationError("Reply signature doesn't match");
    } else if (res.status >= 200 && res.status < 300) {
      // Only pre-auth errors may be unsigned; an unsigned success could be anyone's.
      throw new ReplyVerificationError(`Unsigned success reply from ${endpoint}`);
    }
    let parsed: Json = {};
    try {
      if (raw.length) parsed = JSON.parse(new TextDecoder().decode(raw)) as Json;
    } catch {
      if (res.status < 300) throw new Error(`Reply from ${endpoint} wasn't JSON`);
    }
    return { status: res.status, body: parsed && typeof parsed === "object" ? parsed : {} };
  }

  private serverNow(): number {
    return this.now() + this.offsetSeconds * 1000;
  }

  /** 1, 2, 4 … 60 s with equal jitter. */
  private nextBackoff(): number {
    const step = Math.min(60_000, 1000 * 2 ** Math.min(this.backoffAttempt++, 6));
    return step / 2 + this.random() * (step / 2);
  }

  private recovered() {
    if (this.failureStreak > 0) this.log.info("Reconnected to Ticket Tool");
    this.failureStreak = 0;
    this.backoffAttempt = 0;
  }

  private async failed(msg: string, waitMs: number, error?: unknown) {
    this.state = "reconnecting";
    this.note(msg, error);
    this.log.debug(`Retrying in ${Math.round(waitMs)} ms`);
    await this.sleep(waitMs, this.stopper.signal);
  }

  private note(msg: string, error?: unknown) {
    this.lastError = msg;
    if (this.failureStreak++ === 0) this.log.warn(msg, error);
    else this.log.debug(msg);
  }

  private fatal(state: ClientState, msg: string) {
    this.lastError = msg;
    this.state = state;
    this.running = false;
    this.log.warn(msg);
  }
}

function message(e: unknown): string {
  let t = e as { cause?: unknown; message?: string } | undefined;
  while (t?.cause && t.cause !== t) t = t.cause as typeof t;
  return t?.message || String(e);
}
