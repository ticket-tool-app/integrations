import { randomBytes } from "node:crypto";
import { ApiError, ReplyVerificationError } from "./errors";
import { AbortSignalLike, HttpSender } from "./http";
import { PluginKey } from "./key";
import { Json } from "./messages";
import { PROTOCOL_VERSION } from "./types";

export const SIGNATURE_WINDOW_SECONDS = 300;
export const API_PATH = "/api/plugin/v1";
export const DEFAULT_API_URL = "https://dash.ticket-tool.app";

export interface Reply {
  status: number;
  body: Json;
  signed: boolean;
}

/** The URL every endpoint hangs off, from a host root or an already complete base. */
export function apiBase(apiUrl: string | null | undefined): URL {
  let url = apiUrl == null || apiUrl.trim() === "" ? DEFAULT_API_URL : apiUrl.trim();
  while (url.endsWith("/")) url = url.slice(0, -1);
  if (!url.endsWith(API_PATH)) url += API_PATH;
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    throw new Error(`api-url isn't a valid URL: ${apiUrl}`);
  }
  const host = parsed.hostname;
  const loopback = host === "localhost" || host.startsWith("127.") || host === "[::1]" || host === "::1";
  if (!(parsed.protocol === "https:" || (parsed.protocol === "http:" && loopback))) {
    throw new Error("api-url must use https:// (plain http is only allowed for localhost)");
  }
  return parsed;
}

export function newNonce(): string {
  return randomBytes(16).toString("base64url");
}

/** One signed request and its verified reply. Knows nothing about the poll loop. */
export class Transport {
  /** serverTime - localTime, learned from a stale_timestamp error. */
  #offsetSeconds = 0;

  constructor(
    private readonly http: HttpSender,
    private readonly base: URL,
    private readonly key: PluginKey,
    private readonly userAgent: string,
    private readonly clock: () => number,
  ) {}

  get offsetSeconds(): number {
    return this.#offsetSeconds;
  }

  nowSeconds(): number {
    return Math.floor(this.clock() / 1000) + this.#offsetSeconds;
  }

  correctClock(serverTime: number): void {
    this.#offsetSeconds = Math.floor(serverTime) - Math.floor(this.clock() / 1000);
  }

  /**
   * Sends `payload` to `endpoint` (e.g. `/poll`). The body is serialized once
   * and those exact bytes are both hashed and sent.
   */
  async send(endpoint: string, payload: Json | null, timeoutMs: number, signal?: AbortSignalLike): Promise<Reply> {
    const body = Buffer.from(JSON.stringify(payload ?? {}), "utf8");
    const ts = this.nowSeconds();
    const nonce = newNonce();
    const url = new URL(this.base.toString().replace(/\/+$/, "") + endpoint);
    const reply = await this.http(url, {
      body,
      timeoutMs,
      signal,
      headers: {
        "Content-Type": "application/json",
        Accept: "application/json",
        "X-TT-Key-Id": this.key.keyId,
        "X-TT-Timestamp": String(ts),
        "X-TT-Nonce": nonce,
        "X-TT-Signature": this.key.signer.signRequest("POST", endpoint, ts, nonce, body),
        "X-TT-Protocol": String(PROTOCOL_VERSION),
        "X-TT-Plugin": this.userAgent,
        "User-Agent": this.userAgent,
      },
    });
    const ok = reply.status >= 200 && reply.status < 300;
    const signature = reply.headers["x-tt-signature"];
    const signed = signature != null;
    if (signed) {
      this.verify(endpoint, nonce, reply.body, signature, reply.headers);
    } else if (ok) {
      // Only pre-auth errors may be unsigned; an unsigned success could be anyone's.
      throw new ReplyVerificationError(`Unsigned success reply from ${endpoint}`);
    }
    let parsed: Json = {};
    if (reply.body.length > 0) {
      try {
        const value: unknown = JSON.parse(reply.body.toString("utf8"));
        if (value != null && typeof value === "object" && !Array.isArray(value)) parsed = value as Json;
        else if (ok) throw new Error("not an object");
      } catch {
        if (ok) throw new Error(`Reply from ${endpoint} wasn't JSON`);
        parsed = {};
      }
    }
    return { status: reply.status, body: parsed, signed };
  }

  /**
   * `send` plus error mapping: returns the body of an `ok` reply, retries once
   * after correcting the clock from a stale_timestamp error, and throws
   * `ApiError` for anything else.
   */
  async call(endpoint: string, payload: Json | null, timeoutMs: number, signal?: AbortSignalLike): Promise<Json> {
    for (let attempt = 0; ; attempt++) {
      const reply = await this.send(endpoint, payload, timeoutMs, signal);
      if (reply.status >= 200 && reply.status < 300 && reply.body.ok !== false) {
        return reply.body;
      }
      const error = typeof reply.body.error === "string" ? reply.body.error : null;
      const serverTime = reply.body.serverTime;
      if (attempt === 0 && error === "stale_timestamp" && typeof serverTime === "number") {
        this.correctClock(serverTime);
        continue;
      }
      throw new ApiError(reply.status, error, reply.body);
    }
  }

  private verify(
    endpoint: string,
    nonce: string,
    raw: Buffer,
    signature: string,
    headers: Record<string, string | undefined>,
  ): void {
    if (headers["x-tt-nonce"] !== nonce) {
      throw new ReplyVerificationError("Reply nonce doesn't match the request");
    }
    const tsText = (headers["x-tt-timestamp"] ?? "").trim();
    if (!/^\d+$/.test(tsText)) {
      throw new ReplyVerificationError("Reply timestamp missing or malformed");
    }
    const replyTs = Number(tsText);
    if (Math.abs(this.nowSeconds() - replyTs) > SIGNATURE_WINDOW_SECONDS) {
      throw new ReplyVerificationError("Reply timestamp outside the signature window");
    }
    if (!this.key.signer.verifyReply(endpoint, replyTs, nonce, raw, signature)) {
      throw new ReplyVerificationError("Reply signature doesn't match");
    }
  }
}
