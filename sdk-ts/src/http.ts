import * as http from "node:http";
import * as https from "node:https";
import { AbortedError, HttpTimeoutError } from "./errors";

export interface HttpReply {
  status: number;
  /** Lower-case header names. */
  headers: Record<string, string | undefined>;
  body: Buffer;
}

export interface HttpRequestOptions {
  headers: Record<string, string>;
  body: Buffer;
  timeoutMs: number;
  signal?: AbortSignalLike;
}

/** The subset of AbortSignal used here, so Node 16's EventTarget quirks don't matter. */
export interface AbortSignalLike {
  readonly aborted: boolean;
  addEventListener(type: "abort", listener: () => void): void;
  removeEventListener(type: "abort", listener: () => void): void;
}

export type HttpSender = (url: URL, options: HttpRequestOptions) => Promise<HttpReply>;

/** Replies are small JSON; anything near this is not from Ticket Tool. */
export const MAX_REPLY_BYTES = 256 * 1024;

// node:https rather than fetch: FiveM's bundled Node 16 has no global fetch.
export const nodeHttpSender: HttpSender = (url, options) =>
  new Promise<HttpReply>((resolve, reject) => {
    if (options.signal?.aborted) {
      reject(new AbortedError());
      return;
    }
    const mod = url.protocol === "http:" ? http : https;
    let settled = false;
    const finish = (fn: () => void) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      options.signal?.removeEventListener("abort", onAbort);
      fn();
    };
    const req = mod.request(
      url,
      {
        method: "POST",
        headers: { ...options.headers, "Content-Length": String(options.body.length) },
      },
      (res) => {
        const chunks: Buffer[] = [];
        let size = 0;
        res.on("data", (chunk: Buffer) => {
          size += chunk.length;
          if (size > MAX_REPLY_BYTES) {
            req.destroy();
            finish(() => reject(new Error(`Reply larger than ${MAX_REPLY_BYTES} bytes`)));
            return;
          }
          chunks.push(chunk);
        });
        res.on("end", () => {
          const headers: Record<string, string | undefined> = {};
          for (const [k, v] of Object.entries(res.headers)) {
            headers[k.toLowerCase()] = Array.isArray(v) ? v[0] : v;
          }
          finish(() => resolve({ status: res.statusCode ?? 0, headers, body: Buffer.concat(chunks) }));
        });
        res.on("error", (e) => finish(() => reject(e)));
      },
    );
    // One deadline for the whole exchange: socket idle timeouts alone would let a
    // trickling reply hold a long-poll open forever.
    const timer = setTimeout(() => {
      req.destroy();
      finish(() => reject(new HttpTimeoutError()));
    }, options.timeoutMs);
    const onAbort = () => {
      req.destroy();
      finish(() => reject(new AbortedError()));
    };
    options.signal?.addEventListener("abort", onAbort);
    req.on("error", (e) => finish(() => reject(e)));
    req.end(options.body);
  });
