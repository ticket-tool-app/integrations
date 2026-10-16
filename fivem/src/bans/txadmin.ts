import { promises as fsp } from "node:fs";
import * as path from "node:path";
import { Natives, isStarted } from "../natives";
import { BanHit, BanSource, fromUnixSeconds, lowerIds } from "./types";

/**
 * txAdmin bans. txAdmin has no server-side API for querying bans, so this
 * reads its players database (`<txData>/<profile>/data/playersDB.json`)
 * read-only, and layers txAdmin's own ban and revoke events on top because
 * txAdmin only flushes that file every 15 to 60 seconds.
 */
interface TxAction {
  id: string;
  type: string;
  ids: string[];
  reason: string | null;
  author: string | null;
  timestamp: number | null;
  expiration: number | false;
  revoked: boolean;
}

export interface TxAdminOptions {
  /** An explicit `playersDB.json`, or a txData / profile folder. */
  overridePath?: string | null;
  env?: NodeJS.ProcessEnv;
  platform?: NodeJS.Platform;
  now?: () => number;
  readFile?: (file: string) => Promise<string>;
  stat?: (file: string) => Promise<{ mtimeMs: number; size: number }>;
}

/** Where txAdmin keeps its database, in the order txAdmin itself resolves it. */
export function txAdminDbCandidates(n: Natives, options: TxAdminOptions = {}): string[] {
  const env = options.env ?? process.env;
  const windows = (options.platform ?? process.platform) === "win32";
  const profile = (safeConvar(n, "serverProfile") || "default").replace(/[^a-z0-9._-]/gi, "") || "default";
  const out: string[] = [];
  const fromDataPath = (dataPath: string) => path.join(dataPath, profile, "data", "playersDB.json");

  const override = options.overridePath?.trim();
  if (override) {
    if (override.toLowerCase().endsWith(".json")) {
      out.push(override);
    } else {
      out.push(path.join(override, "data", "playersDB.json"));
      out.push(fromDataPath(override));
    }
  }
  const envPath = env.TXHOST_DATA_PATH?.trim();
  if (envPath) out.push(fromDataPath(envPath));
  const convarPath = safeConvar(n, "txDataPath");
  if (convarPath) out.push(fromDataPath(path.resolve(convarPath)));
  const citizenRoot = safeConvar(n, "citizen_root");
  if (citizenRoot) out.push(fromDataPath(path.join(citizenRoot, windows ? ".." : "../../../", "txData")));
  return [...new Set(out.map((p) => path.normalize(p)))];
}

function safeConvar(n: Natives, name: string): string {
  try {
    return n.GetConvar(name, "").trim();
  } catch {
    return "";
  }
}

export class TxAdminBans implements BanSource {
  readonly provider = "txadmin" as const;
  readonly name = "txAdmin";

  #file: string | null = null;
  #resolvedAt = 0;
  #cacheKey = "";
  #fileActions: TxAction[] = [];
  #index = new Map<string, TxAction[]>();
  #liveBans = new Map<string, TxAction>();
  #liveRevoked = new Set<string>();
  readonly #now: () => number;
  readonly #readFile: (file: string) => Promise<string>;
  readonly #stat: (file: string) => Promise<{ mtimeMs: number; size: number }>;

  constructor(
    private readonly natives: Natives,
    private readonly options: TxAdminOptions = {},
  ) {
    this.#now = options.now ?? Date.now;
    this.#readFile = options.readFile ?? ((f) => fsp.readFile(f, "utf8"));
    this.#stat = options.stat ?? ((f) => fsp.stat(f));
  }

  /** The database file in use, once found. */
  get file(): string | null {
    return this.#file;
  }

  available(): boolean {
    return isStarted(this.natives, "monitor") || this.#file != null;
  }

  /** Hooks txAdmin's events so bans made in the last minute aren't missed. */
  listen(): void {
    this.natives.on("txAdmin:events:playerBanned", (data: Record<string, unknown>) => {
      if (!data || typeof data.actionId !== "string") return;
      this.#liveBans.set(data.actionId, {
        id: data.actionId,
        type: "ban",
        ids: lowerIds(data.targetIds),
        reason: typeof data.reason === "string" ? data.reason : null,
        author: typeof data.author === "string" ? data.author : null,
        timestamp: Math.floor(this.#now() / 1000),
        expiration: typeof data.expiration === "number" ? data.expiration : false,
        revoked: false,
      });
    });
    this.natives.on("txAdmin:events:actionRevoked", (data: Record<string, unknown>) => {
      if (data && typeof data.actionId === "string") this.#liveRevoked.add(data.actionId);
    });
  }

  async lookup(ids: readonly string[]): Promise<BanHit[]> {
    await this.#refresh();
    const wanted = new Set(ids.map((i) => i.toLowerCase()));
    const nowSec = this.#now() / 1000;
    const seen = new Set<string>();
    const hits: BanHit[] = [];
    const consider = (a: TxAction) => {
      if (seen.has(a.id)) return;
      seen.add(a.id);
      if (a.type !== "ban" || a.revoked || this.#liveRevoked.has(a.id)) return;
      if (a.expiration !== false && a.expiration <= nowSec) return;
      if (!a.ids.some((i) => wanted.has(i))) return;
      hits.push({
        provider: "txadmin",
        source: "txAdmin",
        reason: a.reason,
        createdAt: fromUnixSeconds(a.timestamp),
        expiresAt: a.expiration === false ? null : fromUnixSeconds(a.expiration),
        actor: a.author,
      });
    };
    for (const id of wanted) for (const a of this.#index.get(id) ?? []) consider(a);
    for (const a of this.#liveBans.values()) consider(a);
    return hits;
  }

  async #refresh(): Promise<void> {
    const file = await this.#resolve();
    if (!file) {
      if (this.#liveBans.size === 0) {
        throw new Error("txAdmin is running but its playersDB.json wasn't found; set ticket_tool_txadmin_db");
      }
      return;
    }
    const st = await this.#stat(file);
    const key = `${file}|${st.mtimeMs}|${st.size}`;
    if (key === this.#cacheKey) return;
    const data = JSON.parse(await this.#readFile(file)) as { actions?: unknown };
    const actions: TxAction[] = [];
    for (const raw of Array.isArray(data.actions) ? data.actions : []) {
      const a = raw as Record<string, any>;
      if (!a || a.type !== "ban" || typeof a.id !== "string") continue;
      actions.push({
        id: a.id,
        type: "ban",
        ids: lowerIds(a.ids),
        reason: typeof a.reason === "string" ? a.reason : null,
        author: typeof a.author === "string" ? a.author : null,
        timestamp: typeof a.timestamp === "number" ? a.timestamp : null,
        expiration: typeof a.expiration === "number" ? a.expiration : false,
        revoked: a.revocation != null && typeof a.revocation.timestamp === "number",
      });
    }
    const index = new Map<string, TxAction[]>();
    for (const a of actions) {
      for (const id of a.ids) {
        const list = index.get(id);
        if (list) list.push(a);
        else index.set(id, [a]);
      }
    }
    this.#fileActions = actions;
    this.#index = index;
    this.#cacheKey = key;
    // Once the file has caught up, its copy (with revocations) is authoritative.
    for (const id of [...this.#liveBans.keys()]) {
      if (actions.some((a) => a.id === id)) this.#liveBans.delete(id);
    }
  }

  get actionCount(): number {
    return this.#fileActions.length;
  }

  async #resolve(): Promise<string | null> {
    if (this.#file && this.#now() - this.#resolvedAt < 60_000) return this.#file;
    this.#resolvedAt = this.#now();
    for (const candidate of txAdminDbCandidates(this.natives, this.options)) {
      try {
        await this.#stat(candidate);
        this.#file = candidate;
        return candidate;
      } catch {
        // try the next one
      }
    }
    this.#file = null;
    return null;
  }
}
