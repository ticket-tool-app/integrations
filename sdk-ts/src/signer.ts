import { createHash, createHmac, timingSafeEqual } from "node:crypto";

export const SIGNING_CONTEXT = "ticket-tool plugin signing v1";

const hmacHex = (key: string | Buffer, message: string | Buffer): string =>
  createHmac("sha256", key).update(message).digest("hex");

export function deriveSigningSecret(fullKey: string): string {
  return hmacHex(Buffer.from(fullKey, "utf8"), Buffer.from(SIGNING_CONTEXT, "utf8"));
}

export function sha256Hex(body: Uint8Array | string): string {
  return createHash("sha256").update(body).digest("hex");
}

export function canonical(
  method: string,
  endpoint: string,
  timestamp: number,
  nonce: string,
  body: Uint8Array | string,
): string {
  return ["v1", method, endpoint, String(timestamp), nonce, sha256Hex(body)].join("\n");
}

/** Request and reply signatures, protocol v1. */
export class Signer {
  // The HMAC key is the UTF-8 bytes of the hex secret, not the decoded bytes.
  readonly #secret: Buffer;

  constructor(signingSecretHex: string) {
    this.#secret = Buffer.from(signingSecretHex, "utf8");
  }

  signRequest(method: string, endpoint: string, timestamp: number, nonce: string, body: Uint8Array | string): string {
    return this.#sign(canonical(method, endpoint, timestamp, nonce, body));
  }

  signReply(endpoint: string, timestamp: number, nonce: string, body: Uint8Array | string): string {
    return this.#sign(canonical("RESPONSE", endpoint, timestamp, nonce, body));
  }

  /** Constant-time, so a forger can't learn the signature a byte at a time. */
  verifyReply(
    endpoint: string,
    timestamp: number,
    nonce: string,
    body: Uint8Array | string,
    signature: string | null | undefined,
  ): boolean {
    if (signature == null) return false;
    const want = Buffer.from(this.signReply(endpoint, timestamp, nonce, body), "utf8");
    const got = Buffer.from(signature.trim(), "utf8");
    return want.length === got.length && timingSafeEqual(want, got);
  }

  sameSecret(other: Signer): boolean {
    const a = this.#secret;
    const b = other.#secret;
    return a.length === b.length && timingSafeEqual(a, b);
  }

  #sign(canonicalString: string): string {
    return "v1=" + hmacHex(this.#secret, Buffer.from(canonicalString, "utf8"));
  }

  toString(): string {
    return "Signer[redacted]";
  }

  toJSON(): string {
    return "Signer[redacted]";
  }
}
