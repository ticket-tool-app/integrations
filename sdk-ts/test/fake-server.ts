import * as http from "node:http";
import { AddressInfo } from "node:net";
import { Signer, deriveSigningSecret } from "../src/signer";

export const KEY = "ttp_0123456789abcdef.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
export const KEY_ID = "0123456789abcdef";

type Json = Record<string, unknown>;

export interface Received {
  endpoint: string;
  body: Json;
  nonce: string;
  timestamp: number;
}

/** What a scripted handler returns; `signed: false` mimics pre-auth errors. */
export interface Canned {
  status: number;
  body: Json;
  signed: boolean;
}

export const ok = (body: Json): Canned => ({ status: 200, body, signed: true });
export const signedError = (status: number, error: string, extra: Json = {}): Canned => ({
  status,
  body: { ok: false, error, ...extra },
  signed: true,
});
export const unsigned = (status: number, error: string, extra: Json = {}): Canned => ({
  status,
  body: { ok: false, error, ...extra },
  signed: false,
});

/**
 * A protocol v1 server for tests: checks signatures, the clock window and
 * nonce reuse the way Ticket Tool does, signs its replies, and lets a test
 * script what each endpoint returns.
 */
export class FakeTicketTool {
  readonly received: Received[] = [];
  readonly nonces = new Set<string>();
  serverClockOffset = 0;
  pollWaitMs = 200;
  /** Breaks the reply signature, to check the client refuses it. */
  tamperReplies = false;
  pluginType = "fivem";

  #server: http.Server;
  #signer = new Signer(deriveSigningSecret(KEY));
  #pollQueue: Json[] = [];
  #pollWaiters: Array<() => void> = [];
  #overrides = new Map<string, (r: Received) => Canned | Promise<Canned>>();
  #listeners: Array<(r: Received) => void> = [];

  private constructor(server: http.Server) {
    this.#server = server;
  }

  static async start(): Promise<FakeTicketTool> {
    let fake: FakeTicketTool | undefined;
    const server = http.createServer((req, res) => void fake!.#handle(req, res));
    fake = new FakeTicketTool(server);
    await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
    return fake;
  }

  get url(): string {
    return `http://127.0.0.1:${(this.#server.address() as AddressInfo).port}`;
  }

  async close(): Promise<void> {
    for (const w of this.#pollWaiters.splice(0)) w();
    (this.#server as http.Server & { closeAllConnections?: () => void }).closeAllConnections?.();
    await new Promise<void>((resolve) => this.#server.close(() => resolve()));
  }

  now(): number {
    return Math.floor(Date.now() / 1000) + this.serverClockOffset;
  }

  override(endpoint: string, fn: (r: Received) => Canned | Promise<Canned>): void {
    this.#overrides.set(endpoint, fn);
  }

  clearOverride(endpoint: string): void {
    this.#overrides.delete(endpoint);
  }

  calls(endpoint: string): Received[] {
    return this.received.filter((r) => r.endpoint === endpoint);
  }

  queueLookup(request: Json): void {
    this.#pollQueue.push(request);
    for (const w of this.#pollWaiters.splice(0)) w();
  }

  /** Resolves with the next received call to `endpoint` matching `pred` (including ones already received). */
  waitFor(endpoint: string, pred: (r: Received) => boolean = () => true, timeoutMs = 5000): Promise<Received> {
    const existing = this.received.find((r) => r.endpoint === endpoint && pred(r));
    if (existing) return Promise.resolve(existing);
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error(`No ${endpoint} within ${timeoutMs} ms`)), timeoutMs);
      const listener = (r: Received) => {
        if (r.endpoint === endpoint && pred(r)) {
          clearTimeout(timer);
          this.#listeners = this.#listeners.filter((l) => l !== listener);
          resolve(r);
        }
      };
      this.#listeners.push(listener);
    });
  }

  static lookup(id: string, player: Json, expiresInMs: number, include: string[]): Json {
    return {
      id,
      type: "player.lookup",
      expiresAt: new Date(Date.now() + expiresInMs).toISOString(),
      player: { uuid: null, steamId: null, discordId: null, name: null, ...player },
      include,
    };
  }

  async #handle(req: http.IncomingMessage, res: http.ServerResponse): Promise<void> {
    const chunks: Buffer[] = [];
    for await (const c of req) chunks.push(c as Buffer);
    const body = Buffer.concat(chunks);
    const path = (req.url ?? "").split("?")[0];
    const prefix = "/api/plugin/v1";
    if (!path.startsWith(prefix + "/")) {
      return this.#reply(res, path, undefined, unsigned(404, "not_found"));
    }
    const endpoint = path.slice(prefix.length);
    const h = (name: string) => {
      const v = req.headers[name];
      return Array.isArray(v) ? v[0] : v;
    };
    const keyId = h("x-tt-key-id");
    const tsHeader = h("x-tt-timestamp");
    const nonce = h("x-tt-nonce");
    const sig = h("x-tt-signature");
    if (req.method !== "POST") return this.#reply(res, endpoint, nonce, unsigned(405, "method_not_allowed"));
    if (keyId !== KEY_ID) return this.#reply(res, endpoint, nonce, unsigned(401, "invalid_key"));
    if (!tsHeader || !nonce || !sig) return this.#reply(res, endpoint, nonce, unsigned(401, "missing_headers"));
    if (!/^[A-Za-z0-9_-]{16,64}$/.test(nonce)) return this.#reply(res, endpoint, nonce, unsigned(401, "bad_nonce"));
    if (h("x-tt-protocol") !== "1") {
      return this.#reply(res, endpoint, nonce, unsigned(400, "unsupported_protocol", { protocol: 1 }));
    }
    const ts = Number(tsHeader);
    if (!Number.isInteger(ts) || Math.abs(this.now() - ts) > 300) {
      return this.#reply(res, endpoint, nonce, unsigned(401, "stale_timestamp", { serverTime: this.now() }));
    }
    if (this.#signer.signRequest("POST", endpoint, ts, nonce, body) !== sig) {
      return this.#reply(res, endpoint, nonce, unsigned(401, "bad_signature"));
    }
    if (this.nonces.has(nonce)) return this.#reply(res, endpoint, nonce, unsigned(401, "replayed"));
    this.nonces.add(nonce);

    let parsed: Json;
    try {
      parsed = JSON.parse(body.toString("utf8")) as Json;
    } catch {
      return this.#reply(res, endpoint, nonce, signedError(400, "invalid_body"));
    }
    const r: Received = { endpoint, body: parsed, nonce, timestamp: ts };
    this.received.push(r);
    for (const l of [...this.#listeners]) l(r);

    const override = this.#overrides.get(endpoint);
    if (override) return this.#reply(res, endpoint, nonce, await override(r));

    switch (endpoint) {
      case "/hello": {
        const type = (parsed.plugin as Json | undefined)?.type;
        if (type !== this.pluginType) {
          return this.#reply(res, endpoint, nonce, signedError(400, "wrong_plugin_type", { expected: this.pluginType }));
        }
        return this.#reply(
          res,
          endpoint,
          nonce,
          ok({
            ok: true,
            protocol: 1,
            pluginType: this.pluginType,
            key: { name: "Roleplay 1" },
            server: { id: "123456789012345678", name: "My Discord server" },
            poll: { waitSeconds: 25 },
          }),
        );
      }
      case "/poll": {
        if (this.#pollQueue.length === 0) {
          await new Promise<void>((resolve) => {
            const timer = setTimeout(resolve, this.pollWaitMs);
            this.#pollWaiters.push(() => {
              clearTimeout(timer);
              resolve();
            });
          });
        }
        const requests = this.#pollQueue.splice(0, 20);
        return this.#reply(res, endpoint, nonce, ok({ ok: true, requests }));
      }
      case "/respond":
      case "/goodbye":
        return this.#reply(res, endpoint, nonce, ok({ ok: true }));
      case "/link":
        return this.#reply(
          res,
          endpoint,
          nonce,
          ok({ ok: true, result: "linked", discord: { username: "alex" }, player: { name: "Notch" } }),
        );
      default:
        return this.#reply(res, endpoint, nonce, unsigned(404, "not_found"));
    }
  }

  #reply(res: http.ServerResponse, endpoint: string, nonce: string | undefined, canned: Canned): void {
    if (res.destroyed) return;
    const out = Buffer.from(JSON.stringify(canned.body), "utf8");
    const headers: Record<string, string> = { "Content-Type": "application/json" };
    if (canned.signed && nonce) {
      const ts = this.now();
      let signature = this.#signer.signReply(endpoint, ts, nonce, out);
      if (this.tamperReplies) signature = signature.slice(0, -1) + (signature.endsWith("0") ? "1" : "0");
      headers["X-TT-Timestamp"] = String(ts);
      headers["X-TT-Nonce"] = nonce;
      headers["X-TT-Signature"] = signature;
    }
    res.writeHead(canned.status, headers);
    res.end(out);
  }
}
