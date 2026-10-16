package app.tickettool.sdk;

import app.tickettool.sdk.json.Json;
import app.tickettool.sdk.json.JsonException;
import app.tickettool.sdk.json.JsonFields;
import java.io.IOException;
import java.io.InputStream;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.security.SecureRandom;
import java.time.Duration;
import java.time.InstantSource;
import java.util.Base64;
import java.util.Map;
import java.util.concurrent.atomic.AtomicLong;

/** One signed request and its verified reply. Knows nothing about the poll loop. */
final class Transport {
    static final int SIGNATURE_WINDOW_SECONDS = 300;
    /** Replies are small JSON; anything near this is not from Ticket Tool. */
    static final int MAX_REPLY_BYTES = 256 * 1024;

    private static final Base64.Encoder NONCE_ENCODER = Base64.getUrlEncoder().withoutPadding();

    private final HttpClient http;
    private final URI base;
    private final PluginKey key;
    private final String userAgent;
    private final InstantSource clock;
    private final SecureRandom random = new SecureRandom();
    /** serverTime - localTime, learned from a stale_timestamp error. */
    private final AtomicLong offsetSeconds = new AtomicLong();

    record Reply(int status, Map<String, Object> body, boolean signed) {}

    Transport(HttpClient http, URI base, PluginKey key, String userAgent, InstantSource clock) {
        this.http = http;
        this.base = base;
        this.key = key;
        this.userAgent = userAgent;
        this.clock = clock;
    }

    long nowSeconds() {
        return clock.instant().getEpochSecond() + offsetSeconds.get();
    }

    long offsetSeconds() {
        return offsetSeconds.get();
    }

    void correctClock(long serverTime) {
        offsetSeconds.set(serverTime - clock.instant().getEpochSecond());
    }

    String newNonce() {
        byte[] bytes = new byte[16];
        random.nextBytes(bytes);
        return NONCE_ENCODER.encodeToString(bytes);
    }

    /**
     * Sends {@code payload} to {@code endpoint} (e.g. {@code /poll}). The body
     * is serialized once and those exact bytes are both hashed and sent.
     */
    Reply send(String endpoint, Map<String, Object> payload, Duration timeout)
            throws IOException, InterruptedException {
        byte[] body = Json.write(payload == null ? Map.of() : payload).getBytes(StandardCharsets.UTF_8);
        long ts = nowSeconds();
        String nonce = newNonce();
        HttpRequest request = HttpRequest.newBuilder(resolve(endpoint))
                .timeout(timeout)
                .header("Content-Type", "application/json")
                .header("Accept", "application/json")
                .header("X-TT-Key-Id", key.keyId())
                .header("X-TT-Timestamp", Long.toString(ts))
                .header("X-TT-Nonce", nonce)
                .header("X-TT-Signature", key.signer().signRequest("POST", endpoint, ts, nonce, body))
                .header("X-TT-Protocol", Integer.toString(TicketToolClient.PROTOCOL_VERSION))
                .header("X-TT-Plugin", userAgent)
                .header("User-Agent", userAgent)
                .POST(HttpRequest.BodyPublishers.ofByteArray(body))
                .build();

        HttpResponse<InputStream> response = http.send(request, HttpResponse.BodyHandlers.ofInputStream());
        byte[] raw = readCapped(response.body());
        int status = response.statusCode();

        String signature = response.headers().firstValue("X-TT-Signature").orElse(null);
        boolean signed = signature != null;
        if (signed) {
            verify(endpoint, nonce, raw, signature, response);
        } else if (status >= 200 && status < 300) {
            // Only pre-auth errors may be unsigned; an unsigned success could be anyone's.
            throw new ReplyVerificationException("Unsigned success reply from " + endpoint);
        }

        Map<String, Object> parsed;
        try {
            parsed = raw.length == 0 ? Map.of() : Json.parseObject(new String(raw, StandardCharsets.UTF_8));
        } catch (JsonException e) {
            if (status >= 200 && status < 300) {
                throw new IOException("Reply from " + endpoint + " wasn't JSON", e);
            }
            parsed = Map.of();
        }
        return new Reply(status, parsed, signed);
    }

    /**
     * {@link #send} plus error mapping: returns the body of an {@code ok}
     * reply, retries once after correcting the clock from a stale_timestamp
     * error, and throws {@link ApiException} for anything else.
     */
    Map<String, Object> call(String endpoint, Map<String, Object> payload, Duration timeout)
            throws IOException, InterruptedException, ApiException {
        for (int attempt = 0; ; attempt++) {
            Reply reply = send(endpoint, payload, timeout);
            boolean success = reply.status() >= 200 && reply.status() < 300
                    && !Boolean.FALSE.equals(JsonFields.bool(reply.body(), "ok"));
            if (success) {
                return reply.body();
            }
            ApiException error = new ApiException(reply.status(), JsonFields.string(reply.body(), "error"),
                    reply.body());
            Long serverTime = JsonFields.number(reply.body(), "serverTime");
            if (attempt == 0 && "stale_timestamp".equals(error.error()) && serverTime != null) {
                correctClock(serverTime);
                continue;
            }
            throw error;
        }
    }

    private void verify(String endpoint, String nonce, byte[] raw, String signature, HttpResponse<?> response)
            throws ReplyVerificationException {
        String echoed = response.headers().firstValue("X-TT-Nonce").orElse(null);
        if (!nonce.equals(echoed)) {
            throw new ReplyVerificationException("Reply nonce doesn't match the request");
        }
        long replyTs;
        try {
            replyTs = Long.parseLong(response.headers().firstValue("X-TT-Timestamp").orElse("").trim());
        } catch (NumberFormatException e) {
            throw new ReplyVerificationException("Reply timestamp missing or malformed");
        }
        if (Math.abs(nowSeconds() - replyTs) > SIGNATURE_WINDOW_SECONDS) {
            throw new ReplyVerificationException("Reply timestamp outside the signature window");
        }
        if (!key.signer().verifyReply(endpoint, replyTs, nonce, raw, signature)) {
            throw new ReplyVerificationException("Reply signature doesn't match");
        }
    }

    private URI resolve(String endpoint) {
        String b = base.toString();
        return URI.create((b.endsWith("/") ? b.substring(0, b.length() - 1) : b) + endpoint);
    }

    private static byte[] readCapped(InputStream in) throws IOException {
        try (in) {
            byte[] bytes = in.readNBytes(MAX_REPLY_BYTES + 1);
            if (bytes.length > MAX_REPLY_BYTES) {
                throw new IOException("Reply larger than " + MAX_REPLY_BYTES + " bytes");
            }
            return bytes;
        }
    }
}
