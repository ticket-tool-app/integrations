package app.tickettool.minecraft.lookup;

import app.tickettool.minecraft.bans.BanProvider;
import app.tickettool.sdk.BanInfo;
import app.tickettool.sdk.LookupRequest;
import app.tickettool.sdk.LookupResult;
import app.tickettool.sdk.PlayerProfile;
import app.tickettool.sdk.RequestHandler;
import java.util.Optional;
import java.util.concurrent.CompletableFuture;
import java.util.function.Supplier;
import java.util.logging.Level;
import java.util.logging.Logger;

/** Answers {@code player.lookup}: the profile from this server, bans from the chosen ban source. */
public final class LookupHandler implements RequestHandler {
    private final ProfileReader profiles;
    private final Supplier<BanProvider> bans;
    private final Logger log;

    public LookupHandler(ProfileReader profiles, Supplier<BanProvider> bans, Logger log) {
        this.profiles = profiles;
        this.bans = bans;
        this.log = log;
    }

    @Override
    public CompletableFuture<LookupResult> handle(LookupRequest request) {
        // The profile is read even for a bans-only request: "never joined and no ban" is unknown_player.
        CompletableFuture<Optional<PlayerProfile>> profile = profiles.read(request.playerUuid(), request.playerName());
        CompletableFuture<BanInfo> ban = request.wantsBans()
                ? bans.get().lookup(request.playerUuid(), request.playerName())
                : CompletableFuture.completedFuture(null);

        return profile.thenCombine(ban, (p, b) -> {
            boolean banned = b != null && b.banned();
            if (p.isEmpty() && !banned) {
                return (LookupResult) LookupResult.failure(LookupResult.UNKNOWN_PLAYER);
            }
            return LookupResult.answer(request.wantsProfile() ? p.orElse(null) : null, b);
        }).exceptionally(error -> {
            Throwable cause = error.getCause() != null ? error.getCause() : error;
            log.log(Level.WARNING, "Couldn't answer a Ticket Tool lookup: " + cause.getMessage(), cause);
            return LookupResult.failure(LookupResult.ERROR, Text.firstLine(cause.getMessage()));
        });
    }

    private static final class Text {
        static String firstLine(String s) {
            if (s == null) {
                return "The plugin hit an error";
            }
            int nl = s.indexOf('\n');
            return nl < 0 ? s : s.substring(0, nl);
        }
    }
}
