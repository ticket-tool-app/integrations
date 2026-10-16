import type { ProfileField } from "@ticket-tool/plugin-sdk";
import { DISCORD_ID, OnlinePlayer } from "./identity";
import { Natives } from "./natives";

/**
 * Last-seen records for players who aren't online, kept in the resource's own
 * KVP store so they survive restarts without a database. Keyed by Discord id
 * because that is what Ticket Tool asks about.
 */
export interface SeenRecord {
  name: string;
  steamId: string | null;
  license: string | null;
  /** Identifiers in `kind:value` form for ban matching while the player is offline. */
  ids: string[];
  firstSeen: string;
  lastSeen: string;
  /** Character and job as last read from the framework. */
  fields?: ProfileField[];
}

const PREFIX = "seen:";

export class SeenStore {
  constructor(
    private readonly natives: Natives,
    private readonly now: () => number = Date.now,
  ) {}

  get(discordId: string): SeenRecord | null {
    if (!DISCORD_ID.test(discordId)) return null;
    let raw: string | null;
    try {
      raw = this.natives.GetResourceKvpString(PREFIX + discordId);
    } catch {
      return null;
    }
    if (!raw) return null;
    try {
      const parsed = JSON.parse(raw) as Partial<SeenRecord>;
      if (typeof parsed.name !== "string" || typeof parsed.lastSeen !== "string") return null;
      return {
        name: parsed.name,
        steamId: typeof parsed.steamId === "string" ? parsed.steamId : null,
        license: typeof parsed.license === "string" ? parsed.license : null,
        ids: Array.isArray(parsed.ids) ? parsed.ids.filter((i): i is string => typeof i === "string") : [],
        firstSeen: typeof parsed.firstSeen === "string" ? parsed.firstSeen : parsed.lastSeen,
        lastSeen: parsed.lastSeen,
        fields: Array.isArray(parsed.fields) ? parsed.fields : undefined,
      };
    } catch {
      return null;
    }
  }

  /** Records that `player` is here now. Players without a Discord identifier can't be asked about, so they're skipped. */
  touch(player: OnlinePlayer, fields?: ProfileField[]): void {
    const discordId = player.identity.discordId;
    if (!discordId) return;
    const existing = this.get(discordId);
    const nowIso = new Date(this.now()).toISOString();
    const record: SeenRecord = {
      name: player.name || existing?.name || "Unknown",
      steamId: player.identity.steamId ?? existing?.steamId ?? null,
      license: player.identity.license ?? existing?.license ?? null,
      ids: player.identity.ids.length > 0 ? player.identity.ids : existing?.ids ?? [],
      firstSeen: existing?.firstSeen ?? nowIso,
      lastSeen: nowIso,
    };
    const keptFields = fields && fields.length > 0 ? fields : existing?.fields;
    if (keptFields) record.fields = keptFields;
    this.natives.SetResourceKvp(PREFIX + discordId, JSON.stringify(record));
  }
}
