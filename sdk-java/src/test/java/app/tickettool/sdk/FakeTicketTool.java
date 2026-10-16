package app.tickettool.sdk;

import app.tickettool.sdk.json.Json;
import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;
import java.io.IOException;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.BlockingQueue;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.Executors;
import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicLong;
import java.util.function.Function;

/**
 * A protocol v1 server for tests: checks signatures, the clock window and
 * nonce reuse the way Ticket Tool does, signs its replies, and lets a test
 * script what each endpoint returns.
 */
final class FakeTicketTool implements AutoCloseable {
    static final String KEY = "ttp_0123456789abcdef.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    record Received(String endpoint, Map<String, Object> body, String nonce, long timestamp) {}

    /** What a scripted handler returns; {@code signed=false} mimics pre-auth errors. */
    record Canned(int status, Map<String, Object> body, boolean signed) {
        static Canned ok(Map<String, Object> body) {
            return new Canned(200, body, true);
        }

        static Canned error(int status, String error) {
            return new Canned(status, Map.of("ok", false, "error", error), true);
        }
    }

    private final HttpServer server;
    private final Signer signer = new Signer(Signer.deriveSigningSecret(KEY));
    private final Set<String> nonces = Collections.synchronizedSet(new HashSet<>());
    final List<Received> received = new CopyOnWriteArrayList<>();
    final BlockingQueue<Received> responses = new LinkedBlockingQueue<>();
    final BlockingQueue<Map<String, Object>> pollQueue = new LinkedBlockingQueue<>();
    final AtomicLong serverClockOffset = new AtomicLong();
    private final Map<String, Function<Received, Canned>> overrides = new ConcurrentHashMap<>();
    volatile long pollWaitMillis = 200;
    /** Breaks the reply signature, to check the client refuses it. */
    volatile boolean tamperReplies;

    FakeTicketTool() throws IOException {
        server = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
        server.setExecutor(Executors.newCachedThreadPool());
        server.createContext("/api/plugin/v1/", this::handle);
        server.start();
    }

    String url() {
        return "http://127.0.0.1:" + server.getAddress().getPort();
    }

    void override(String endpoint, Function<Received, Canned> fn) {
        overrides.put(endpoint, fn);
    }

    void clearOverride(String endpoint) {
        overrides.remove(endpoint);
    }

    long now() {
        return System.currentTimeMillis() / 1000 + serverClockOffset.get();
    }

    List<Received> calls(String endpoint) {
        return received.stream().filter(r -> r.endpoint().equals(endpoint)).toList();
    }

    void queueLookup(Map<String, Object> request) {
        pollQueue.add(request);
    }

    static Map<String, Object> lookup(String id, String uuid, long expiresInMillis, List<String> include) {
        Map<String, Object> r = new LinkedHashMap<>();
        r.put("id", id);
        r.put("type", "player.lookup");
        r.put("expiresAt", java.time.Instant.now().plusMillis(expiresInMillis).toString());
        r.put("player", Map.of("uuid", uuid, "name", "Notch"));
        r.put("include", include);
        return r;
    }

    private void handle(HttpExchange ex) throws IOException {
        try (ex) {
            String endpoint = ex.getRequestURI().getPath().substring("/api/plugin/v1".length());
            byte[] body = ex.getRequestBody().readAllBytes();
            String keyId = ex.getRequestHeaders().getFirst("X-TT-Key-Id");
            String tsHeader = ex.getRequestHeaders().getFirst("X-TT-Timestamp");
            String nonce = ex.getRequestHeaders().getFirst("X-TT-Nonce");
            String sig = ex.getRequestHeaders().getFirst("X-TT-Signature");

            if (!"0123456789abcdef".equals(keyId)) {
                reply(ex, endpoint, nonce, new Canned(401, Map.of("ok", false, "error", "invalid_key"), false));
                return;
            }
            if (tsHeader == null || nonce == null || sig == null) {
                reply(ex, endpoint, nonce, unsigned(401, "missing_headers"));
                return;
            }
            if (!nonce.matches("[A-Za-z0-9_-]{16,64}")) {
                reply(ex, endpoint, nonce, unsigned(401, "bad_nonce"));
                return;
            }
            long ts = Long.parseLong(tsHeader);
            if (Math.abs(now() - ts) > 300) {
                Map<String, Object> err = new LinkedHashMap<>();
                err.put("ok", false);
                err.put("error", "stale_timestamp");
                err.put("serverTime", now());
                reply(ex, endpoint, nonce, new Canned(401, err, false));
                return;
            }
            String expected = signer.signRequest("POST", endpoint, ts, nonce, body);
            if (!expected.equals(sig)) {
                reply(ex, endpoint, nonce, unsigned(401, "bad_signature"));
                return;
            }
            if (!nonces.add(nonce)) {
                reply(ex, endpoint, nonce, unsigned(401, "replayed"));
                return;
            }
            Map<String, Object> parsed = Json.parseObject(new String(body, StandardCharsets.UTF_8));
            Received r = new Received(endpoint, parsed, nonce, ts);
            received.add(r);

            Function<Received, Canned> override = overrides.get(endpoint);
            if (override != null) {
                reply(ex, endpoint, nonce, override.apply(r));
                return;
            }
            reply(ex, endpoint, nonce, switch (endpoint) {
                case "/hello" -> Canned.ok(Map.of("ok", true, "protocol", 1, "pluginType", "minecraft",
                        "key", Map.of("name", "Survival"),
                        "server", Map.of("id", "123456789012345678", "name", "My Discord server"),
                        "poll", Map.of("waitSeconds", 25)));
                case "/poll" -> {
                    List<Object> requests = new ArrayList<>();
                    Map<String, Object> first = pollQueue.poll(pollWaitMillis, TimeUnit.MILLISECONDS);
                    if (first != null) {
                        requests.add(first);
                        List<Map<String, Object>> more = new ArrayList<>();
                        pollQueue.drainTo(more, 19);
                        requests.addAll(more);
                    }
                    yield Canned.ok(Map.of("ok", true, "requests", requests));
                }
                case "/respond" -> {
                    responses.add(r);
                    yield Canned.ok(Map.of("ok", true));
                }
                case "/goodbye" -> Canned.ok(Map.of("ok", true));
                case "/link" -> Canned.ok(Map.of("ok", true, "result", "linked",
                        "discord", Map.of("username", "alex"), "player", Map.of("name", "Notch")));
                default -> new Canned(404, Map.of("ok", false, "error", "not_found"), false);
            });
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }
    }

    private static Canned unsigned(int status, String error) {
        Map<String, Object> err = new LinkedHashMap<>();
        err.put("ok", false);
        err.put("error", error);
        return new Canned(status, err, false);
    }

    private void reply(HttpExchange ex, String endpoint, String nonce, Canned canned) throws IOException {
        byte[] out = Json.write(canned.body()).getBytes(StandardCharsets.UTF_8);
        ex.getResponseHeaders().add("Content-Type", "application/json");
        if (canned.signed() && nonce != null) {
            long ts = now();
            String signature = signer.signReply(endpoint, ts, nonce, out);
            if (tamperReplies) {
                signature = signature.substring(0, signature.length() - 1)
                        + (signature.endsWith("0") ? "1" : "0");
            }
            ex.getResponseHeaders().add("X-TT-Timestamp", Long.toString(ts));
            ex.getResponseHeaders().add("X-TT-Nonce", nonce);
            ex.getResponseHeaders().add("X-TT-Signature", signature);
        }
        ex.sendResponseHeaders(canned.status(), out.length);
        ex.getResponseBody().write(out);
    }

    @Override
    public void close() {
        server.stop(0);
    }
}
