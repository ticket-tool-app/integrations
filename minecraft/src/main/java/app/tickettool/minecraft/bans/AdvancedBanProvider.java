package app.tickettool.minecraft.bans;

import app.tickettool.sdk.BanInfo;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.Executor;

/** AdvancedBan through {@code PunishmentManager}, which may query its database, so async. */
final class AdvancedBanProvider implements BanProvider {
    private final Class<?> manager;
    private final Executor async;

    AdvancedBanProvider(ClassLoader loader, Executor async) throws ClassNotFoundException {
        this.manager = Reflect.type(loader, "me.leoko.advancedban.manager.PunishmentManager");
        this.async = async;
    }

    @Override
    public String id() {
        return "advancedban";
    }

    @Override
    public String displayName() {
        return "AdvancedBan";
    }

    @Override
    public CompletableFuture<BanInfo> lookup(UUID uuid, String name) {
        return CompletableFuture.supplyAsync(() -> {
            try {
                Object pm = Reflect.callStatic(manager, "get");
                // Online-mode servers key AdvancedBan by the undashed UUID; try the dashed form for odd setups.
                Object ban = Reflect.call(pm, "getBan", uuid.toString().replace("-", ""));
                if (ban == null) {
                    ban = Reflect.call(pm, "getBan", uuid.toString());
                }
                if (ban == null || Boolean.TRUE.equals(Reflect.call(ban, "isExpired"))) {
                    return BanInfo.notBanned(id());
                }
                long end = (Long) Reflect.call(ban, "getEnd");
                return new BanInfo(true, id(),
                        (String) Reflect.call(ban, "getReason"),
                        BanProvider.millis((Long) Reflect.call(ban, "getStart")),
                        end <= 0 ? null : BanProvider.millis(end),
                        (String) Reflect.call(ban, "getOperator"),
                        null);
            } catch (ReflectiveOperationException | RuntimeException e) {
                throw new BanLookupException("AdvancedBan", e);
            }
        }, async);
    }
}
