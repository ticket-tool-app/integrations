import { Natives, isStarted } from "../natives";
import { BanHit, BanSource, fromUnixSeconds, lowerIds } from "./types";

// EasyAdmin stores permanent bans with this expiry (year 2300).
const EASYADMIN_PERMANENT = 10444633200;

/** EasyAdmin's `banlist.json`, read through the resource file API so it works wherever EasyAdmin is installed. */
export class EasyAdminBans implements BanSource {
  readonly provider = "server" as const;
  readonly name = "EasyAdmin";

  constructor(
    private readonly natives: Natives,
    private readonly now: () => number = Date.now,
    private readonly resource = "EasyAdmin",
  ) {}

  available(): boolean {
    return isStarted(this.natives, this.resource);
  }

  async lookup(ids: readonly string[]): Promise<BanHit[]> {
    const raw = this.natives.LoadResourceFile(this.resource, "banlist.json");
    if (!raw) return [];
    const list = JSON.parse(raw) as unknown;
    if (!Array.isArray(list)) return [];
    const wanted = new Set(ids.map((i) => i.toLowerCase()));
    const nowSec = this.now() / 1000;
    const hits: BanHit[] = [];
    for (const item of list) {
      const b = item as Record<string, unknown>;
      if (!b || !lowerIds(b.identifiers).some((i) => wanted.has(i))) continue;
      const expire = typeof b.expire === "number" ? b.expire : EASYADMIN_PERMANENT;
      if (expire < EASYADMIN_PERMANENT && expire <= nowSec) continue;
      hits.push({
        provider: "server",
        source: "EasyAdmin",
        reason: typeof b.reason === "string" ? b.reason : null,
        createdAt: fromUnixSeconds(b.time),
        expiresAt: expire >= EASYADMIN_PERMANENT ? null : fromUnixSeconds(expire),
        actor: typeof b.banner === "string" ? b.banner : null,
      });
    }
    return hits;
  }
}
