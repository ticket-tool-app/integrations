package app.tickettool.sdk;

import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.HexFormat;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

/** Request and reply signatures, protocol v1. */
public final class Signer {
    static final String SIGNING_CONTEXT = "ticket-tool plugin signing v1";
    private static final HexFormat HEX = HexFormat.of();

    /** The HMAC key: the UTF-8 bytes of the hex secret, not the decoded bytes. */
    private final byte[] secret;

    public Signer(String signingSecretHex) {
        this.secret = signingSecretHex.getBytes(StandardCharsets.UTF_8);
    }

    public static String deriveSigningSecret(String fullKey) {
        return HEX.formatHex(hmac(fullKey.getBytes(StandardCharsets.UTF_8),
                SIGNING_CONTEXT.getBytes(StandardCharsets.UTF_8)));
    }

    public static String sha256Hex(byte[] body) {
        try {
            return HEX.formatHex(MessageDigest.getInstance("SHA-256").digest(body));
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException("SHA-256 unavailable", e);
        }
    }

    public static String canonical(String method, String endpoint, long timestamp, String nonce, byte[] body) {
        return String.join("\n", "v1", method, endpoint, Long.toString(timestamp), nonce, sha256Hex(body));
    }

    public String signRequest(String method, String endpoint, long timestamp, String nonce, byte[] body) {
        return sign(canonical(method, endpoint, timestamp, nonce, body));
    }

    public String signReply(String endpoint, long timestamp, String nonce, byte[] body) {
        return sign(canonical("RESPONSE", endpoint, timestamp, nonce, body));
    }

    /** Constant-time, so a forger can't learn the signature a byte at a time. */
    public boolean verifyReply(String endpoint, long timestamp, String nonce, byte[] body, String signature) {
        if (signature == null) {
            return false;
        }
        byte[] want = signReply(endpoint, timestamp, nonce, body).getBytes(StandardCharsets.UTF_8);
        byte[] got = signature.trim().getBytes(StandardCharsets.UTF_8);
        return MessageDigest.isEqual(want, got);
    }

    boolean sameSecret(Signer other) {
        return MessageDigest.isEqual(secret, other.secret);
    }

    private String sign(String canonical) {
        return "v1=" + HEX.formatHex(hmac(secret, canonical.getBytes(StandardCharsets.UTF_8)));
    }

    private static byte[] hmac(byte[] key, byte[] message) {
        try {
            Mac mac = Mac.getInstance("HmacSHA256");
            mac.init(new SecretKeySpec(key, "HmacSHA256"));
            return mac.doFinal(message);
        } catch (GeneralSecurityException e) {
            throw new IllegalStateException("HmacSHA256 unavailable", e);
        }
    }

    @Override
    public String toString() {
        return "Signer[redacted]";
    }
}
