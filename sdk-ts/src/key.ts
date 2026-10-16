import { inspect } from "node:util";
import { Signer, deriveSigningSecret } from "./signer";

const PATTERN = /^ttp_([0-9a-f]{16})\.([A-Za-z0-9_-]{43})$/;

/**
 * An integration key (`ttp_<16 hex>.<43 base64url>`). Only the derived signing
 * secret is kept, and every way of printing the object shows `ttp_<keyId>`, so
 * a key can't leak into a log through string conversion or `console.log`.
 */
export class PluginKey {
  readonly keyId: string;
  readonly #signer: Signer;

  private constructor(keyId: string, signer: Signer) {
    this.keyId = keyId;
    this.#signer = signer;
  }

  /** Throws when the text isn't a key; the message never repeats it. */
  static parse(key: string | null | undefined): PluginKey {
    if (key == null || key.trim() === "") {
      throw new Error("No integration key set");
    }
    const trimmed = key.trim();
    const m = PATTERN.exec(trimmed);
    if (!m) {
      throw new Error("That isn't an integration key. Keys look like ttp_0123456789abcdef.<43 characters>");
    }
    return new PluginKey(m[1], new Signer(deriveSigningSecret(trimmed)));
  }

  static looksLikeKey(key: string | null | undefined): boolean {
    return key != null && PATTERN.test(key.trim());
  }

  /** What may be shown or logged. */
  get displayPrefix(): string {
    return "ttp_" + this.keyId;
  }

  get signer(): Signer {
    return this.#signer;
  }

  equals(other: PluginKey): boolean {
    return other.keyId === this.keyId && other.#signer.sameSecret(this.#signer);
  }

  toString(): string {
    return this.displayPrefix;
  }

  toJSON(): string {
    return this.displayPrefix;
  }

  [inspect.custom](): string {
    return `PluginKey(${this.displayPrefix})`;
  }
}
