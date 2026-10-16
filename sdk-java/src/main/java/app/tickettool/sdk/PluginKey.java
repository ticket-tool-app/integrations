package app.tickettool.sdk;

import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * An integration key ({@code ttp_<16 hex>.<43 base64url>}). Only the derived
 * signing secret is kept; the key string is dropped once that is computed, and
 * {@link #toString()} shows the public part alone so a key can't leak into logs.
 */
public final class PluginKey {
    private static final Pattern PATTERN = Pattern.compile("^ttp_([0-9a-f]{16})\\.([A-Za-z0-9_-]{43})$");

    private final String keyId;
    private final Signer signer;

    private PluginKey(String keyId, Signer signer) {
        this.keyId = keyId;
        this.signer = signer;
    }

    /** @throws IllegalArgumentException when the text isn't a key; the message never repeats it. */
    public static PluginKey parse(String key) {
        if (key == null) {
            throw new IllegalArgumentException("No integration key set");
        }
        String trimmed = key.trim();
        Matcher m = PATTERN.matcher(trimmed);
        if (!m.matches()) {
            throw new IllegalArgumentException(
                    "That isn't an integration key. Keys look like ttp_0123456789abcdef.<43 characters>");
        }
        return new PluginKey(m.group(1), new Signer(Signer.deriveSigningSecret(trimmed)));
    }

    public static boolean looksLikeKey(String key) {
        return key != null && PATTERN.matcher(key.trim()).matches();
    }

    public String keyId() {
        return keyId;
    }

    /** What may be shown or logged: {@code ttp_<keyId>}. */
    public String displayPrefix() {
        return "ttp_" + keyId;
    }

    Signer signer() {
        return signer;
    }

    @Override
    public String toString() {
        return displayPrefix();
    }

    @Override
    public boolean equals(Object o) {
        return o instanceof PluginKey other && other.keyId.equals(keyId) && other.signer.sameSecret(signer);
    }

    @Override
    public int hashCode() {
        return keyId.hashCode();
    }
}
