import { LookupRequest, LookupResult, PlayerProfile, ProfileField, stripCfxColors } from "@ticket-tool/plugin-sdk";
import { BanProviderSetting, activeSources, lookupBans } from "./bans";
import { BanSource } from "./bans/types";
import { Frameworks } from "./frameworks";
import { DISCORD_ID, findOnlineByDiscord, steamHexToId64 } from "./identity";
import { Natives } from "./natives";
import { SeenStore } from "./seen-store";

export interface LookupDeps {
  natives: Natives;
  store: SeenStore;
  frameworks: Frameworks;
  banSources: readonly BanSource[];
  banProvider: () => BanProviderSetting;
  serverName: () => string | null;
  now?: () => number;
  warn?: (message: string) => void;
}

/**
 * Answers `player.lookup` for a FiveM/RedM key: the player is the one whose
 * Cfx.re `discord:` identifier is the requested Discord id, online now or
 * remembered in the last-seen store.
 */
export function createLookupHandler(deps: LookupDeps) {
  const now = deps.now ?? Date.now;
  const warn = deps.warn ?? (() => {});
  return async (request: LookupRequest): Promise<LookupResult> => {
    const discordId = request.player.discordId;
    if (!discordId || !DISCORD_ID.test(discordId)) return LookupResult.failure("unsupported");

    const online = findOnlineByDiscord(deps.natives, discordId);
    let fields: ProfileField[] = [];
    if (online) {
      fields = deps.frameworks.fields(online.source);
      deps.store.touch(online, fields);
    }
    const seen = deps.store.get(discordId);

    const ids = new Set<string>([`discord:${discordId}`]);
    for (const id of online?.identity.ids ?? seen?.ids ?? []) ids.add(id);

    let bans = null;
    let noBanProvider = false;
    if (request.wantsBans) {
      const sources = activeSources(deps.banSources, deps.banProvider());
      bans = await lookupBans(sources, [...ids], (source, error) =>
        warn(`Couldn't read ${source.name} bans: ${(error as Error)?.message ?? String(error)}`),
      );
      noBanProvider = bans == null;
    }

    if (!online && !seen && !bans?.banned) {
      return LookupResult.failure(noBanProvider ? "no_ban_provider" : "unknown_player");
    }
    if (noBanProvider && !request.wantsProfile) return LookupResult.failure("no_ban_provider");

    let profile: PlayerProfile | null = null;
    if (request.wantsProfile && (online || seen)) {
      const shownFields: ProfileField[] = [];
      if (online) shownFields.push({ label: "Server ID", value: online.source });
      shownFields.push(...(online ? fields : seen?.fields ?? []));
      profile = {
        discordId,
        steamId: online?.identity.steamId ?? seen?.steamId ?? steamFromIds([...ids]),
        name: stripCfxColors(online?.name || seen?.name || "Unknown")!,
        online: online != null,
        lastSeen: online ? new Date(now()) : seen ? new Date(seen.lastSeen) : null,
        firstSeen: seen ? new Date(seen.firstSeen) : null,
        server: deps.serverName(),
        fields: shownFields,
      };
    }
    // With no ban source the bans part is left out rather than failing the whole answer,
    // so a server without txAdmin still gets profile lookups.
    return LookupResult.answer(profile, bans);
  };
}

function steamFromIds(ids: string[]): string | null {
  const steam = ids.find((i) => i.startsWith("steam:"));
  return steam ? steamHexToId64(steam.slice(6)) : null;
}
