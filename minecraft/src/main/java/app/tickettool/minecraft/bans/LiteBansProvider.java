package app.tickettool.minecraft.bans;

import app.tickettool.sdk.BanInfo;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.Executor;

/** LiteBans through {@code litebans.api.Database}; every call hits its database, so it runs async. */
final class LiteBansProvider implements BanProvider {
    private final Class<?> database;
    private final Executor async;

    LiteBansProvider(ClassLoader loader, Executor async) throws ClassNotFoundException {
        this.database = Reflect.type(loader, "litebans.api.Database");
        this.async = async;
    }

    @Override
    public String id() {
        return "litebans";
    }

    @Override
    public String unbanCommand(String name) {
        return "litebans:unban " + name;
    }

    @Override
    public String displayName() {
        return "LiteBans";
    }

    @Override
    public CompletableFuture<BanInfo> lookup(UUID uuid, String name) {
        return CompletableFuture.supplyAsync(() -> {
            try {
                Object db = Reflect.callStatic(database, "get");
                // No IP: the protocol never carries one. A null server scope means this server, which includes global bans.
                Object entry = Reflect.call(db, "getBan", uuid, null, null);
                if (entry == null || !Boolean.TRUE.equals(Reflect.call(entry, "isActive"))
                        || Boolean.TRUE.equals(Reflect.call(entry, "isExpired", System.currentTimeMillis()))) {
                    return BanInfo.notBanned(id());
                }
                boolean permanent = Boolean.TRUE.equals(Reflect.call(entry, "isPermanent"));
                return new BanInfo(true, id(),
                        (String) Reflect.call(entry, "getReason"),
                        BanProvider.millis((Long) Reflect.call(entry, "getDateStart")),
                        permanent ? null : BanProvider.millis((Long) Reflect.call(entry, "getDateEnd")),
                        (String) Reflect.call(entry, "getExecutorName"),
                        activeCount(db, uuid));
            } catch (ReflectiveOperationException | RuntimeException e) {
                throw new BanLookupException("LiteBans", e);
            }
        }, async);
    }

    /** Best effort: the count is a nicety on the card, so a failed query just leaves it out. */
    private static Integer activeCount(Object db, UUID uuid) {
        String sql = "SELECT COUNT(*) FROM {bans} WHERE uuid = ? AND active AND (until < 1 OR until > ?)";
        try (PreparedStatement st = (PreparedStatement) Reflect.call(db, "prepareStatement", sql)) {
            st.setString(1, uuid.toString());
            st.setLong(2, System.currentTimeMillis());
            try (ResultSet rs = st.executeQuery()) {
                return rs.next() ? rs.getInt(1) : null;
            }
        } catch (Exception e) {
            return null;
        }
    }
}
