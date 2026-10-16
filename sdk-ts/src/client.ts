import { Backoff } from "./backoff";
import { AbortedError, ApiError, HttpTimeoutError } from "./errors";
import { HttpSender, nodeHttpSender } from "./http";
import { PluginKey } from "./key";
import * as Messages from "./messages";
import { SdkLogger, silentLogger } from "./logger";
import { Transport, apiBase } from "./transport";
import {
  ClientState,
  ClientStatus,
  HelloReply,
  LinkResult,
  LookupRequest,
  LookupResult,
  PROTOCOL_VERSION,
  PluginInfo,
  RequestHandler,
  isRunningState,
} from "./types";

export const CALL_TIMEOUT_MS = 10_000;
export const GOODBYE_TIMEOUT_MS = 2_000;
export const PLAN_RETRY_MS = 60 * 60 * 1000;
export const RATE_LIMIT_WAIT_MS = 60_000;
// A crashed process's poll lease lapses on the server within 40 s.
export const TOO_MANY_POLLS_MIN_WAIT_MS = 15_000;

export interface ClientOptions {
  /** The full integration key; only the derived signing secret is kept. */
  key: string;
  /** Host root such as `https://dash.ticket-tool.app`, or a base ending in `/api/plugin/v1`. */
  apiUrl?: string | null;
  plugin: PluginInfo;
  handler: RequestHandler;
  /** Read at each `/hello`, e.g. `["txadmin"]`. */
  banProviders?: () => readonly string[];
  logger?: SdkLogger;
  /** Time allowed per answer before a timeout error is sent instead; capped by the request's expiry. */
  answerBudgetMs?: number;
  /** Epoch milliseconds. */
  clock?: () => number;
  sleep?: (ms: number, signal: AbortSignal) => Promise<void>;
  random?: () => number;
  http?: HttpSender;
}

const abortableSleep = (ms: number, signal: AbortSignal): Promise<void> =>
  new Promise((resolve) => {
    if (signal.aborted) return resolve();
    const done = () => {
      clearTimeout(timer);
      signal.removeEventListener("abort", done);
      resolve();
    };
    const timer = setTimeout(done, ms);
    signal.addEventListener("abort", done);
  });

function rootMessage(e: unknown): string {
  let t: unknown = e;
  while (t instanceof Error && (t as { cause?: unknown }).cause instanceof Error) {
    t = (t as { cause?: unknown }).cause;
  }
  if (t instanceof Error) {
    const code = (t as { code?: unknown }).code;
    return t.message || (typeof code === "string" ? code : t.name);
  }
  return String(t);
}

/**
 * A Ticket Tool game plugin client: `/hello`, one long-poll loop, answers off
 * the loop, `/link` on demand and `/goodbye` on `stop()`.
 */
export class TicketToolClient {
  readonly key: PluginKey;
  readonly #plugin: PluginInfo;
  readonly #handler: RequestHandler;
  readonly #banProviders: () => readonly string[];
  readonly #log: SdkLogger;
  readonly #clock: () => number;
  readonly #sleep: (ms: number, signal: AbortSignal) => Promise<void>;
  readonly #random: () => number;
  readonly #answerBudgetMs: number;
  readonly #transport: Transport;
  readonly #backoff: Backoff;
  readonly #stop = new AbortController();

  #running = false;
  #closed = false;
  #everConnected = false;
  #loop: Promise<void> | null = null;
  #inFlight = new Set<Promise<unknown>>();

  #state: ClientState = "stopped";
  #lastError: string | null = null;
  #lastErrorAt: Date | null = null;
  #hello: HelloReply | null = null;
  #lastPollAt: Date | null = null;
  #answered = 0;
  #failureStreak = 0;

  constructor(options: ClientOptions) {
    this.key = PluginKey.parse(options.key);
    if (!options.plugin) throw new Error("plugin is required");
    if (typeof options.handler !== "function") throw new Error("handler is required");
    this.#plugin = options.plugin;
    this.#handler = options.handler;
    this.#banProviders = options.banProviders ?? (() => []);
    this.#log = options.logger ?? silentLogger;
    this.#clock = options.clock ?? Date.now;
    this.#sleep = options.sleep ?? abortableSleep;
    this.#random = options.random ?? Math.random;
    this.#answerBudgetMs = options.answerBudgetMs ?? 3000;
    this.#transport = new Transport(
      options.http ?? nodeHttpSender,
      apiBase(options.apiUrl),
      this.key,
      Messages.userAgent(this.#plugin),
      this.#clock,
    );
    this.#backoff = Backoff.standard(this.#random);
  }

  status(): ClientStatus {
    return {
      state: this.#state,
      lastError: this.#lastError,
      lastErrorAt: this.#lastErrorAt,
      hello: this.#hello,
      lastPollAt: this.#lastPollAt,
      answered: this.#answered,
      clockOffsetSeconds: this.#transport.offsetSeconds,
    };
  }

  /** Starts the poll loop. Returns at once; `/hello` is sent from the loop. */
  start(): void {
    if (this.#closed) throw new Error("A stopped client can't be restarted; create a new one");
    if (this.#running) return;
    this.#running = true;
    this.#state = "starting";
    this.#loop = this.#runLoop().catch((e) => {
      this.#log.warn("Ticket Tool poll loop crashed", e);
      this.#fatal("stopped_error", "The poll loop crashed: " + rootMessage(e));
    });
  }

  /**
   * Stops polling and sends `/goodbye` so lookups show the plugin as offline
   * at once. Settles within about three seconds and never rejects.
   */
  async stop(): Promise<void> {
    if (this.#closed) return;
    this.#closed = true;
    this.#running = false;
    this.#stop.abort();
    if (this.#loop) {
      await Promise.race([this.#loop, new Promise((r) => setTimeout(r, 1000))]);
    }
    if (this.#everConnected && this.#state !== "stopped_invalid_key") {
      try {
        await this.#transport.call("/goodbye", {}, GOODBYE_TIMEOUT_MS);
      } catch (e) {
        this.#log.debug("Goodbye not delivered: " + rootMessage(e));
      }
    }
    if (isRunningState(this.#state)) this.#state = "stopped";
  }

  /** Resolves once every answer and link call started so far has finished; for tests and shutdown. */
  async idle(): Promise<void> {
    while (this.#inFlight.size > 0) {
      await Promise.allSettled([...this.#inFlight]);
    }
  }

  /**
   * Reports that a player typed a link code. Pass the identity of the player
   * who ran the command, never values from its arguments. Never rejects.
   */
  link(player: Messages.LinkPlayer, code: string): Promise<LinkResult> {
    if (this.#closed) return Promise.resolve(Messages.linkFailed("failed", "not_running"));
    return this.#track(
      (async () => {
        try {
          return Messages.linkReply(
            await this.#transport.call("/link", Messages.link(code, player), CALL_TIMEOUT_MS),
          );
        } catch (e) {
          if (e instanceof ApiError) return Messages.linkError(e);
          this.#log.warn(`Couldn't reach Ticket Tool to link ${player.name}: ${rootMessage(e)}`);
          return Messages.linkFailed("failed", "network");
        }
      })(),
    );
  }

  async #runLoop(): Promise<void> {
    let needHello = true;
    while (this.#running) {
      try {
        if (needHello) {
          const reply = Messages.helloReply(
            await this.#transport.call(
              "/hello",
              Messages.hello(this.#plugin, this.#banProviders()),
              CALL_TIMEOUT_MS,
              this.#stop.signal,
            ),
          );
          this.#hello = reply;
          needHello = false;
          this.#everConnected = true;
          this.#log.info(
            `Connected to Ticket Tool as "${reply.keyName}" for Discord server ` +
              `${reply.serverName ?? reply.serverId} (${this.key})`,
          );
        }
        const pollTimeoutMs = Math.max(35, (this.#hello?.pollWaitSeconds ?? 25) + 10) * 1000;
        this.#lastPollAt = new Date(this.#clock());
        this.#state = "connected";
        const body = await this.#transport.call("/poll", {}, pollTimeoutMs, this.#stop.signal);
        this.#recovered();
        for (const request of Messages.pollRequests(body)) {
          this.#dispatch(request);
        }
      } catch (e) {
        if (!this.#running || e instanceof AbortedError) break;
        if (e instanceof ApiError) {
          needHello = (await this.#handleApiError(e)) || needHello;
        } else if (e instanceof HttpTimeoutError) {
          await this.#failed("Ticket Tool didn't answer in time", e, this.#backoff.next());
        } else {
          await this.#failed("Can't reach Ticket Tool: " + rootMessage(e), e, this.#backoff.next());
        }
      }
    }
  }

  /** Returns true when the next attempt should start over with `/hello`. */
  async #handleApiError(e: ApiError): Promise<boolean> {
    const error = e.error ?? `http_${e.status}`;
    switch (error) {
      case "invalid_key":
        this.#fatal(
          "stopped_invalid_key",
          `Ticket Tool doesn't recognize key ${this.key}. It was revoked, rotated or mistyped: ` +
            "paste the current key from the dashboard and reload.",
        );
        return false;
      case "wrong_plugin_type":
        this.#fatal(
          "stopped_error",
          `Key ${this.key} was made for a different game (${String(e.body.expected ?? "?")}). ` +
            `Create a ${this.#plugin.type} key on the dashboard.`,
        );
        return false;
      case "unsupported_protocol":
        this.#fatal("stopped_error", `Ticket Tool no longer serves protocol ${PROTOCOL_VERSION}. Update the plugin.`);
        return false;
      case "plan_not_entitled":
        this.#state = "paused_plan";
        this.#note("The Discord server's plan doesn't include game lookups. Checking again in an hour.", null);
        await this.#pause(PLAN_RETRY_MS);
        return true;
      case "rate_limited":
        await this.#failed(
          "Ticket Tool is rate limiting this key; waiting a minute",
          null,
          RATE_LIMIT_WAIT_MS + Math.floor(this.#random() * 5000),
        );
        return false;
      case "too_many_polls": {
        const wait = this.#backoff.next();
        await this.#failed(
          `Three polls are already open for key ${this.key}. ` +
            "Is the same key in use on another server? Each server needs its own key.",
          null,
          wait < TOO_MANY_POLLS_MIN_WAIT_MS ? TOO_MANY_POLLS_MIN_WAIT_MS + Math.floor(this.#random() * 5000) : wait,
        );
        return false;
      }
      case "bad_signature":
      case "missing_headers":
      case "bad_nonce":
        await this.#failed(
          `Ticket Tool rejected the request signature (${error}). Check the key was pasted whole.`,
          null,
          this.#backoff.next(),
        );
        return false;
      default:
        await this.#failed(`Ticket Tool returned ${error} (HTTP ${e.status})`, null, this.#backoff.next());
        return false;
    }
  }

  #dispatch(request: LookupRequest): void {
    if (request.type !== "player.lookup") {
      this.#log.debug("Ignoring request type " + request.type);
      return;
    }
    const received = this.#serverNow();
    if (request.expiresAt.getTime() <= received) {
      this.#log.debug("Dropping expired request " + request.id);
      return;
    }
    void this.#track(this.#answer(request, received));
  }

  async #answer(request: LookupRequest, received: number): Promise<void> {
    if (request.include.length === 0) {
      await this.#respond(request, LookupResult.failure("unsupported"));
      return;
    }
    const deadline = Math.min(received + this.#answerBudgetMs, request.expiresAt.getTime());
    const remaining = Math.max(1, deadline - received);

    let timer: NodeJS.Timeout | undefined;
    const timeout = new Promise<"timeout">((resolve) => {
      timer = setTimeout(() => resolve("timeout"), remaining);
    });
    let out: LookupResult;
    try {
      const result = await Promise.race([Promise.resolve().then(() => this.#handler(request)), timeout]);
      if (result === "timeout") {
        out = LookupResult.failure("error", "The game server took too long to answer");
      } else if (result == null) {
        out = LookupResult.failure("error", "The plugin had no answer");
      } else {
        out = result;
      }
    } catch (e) {
      this.#log.warn(`Lookup ${request.id} failed`, e);
      out = LookupResult.failure("error", "The plugin hit an error answering this lookup");
    } finally {
      clearTimeout(timer);
    }
    if (this.#serverNow() >= request.expiresAt.getTime()) {
      this.#log.debug(`Answer for ${request.id} is too late; not sending it`);
      return;
    }
    await this.#respond(request, out);
  }

  async #respond(request: LookupRequest, result: LookupResult): Promise<void> {
    try {
      await this.#transport.call("/respond", Messages.respond(request, result), CALL_TIMEOUT_MS);
      this.#answered++;
    } catch (e) {
      if (e instanceof ApiError && e.error === "unknown_request") {
        this.#log.debug(`Request ${request.id} expired before the answer arrived`);
      } else if (e instanceof ApiError) {
        this.#log.warn(`Ticket Tool refused the answer to ${request.id}: ${e.message}`);
      } else {
        this.#log.warn(`Couldn't send the answer to ${request.id}: ${rootMessage(e)}`);
      }
    }
  }

  #track<T>(p: Promise<T>): Promise<T> {
    this.#inFlight.add(p);
    const remove = () => {
      this.#inFlight.delete(p);
    };
    p.then(remove, remove);
    return p;
  }

  #serverNow(): number {
    return this.#clock() + this.#transport.offsetSeconds * 1000;
  }

  #recovered(): void {
    if (this.#failureStreak > 0) this.#log.info("Reconnected to Ticket Tool");
    this.#failureStreak = 0;
    this.#backoff.reset();
  }

  /** Logs the first failure of a streak as a warning and the rest at debug, then waits. */
  async #failed(message: string, error: unknown, waitMs: number): Promise<void> {
    this.#state = "reconnecting";
    this.#note(message, error);
    this.#log.debug(`Retrying in ${waitMs} ms`);
    await this.#pause(waitMs);
  }

  #note(message: string, error: unknown): void {
    this.#lastError = message;
    this.#lastErrorAt = new Date(this.#clock());
    if (this.#failureStreak++ === 0) {
      this.#log.warn(message, error ?? undefined);
    } else {
      this.#log.debug(message);
    }
  }

  #fatal(state: ClientState, message: string): void {
    this.#lastError = message;
    this.#lastErrorAt = new Date(this.#clock());
    this.#state = state;
    this.#running = false;
    this.#log.warn(message);
  }

  #pause(ms: number): Promise<void> {
    return this.#sleep(ms, this.#stop.signal);
  }
}
