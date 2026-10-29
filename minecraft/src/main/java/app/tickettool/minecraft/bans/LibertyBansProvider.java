package app.tickettool.minecraft.bans;

import app.tickettool.sdk.BanInfo;
import java.time.Instant;
import java.util.List;
import java.util.Optional;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionStage;
import java.util.concurrent.Executor;
import java.util.concurrent.TimeUnit;
import org.bukkit.Bukkit;

/**
 * LibertyBans through its API, found via Omnibus (how LibertyBans publishes
 * it) or Bukkit's ServicesManager. Its selector is asynchronous already; the
 * blocking waits run on {@code async}.
 */
final class LibertyBansProvider implements BanProvider {
    private final ClassLoader loader;
    private final Executor async;
    private final Class<?> apiType;

    LibertyBansProvider(ClassLoader loader, Executor async) throws ClassNotFoundException {
        this.loader = loader;
        this.async = async;
        this.apiType = Reflect.type(loader, "space.arim.libertybans.api.LibertyBans");
    }

    @Override
    public String id() {
        return "libertybans";
    }

    @Override
    public String unbanCommand(String name) {
        return "libertybans:unban " + name;
    }

    @Override
    public String displayName() {
        return "LibertyBans";
    }

    @Override
    public CompletableFuture<BanInfo> lookup(UUID uuid, String name) {
        return CompletableFuture.supplyAsync(() -> {
            try {
                Object api = api();
                Object victim = Reflect.callStatic(Reflect.type(loader, "space.arim.libertybans.api.PlayerVictim"),
                        "of", uuid);
                Object banType = enumConstant("space.arim.libertybans.api.PunishmentType", "BAN");
                Object builder = Reflect.call(Reflect.call(api, "getSelector"), "selectionBuilder");
                if (Reflect.has(builder, "victim", 1)) {
                    builder = Reflect.call(builder, "victim", victim);
                    builder = Reflect.call(builder, "type", banType);
                } else {
                    // 1.1+ takes predicates instead of single values.
                    Class<?> predicate = Reflect.type(loader, "space.arim.libertybans.api.select.SelectionPredicate");
                    builder = Reflect.call(builder, "victims", Reflect.callStatic(predicate, "matchingOnly", victim));
                    builder = Reflect.call(builder, "types", Reflect.callStatic(predicate, "matchingOnly", banType));
                }
                Object order = Reflect.call(builder, "build");
                Object stage = Reflect.call(order, "getAllSpecificPunishments");
                List<?> bans = (List<?>) ((CompletionStage<?>) stage).toCompletableFuture().get(3, TimeUnit.SECONDS);
                if (bans == null || bans.isEmpty()) {
                    return BanInfo.notBanned(id());
                }
                Object ban = bans.get(0);
                boolean permanent = Boolean.TRUE.equals(Reflect.call(ban, "isPermanent"));
                return new BanInfo(true, id(),
                        (String) Reflect.call(ban, "getReason"),
                        (Instant) Reflect.call(ban, "getStartDate"),
                        permanent ? null : (Instant) Reflect.call(ban, "getEndDate"),
                        operatorName(api, Reflect.call(ban, "getOperator")),
                        bans.size());
            } catch (Exception e) {
                throw new BanLookupException("LibertyBans", e);
            }
        }, async);
    }

    private Object api() throws ReflectiveOperationException {
        try {
            Class<?> provider = Reflect.type(loader, "space.arim.omnibus.OmnibusProvider");
            Object registry = Reflect.call(Reflect.callStatic(provider, "getOmnibus"), "getRegistry");
            Object found = Reflect.call(registry, "getProvider", apiType);
            if (found instanceof Optional<?> opt && opt.isPresent()) {
                return opt.get();
            }
        } catch (ClassNotFoundException ignored) {
            // Older or repackaged builds: fall through to the ServicesManager.
        }
        Object viaBukkit = Bukkit.getServicesManager().load(apiType);
        if (viaBukkit == null) {
            throw new IllegalStateException("LibertyBans API not registered");
        }
        return viaBukkit;
    }

    /** Console bans read as "Console"; a player's UUID is resolved to a name when LibertyBans knows it. */
    private String operatorName(Object api, Object operator) {
        try {
            Object type = Reflect.call(operator, "getType");
            if (type != null && "CONSOLE".equals(type.toString())) {
                return "Console";
            }
            UUID opUuid = (UUID) Reflect.call(operator, "getUUID");
            Object future = Reflect.call(Reflect.call(api, "getUserResolver"), "lookupName", opUuid);
            Object name = ((CompletionStage<?>) future).toCompletableFuture().get(1, TimeUnit.SECONDS);
            if (name instanceof Optional<?> opt && opt.isPresent()) {
                return opt.get().toString();
            }
            return opUuid.toString();
        } catch (Exception e) {
            return null;
        }
    }

    private Object enumConstant(String typeName, String constant) throws ClassNotFoundException {
        Class<?> type = Reflect.type(loader, typeName);
        for (Object c : type.getEnumConstants()) {
            if (((Enum<?>) c).name().equals(constant)) {
                return c;
            }
        }
        throw new IllegalStateException(typeName + "." + constant + " missing");
    }
}
