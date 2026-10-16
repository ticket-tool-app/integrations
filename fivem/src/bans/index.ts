import { BanInfo, stripCfxColors } from "@ticket-tool/plugin-sdk";
import { BanHit, BanSource } from "./types";

export type BanProviderSetting = "auto" | "txadmin" | "server" | "none";

/** Chooses the sources a lookup consults, per the `ticket_tool_ban_provider` setting. */
export function activeSources(sources: readonly BanSource[], setting: BanProviderSetting): BanSource[] {
  if (setting === "none") return [];
  return sources.filter((s) => s.available() && (setting === "auto" || s.provider === setting));
}

/** The provider names for `/hello`, without duplicates. */
export function providerNames(sources: readonly BanSource[]): string[] {
  return [...new Set(sources.map((s) => s.provider))];
}

/**
 * Asks every active source and merges the answers into one protocol `bans`
 * part: banned when any source has an active ban, showing the newest one.
 * Returns null when no source is active, which the caller turns into
 * `no_ban_provider`. A source that fails is skipped unless all of them fail.
 */
export async function lookupBans(
  sources: readonly BanSource[],
  ids: readonly string[],
  onError: (source: BanSource, error: unknown) => void = () => {},
): Promise<BanInfo | null> {
  if (sources.length === 0) return null;
  const results = await Promise.allSettled(sources.map((s) => s.lookup(ids)));
  const hits: BanHit[] = [];
  let failures = 0;
  let lastError: unknown;
  results.forEach((r, i) => {
    if (r.status === "fulfilled") {
      hits.push(...r.value);
    } else {
      failures++;
      lastError = r.reason;
      onError(sources[i], r.reason);
    }
  });
  if (failures === sources.length) throw lastError instanceof Error ? lastError : new Error(String(lastError));
  return mergeHits(hits, sources[0].provider);
}

export function mergeHits(hits: readonly BanHit[], defaultProvider: string): BanInfo {
  if (hits.length === 0) return { banned: false, provider: defaultProvider };
  const sorted = [...hits].sort((a, b) => (b.createdAt?.getTime() ?? 0) - (a.createdAt?.getTime() ?? 0));
  const top = sorted[0];
  return {
    banned: true,
    provider: top.provider,
    reason: stripCfxColors(top.reason),
    createdAt: top.createdAt,
    expiresAt: top.expiresAt,
    actor: stripCfxColors(top.actor),
    activeCount: hits.length > 1 ? hits.length : null,
  };
}
