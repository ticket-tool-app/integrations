import { Natives, isStarted } from "../natives";
import { BanHit, BanSource, fromUnixSeconds } from "./types";

// qb-adminmenu writes permanent bans with the largest 32-bit timestamp.
const QB_PERMANENT = 2147483647;

/** The `bans` table QBCore and Qbox ship, queried through oxmysql. */
export class QbBans implements BanSource {
  readonly provider = "server" as const;
  readonly name = "QBCore bans table";

  constructor(
    private readonly natives: Natives,
    private readonly now: () => number = Date.now,
  ) {}

  available(): boolean {
    return (isStarted(this.natives, "qb-core") || isStarted(this.natives, "qbx_core")) && isStarted(this.natives, "oxmysql");
  }

  async lookup(ids: readonly string[]): Promise<BanHit[]> {
    const discord = ids.filter((i) => i.startsWith("discord:"));
    const license = ids.filter((i) => i.startsWith("license:") || i.startsWith("license2:"));
    if (discord.length === 0 && license.length === 0) return [];
    const where: string[] = [];
    const params: string[] = [];
    if (discord.length) {
      where.push(`discord IN (${discord.map(() => "?").join(",")})`);
      params.push(...discord);
    }
    if (license.length) {
      where.push(`license IN (${license.map(() => "?").join(",")})`);
      params.push(...license);
    }
    const rows = await new Promise<unknown>((resolve, reject) => {
      try {
        this.natives.callExport(
          "oxmysql",
          "query",
          `SELECT reason, expire, bannedby FROM bans WHERE ${where.join(" OR ")}`,
          params,
          (result: unknown) => resolve(result),
        );
      } catch (e) {
        reject(e);
      }
    });
    if (!Array.isArray(rows)) return [];
    const nowSec = this.now() / 1000;
    const hits: BanHit[] = [];
    for (const row of rows) {
      const r = row as Record<string, unknown>;
      const expire = typeof r.expire === "number" ? r.expire : Number(r.expire);
      if (!Number.isFinite(expire) || (expire < QB_PERMANENT && expire <= nowSec)) continue;
      hits.push({
        provider: "server",
        source: "QBCore",
        reason: typeof r.reason === "string" ? r.reason : null,
        createdAt: null,
        expiresAt: expire >= QB_PERMANENT ? null : fromUnixSeconds(expire),
        actor: typeof r.bannedby === "string" ? r.bannedby : null,
      });
    }
    return hits;
  }
}
