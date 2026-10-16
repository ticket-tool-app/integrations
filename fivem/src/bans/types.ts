/** One active ban as a source reports it, before the sources are merged. */
export interface BanHit {
  provider: "txadmin" | "server";
  /** Which list it came from, for logs and the status command. */
  source: string;
  reason: string | null;
  createdAt: Date | null;
  /** Null means permanent. */
  expiresAt: Date | null;
  actor: string | null;
}

export interface BanSource {
  readonly provider: "txadmin" | "server";
  readonly name: string;
  available(): boolean;
  /** Active bans matching any of `ids` (`kind:value`, lower-case). */
  lookup(ids: readonly string[]): Promise<BanHit[]>;
}

export const fromUnixSeconds = (v: unknown): Date | null =>
  typeof v === "number" && Number.isFinite(v) && v > 0 ? new Date(v * 1000) : null;

export const lowerIds = (ids: unknown): string[] =>
  Array.isArray(ids) ? ids.filter((i): i is string => typeof i === "string").map((i) => i.toLowerCase()) : [];
