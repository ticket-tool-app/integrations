package app.tickettool.minecraft.bans;

/** A ban plugin threw or its API didn't look as expected. */
public final class BanLookupException extends RuntimeException {
    private static final long serialVersionUID = 1L;

    BanLookupException(String provider, Throwable cause) {
        super(provider + " lookup failed: " + cause.getMessage(), cause);
    }
}
