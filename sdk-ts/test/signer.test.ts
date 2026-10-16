import { readFileSync } from "node:fs";
import { join } from "node:path";
import { inspect } from "node:util";
import { describe, expect, it } from "vitest";
import { PluginKey, Signer, canonical, deriveSigningSecret, sha256Hex } from "../src";

interface Vectors {
  key: string;
  keyId: string;
  signingSecret: string;
  bodyHashes: { body: string; sha256: string }[];
  requests: { method: string; endpoint: string; body: string; timestamp: number; nonce: string; canonical: string; signature: string }[];
  replies: { endpoint: string; body: string; timestamp: number; nonce: string; signature: string }[];
}

const vectors = JSON.parse(
  readFileSync(join(__dirname, "../../spec/test-vectors/v1.json"), "utf8"),
) as Vectors;

describe("spec/test-vectors/v1.json", () => {
  it("derives the signing secret", () => {
    expect(deriveSigningSecret(vectors.key)).toBe(vectors.signingSecret);
  });

  it("hashes bodies", () => {
    for (const v of vectors.bodyHashes) expect(sha256Hex(Buffer.from(v.body, "utf8"))).toBe(v.sha256);
  });

  it("builds the canonical string and signs requests", () => {
    const signer = new Signer(vectors.signingSecret);
    for (const v of vectors.requests) {
      expect(canonical(v.method, v.endpoint, v.timestamp, v.nonce, Buffer.from(v.body))).toBe(v.canonical);
      expect(signer.signRequest(v.method, v.endpoint, v.timestamp, v.nonce, Buffer.from(v.body))).toBe(v.signature);
    }
  });

  it("signs and verifies replies", () => {
    const signer = new Signer(vectors.signingSecret);
    for (const v of vectors.replies) {
      expect(signer.signReply(v.endpoint, v.timestamp, v.nonce, Buffer.from(v.body))).toBe(v.signature);
      expect(signer.verifyReply(v.endpoint, v.timestamp, v.nonce, Buffer.from(v.body), v.signature)).toBe(true);
      expect(signer.verifyReply(v.endpoint, v.timestamp, v.nonce, Buffer.from(v.body + " "), v.signature)).toBe(false);
      expect(signer.verifyReply(v.endpoint, v.timestamp + 1, v.nonce, Buffer.from(v.body), v.signature)).toBe(false);
      expect(signer.verifyReply(v.endpoint, v.timestamp, v.nonce, Buffer.from(v.body), "v1=00")).toBe(false);
      expect(signer.verifyReply(v.endpoint, v.timestamp, v.nonce, Buffer.from(v.body), null)).toBe(false);
    }
  });
});

describe("PluginKey", () => {
  it("parses a key and keeps only its public part printable", () => {
    const key = PluginKey.parse(`  ${vectors.key}\n`);
    expect(key.keyId).toBe(vectors.keyId);
    const secretPart = vectors.key.split(".")[1];
    for (const shown of [String(key), JSON.stringify({ key }), inspect(key), `${key}`]) {
      expect(shown).toContain("ttp_0123456789abcdef");
      expect(shown).not.toContain(secretPart);
      expect(shown).not.toContain(vectors.signingSecret);
    }
    expect(inspect(key.signer)).not.toContain(vectors.signingSecret);
    expect(JSON.stringify(key.signer)).not.toContain(vectors.signingSecret);
  });

  it("rejects malformed keys without echoing them", () => {
    const bad = "ttp_0123456789abcdef.tooShort";
    expect(() => PluginKey.parse(bad)).toThrow(/isn't an integration key/);
    try {
      PluginKey.parse(bad);
    } catch (e) {
      expect((e as Error).message).not.toContain("tooShort");
    }
    expect(() => PluginKey.parse("")).toThrow(/No integration key/);
    expect(() => PluginKey.parse("ttp_0123456789ABCDEF.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")).toThrow();
    expect(PluginKey.looksLikeKey(vectors.key)).toBe(true);
    expect(PluginKey.looksLikeKey("nope")).toBe(false);
  });

  it("compares by secret", () => {
    const a = PluginKey.parse(vectors.key);
    const b = PluginKey.parse(vectors.key);
    const c = PluginKey.parse(vectors.key.slice(0, -1) + "B");
    expect(a.equals(b)).toBe(true);
    expect(a.equals(c)).toBe(false);
  });
});
