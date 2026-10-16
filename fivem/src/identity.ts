import { Natives } from "./natives";

/** The identifier kinds worth keeping. `ip:` is left out on purpose: the protocol never carries addresses. */
const KEPT_KINDS = new Set(["license", "license2", "discord", "steam", "fivem", "xbl", "live"]);

export interface Identity {
  discordId: string | null;
  /** SteamID64, converted from the `steam:` hex identifier. */
  steamId: string | null;
  license: string | null;
  /** Every kept identifier in `kind:value` form, lower-case, for ban matching. */
  ids: string[];
}

const STEAM_ID64 = /^7656119\d{10}$/;
export const DISCORD_ID = /^\d{15,21}$/;

/** Cfx.re reports Steam as `steam:<hex of SteamID64>`; Ticket Tool wants the decimal form. */
export function steamHexToId64(hex: string): string | null {
  if (!/^[0-9a-f]{1,16}$/i.test(hex)) return null;
  const id = BigInt("0x" + hex).toString(10);
  return STEAM_ID64.test(id) ? id : null;
}

export function steamId64ToHex(id64: string): string | null {
  if (!STEAM_ID64.test(id64)) return null;
  return BigInt(id64).toString(16);
}

export function parseIdentifiers(raw: readonly (string | null | undefined)[]): Identity {
  const ids: string[] = [];
  let discordId: string | null = null;
  let steamId: string | null = null;
  let license: string | null = null;
  for (const item of raw) {
    if (typeof item !== "string") continue;
    const colon = item.indexOf(":");
    if (colon <= 0) continue;
    const kind = item.slice(0, colon).toLowerCase();
    const value = item.slice(colon + 1).trim();
    if (!KEPT_KINDS.has(kind) || value === "") continue;
    const normalized = `${kind}:${value.toLowerCase()}`;
    if (!ids.includes(normalized)) ids.push(normalized);
    if (kind === "discord" && DISCORD_ID.test(value) && !discordId) discordId = value;
    if (kind === "steam" && !steamId) steamId = steamHexToId64(value);
    if (kind === "license" && !license) license = value.toLowerCase();
  }
  return { discordId, steamId, license, ids };
}

export function playerIdentifiers(n: Natives, source: string): string[] {
  const count = Number(n.GetNumPlayerIdentifiers(source)) || 0;
  const out: string[] = [];
  for (let i = 0; i < count; i++) {
    const id = n.GetPlayerIdentifier(source, i);
    if (id) out.push(id);
  }
  return out;
}

export interface OnlinePlayer {
  source: string;
  name: string;
  identity: Identity;
}

export function onlinePlayer(n: Natives, source: string): OnlinePlayer {
  return {
    source,
    name: n.GetPlayerName(source) ?? "",
    identity: parseIdentifiers(playerIdentifiers(n, source)),
  };
}

/**
 * The online player whose Cfx.re-reported Discord account is `discordId`.
 * Ticket Tool sends the ticket opener's own Discord id, and FXServer gets
 * `discord:` identifiers straight from Cfx.re, so no linking step is needed.
 */
export function findOnlineByDiscord(n: Natives, discordId: string): OnlinePlayer | null {
  for (const source of n.GetPlayers()) {
    const player = onlinePlayer(n, source);
    if (player.identity.discordId === discordId) return player;
  }
  return null;
}
