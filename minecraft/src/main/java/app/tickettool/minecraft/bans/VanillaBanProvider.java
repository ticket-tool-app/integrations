package app.tickettool.minecraft.bans;

import app.tickettool.minecraft.platform.Scheduler;
import app.tickettool.sdk.BanInfo;
import java.util.Date;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import org.bukkit.BanEntry;
import org.bukkit.BanList;
import org.bukkit.Bukkit;

/** The server's own ban list (banned-players.json), read on the global thread. */
final class VanillaBanProvider implements BanProvider {
    private final Scheduler scheduler;

    VanillaBanProvider(Scheduler scheduler) {
        this.scheduler = scheduler;
    }

    @Override
    public String id() {
        return "vanilla";
    }

    @Override
    public String displayName() {
        return "the server ban list";
    }

    @Override
    public CompletableFuture<BanInfo> lookup(UUID uuid, String name) {
        return scheduler.callGlobal(() -> read(uuid));
    }

    // Raw BanList: Paper and Spigot type the profile list with different PlayerProfile
    // classes, and the erased getBanEntry(Object) is the one signature both share.
    @SuppressWarnings({"rawtypes", "unchecked", "deprecation"})
    private BanInfo read(UUID uuid) {
        BanList list = Bukkit.getBanList(BanList.Type.PROFILE);
        BanEntry entry = list.getBanEntry((Object) Bukkit.createPlayerProfile(uuid));
        if (entry == null) {
            return BanInfo.notBanned(id());
        }
        Date expires = entry.getExpiration();
        if (expires != null && expires.getTime() <= System.currentTimeMillis()) {
            return BanInfo.notBanned(id());
        }
        Date created = entry.getCreated();
        return new BanInfo(true, id(), entry.getReason(), created == null ? null : created.toInstant(),
                expires == null ? null : expires.toInstant(), entry.getSource(), null);
    }
}
