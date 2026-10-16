import { createHash, createHmac, randomBytes, timingSafeEqual } from "node:crypto";

const KEY_PATTERN = /^ttp_([0-9a-f]{16})\.[A-Za-z0-9_-]{43}$/;
const SECRET_LABEL = "ticket-tool plugin signing v1";

const sha256Hex = (body: Uint8Array) => createHash("sha256").update(body).digest("hex");
const hmacHex = (key: string, message: string) => createHmac("sha256", key).update(message).digest("hex");

/** The key id and derived signing secret. The full key is not kept. */
export interface ParsedKey {
  keyId: string;
  secret: string;
}

export function parseKey(fullKey: string): ParsedKey {
  const match = KEY_PATTERN.exec(fullKey.trim());
  if (!match) {
    throw new Error("That isn't a Ticket Tool plugin key. Copy the whole ttp_... key from the dashboard.");
  }
  return { keyId: match[1]!, secret: signingSecret(fullKey.trim()) };
}

/** hex(HMAC-SHA256(key = full key, "ticket-tool plugin signing v1")); its hex string is the HMAC key. */
export function signingSecret(fullKey: string): string {
  return hmacHex(fullKey, SECRET_LABEL);
}

export function bodyHash(body: Uint8Array): string {
  return sha256Hex(body);
}

function canonical(method: string, endpoint: string, ts: number | string, nonce: string, body: Uint8Array) {
  return ["v1", method, endpoint, String(ts), nonce, sha256Hex(body)].join("\n");
}

export function signRequest(secret: string, method: string, endpoint: string, ts: number, nonce: string, body: Uint8Array) {
  return `v1=${hmacHex(secret, canonical(method, endpoint, ts, nonce, body))}`;
}

export function signReply(secret: string, endpoint: string, ts: number | string, nonce: string, body: Uint8Array) {
  return `v1=${hmacHex(secret, canonical("RESPONSE", endpoint, ts, nonce, body))}`;
}

export function verifyReply(secret: string, endpoint: string, ts: string, nonce: string, body: Uint8Array, signature: string) {
  const want = Buffer.from(signReply(secret, endpoint, ts, nonce, body));
  const got = Buffer.from(signature);
  return want.length === got.length && timingSafeEqual(want, got);
}

/** 16 random bytes, base64url without padding: 22 characters. */
export function newNonce(): string {
  return randomBytes(16).toString("base64url");
}
