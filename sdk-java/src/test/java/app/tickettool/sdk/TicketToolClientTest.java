package app.tickettool.sdk;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import app.tickettool.sdk.FakeTicketTool.Canned;
import app.tickettool.sdk.json.JsonFields;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.InstantSource;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.function.BooleanSupplier;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

class TicketToolClientTest {
    private static final String UUID_DASHED = "069a79f4-44e9-4726-a5be-fca90e38aaf5";

    private FakeTicketTool server;
    private TicketToolClient client;
    private final List<Duration> sleeps = new CopyOnWriteArrayList<>();
    private final Sleeper recordingSleeper = d -> {
        sleeps.add(d);
        Thread.sleep(5);
    };

    @BeforeEach
    void startServer() throws Exception {
        server = new FakeTicketTool();
    }

    @AfterEach
    void stopAll() {
        if (client != null) {
            client.stop();
        }
        server.close();
    }

    private TicketToolClient.Builder builder(RequestHandler handler) {
        return TicketToolClient.builder()
                .key(FakeTicketTool.KEY)
                .apiUrl(server.url())
                .plugin(new PluginInfo("minecraft", "ticket-tool-minecraft", "1.0.0", "Test 1.0"))
                .banProviders(() -> List.of("litebans"))
                .sleeper(recordingSleeper)
                .handler(handler);
    }

    private static RequestHandler profileHandler() {
        return request -> CompletableFuture.completedFuture(LookupResult.answer(
                new PlayerProfile(request.playerUuid(), "Notch", true, Instant.parse("2026-10-15T11:58:00Z"),
                        Instant.parse("2011-02-03T18:00:00Z"), "survival-1"),
                new BanInfo(true, "LiteBans", "§cX-ray", Instant.parse("2026-09-01T20:15:00Z"), null, "ModAlex", 2)));
    }

    private static void await(BooleanSupplier condition, String what) throws InterruptedException {
        long end = System.nanoTime() + TimeUnit.SECONDS.toNanos(10);
        while (!condition.getAsBoolean()) {
            if (System.nanoTime() > end) {
                throw new AssertionError("Timed out waiting for " + what);
            }
            Thread.sleep(10);
        }
    }

    @Test
    void helloPollRespondRoundTrip() throws Exception {
        client = builder(profileHandler()).build();
        client.start();
        await(() -> !server.calls("/poll").isEmpty(), "first poll");

        Map<String, Object> hello = server.calls("/hello").get(0).body();
        assertEquals(1L, hello.get("protocol"));
        assertEquals("minecraft", JsonFields.string(JsonFields.object(hello, "plugin"), "type"));
        assertEquals(List.of("litebans"), hello.get("banProviders"));

        String id = UUID.randomUUID().toString();
        server.queueLookup(FakeTicketTool.lookup(id, UUID_DASHED, 4000, List.of("profile", "bans", "future-part")));
        FakeTicketTool.Received answer = server.responses.poll(5, TimeUnit.SECONDS);
        assertNotNull(answer, "no /respond");
        Map<String, Object> body = answer.body();
        assertEquals(id, body.get("requestId"));
        assertEquals(true, body.get("ok"));
        Map<String, Object> profile = JsonFields.object(body, "profile");
        assertEquals(UUID_DASHED, profile.get("uuid"));
        assertEquals(true, profile.get("online"));
        assertEquals("2026-10-15T11:58:00Z", profile.get("lastSeen"));
        assertEquals("survival-1", profile.get("server"));
        Map<String, Object> bans = JsonFields.object(body, "bans");
        assertEquals("litebans", bans.get("provider"));
        assertEquals("X-ray", bans.get("reason"));
        assertNull(bans.get("expiresAt"));
        assertTrue(bans.containsKey("expiresAt"), "permanent bans send expiresAt: null");
        assertEquals(2L, bans.get("activeCount"));

        ClientStatus status = client.status();
        assertEquals(ClientStatus.State.CONNECTED, status.state());
        assertEquals("Survival", status.hello().keyName());
        await(() -> client.status().answered() == 1, "answered count");
    }

    @Test
    void sendsOnlyThePartsAskedFor() throws Exception {
        client = builder(profileHandler()).build();
        client.start();
        server.queueLookup(FakeTicketTool.lookup(UUID.randomUUID().toString(), UUID_DASHED, 4000, List.of("profile")));
        Map<String, Object> body = server.responses.poll(5, TimeUnit.SECONDS).body();
        assertTrue(body.containsKey("profile"));
        assertFalse(body.containsKey("bans"));
    }

    @Test
    void skipsExpiredAndUnknownRequests() throws Exception {
        AtomicInteger handled = new AtomicInteger();
        client = builder(r -> {
            handled.incrementAndGet();
            return profileHandler().handle(r);
        }).build();
        client.start();
        server.queueLookup(FakeTicketTool.lookup(UUID.randomUUID().toString(), UUID_DASHED, -1000, List.of("profile")));
        Map<String, Object> unknown = FakeTicketTool.lookup(UUID.randomUUID().toString(), UUID_DASHED, 4000,
                List.of("profile"));
        unknown.put("type", "player.teleport");
        server.queueLookup(unknown);
        String onlyUnknownParts = UUID.randomUUID().toString();
        server.queueLookup(FakeTicketTool.lookup(onlyUnknownParts, UUID_DASHED, 4000, List.of("inventory")));

        FakeTicketTool.Received answer = server.responses.poll(5, TimeUnit.SECONDS);
        assertEquals(onlyUnknownParts, answer.body().get("requestId"));
        assertEquals("unsupported", answer.body().get("error"));
        assertNull(server.responses.poll(500, TimeUnit.MILLISECONDS));
        assertEquals(0, handled.get());
    }

    @Test
    void reportsASlowHandlerAsAnErrorBeforeExpiry() throws Exception {
        client = builder(r -> new CompletableFuture<>()).answerBudget(Duration.ofMillis(300)).build();
        client.start();
        server.queueLookup(FakeTicketTool.lookup(UUID.randomUUID().toString(), UUID_DASHED, 4000, List.of("bans")));
        Map<String, Object> body = server.responses.poll(5, TimeUnit.SECONDS).body();
        assertEquals(false, body.get("ok"));
        assertEquals("error", body.get("error"));
    }

    @Test
    void correctsClockSkewFromServerTime() throws Exception {
        InstantSource skewed = Clock.offset(Clock.systemUTC(), Duration.ofSeconds(-1000));
        client = builder(profileHandler()).clock(skewed).build();
        client.start();
        await(() -> !server.calls("/poll").isEmpty(), "poll after skew correction");
        long offset = client.status().clockOffsetSeconds();
        assertTrue(Math.abs(offset - 1000) <= 2, "offset was " + offset);
        assertEquals(ClientStatus.State.CONNECTED, client.status().state());
    }

    @Test
    void retriesStaleTimestampOnlyOnce() throws Exception {
        AtomicInteger hellos = new AtomicInteger();
        server.override("/hello", r -> {
            hellos.incrementAndGet();
            return new Canned(401, Map.of("ok", false, "error", "stale_timestamp", "serverTime", server.now() + 5000),
                    false);
        });
        client = builder(profileHandler()).build();
        client.start();
        await(() -> sleeps.size() >= 1, "backoff after repeated stale_timestamp");
        // One retry inside the call, then the loop backs off rather than spinning.
        assertTrue(hellos.get() <= 3, "hello sent " + hellos.get() + " times before backing off");
    }

    @Test
    void backsOffOnServerErrorsAndResetsAfterSuccess() throws Exception {
        AtomicInteger failures = new AtomicInteger(5);
        server.override("/poll", r -> failures.getAndDecrement() > 0
                ? new Canned(503, Map.of("ok", false, "error", "unavailable"), true)
                : Canned.ok(Map.of("ok", true, "requests", List.of())));
        client = builder(profileHandler()).build();
        client.start();
        await(() -> server.calls("/poll").size() >= 7, "polls after recovery");

        long[] steps = {1000, 2000, 4000, 8000, 16000};
        assertTrue(sleeps.size() >= steps.length);
        for (int i = 0; i < steps.length; i++) {
            long ms = sleeps.get(i).toMillis();
            assertTrue(ms >= steps[i] / 2 && ms <= steps[i], "sleep " + i + " was " + ms);
        }
        assertEquals(steps.length, sleeps.size(), "no sleeps once polls succeed");
        assertEquals(ClientStatus.State.CONNECTED, client.status().state());
    }

    @Test
    void waitsOutPollLeaseErrors() throws Exception {
        AtomicInteger failures = new AtomicInteger(1);
        server.override("/poll", r -> failures.getAndDecrement() > 0
                ? Canned.error(429, "too_many_polls")
                : Canned.ok(Map.of("ok", true, "requests", List.of())));
        client = builder(profileHandler()).build();
        client.start();
        await(() -> server.calls("/poll").size() >= 2, "poll after lease wait");
        assertTrue(sleeps.get(0).compareTo(Duration.ofSeconds(15)) >= 0, "waited " + sleeps.get(0));
    }

    @Test
    void stopsForGoodOnInvalidKey() throws Exception {
        server.override("/hello", r -> new Canned(401, Map.of("ok", false, "error", "invalid_key"), false));
        client = builder(profileHandler()).build();
        client.start();
        await(() -> client.status().state() == ClientStatus.State.STOPPED_INVALID_KEY, "invalid key stop");
        Thread.sleep(200);
        assertEquals(1, server.calls("/hello").size());
        assertTrue(server.calls("/poll").isEmpty());
        assertFalse(client.status().lastError().contains("AAAA"), "status must not include the key");
    }

    @Test
    void pausesHourlyWhenThePlanLapses() throws Exception {
        AtomicInteger refusals = new AtomicInteger(1);
        server.override("/poll", r -> refusals.getAndDecrement() > 0
                ? new Canned(403, Map.of("ok", false, "error", "plan_not_entitled"), false)
                : Canned.ok(Map.of("ok", true, "requests", List.of())));
        client = builder(profileHandler()).build();
        client.start();
        await(() -> server.calls("/hello").size() >= 2, "hello again after the pause");
        assertEquals(Duration.ofHours(1), sleeps.get(0));
    }

    @Test
    void refusesUnsignedAndTamperedSuccessReplies() throws Exception {
        server.override("/hello", r -> new Canned(200, Map.of("ok", true, "protocol", 1), false));
        client = builder(profileHandler()).build();
        client.start();
        await(() -> client.status().lastError() != null, "unsigned reply refusal");
        assertEquals(ClientStatus.State.RECONNECTING, client.status().state());
        assertTrue(server.calls("/poll").isEmpty());
        client.stop();

        server.clearOverride("/hello");
        server.tamperReplies = true;
        client = builder(profileHandler()).build();
        client.start();
        await(() -> client.status().lastError() != null, "tampered reply refusal");
        assertTrue(client.status().lastError().contains("signature"), client.status().lastError());
        assertTrue(server.calls("/poll").isEmpty());
    }

    @Test
    void usesAFreshNoncePerRequestIncludingRetries() throws Exception {
        AtomicInteger failures = new AtomicInteger(3);
        server.override("/poll", r -> failures.getAndDecrement() > 0
                ? new Canned(502, Map.of(), false)
                : Canned.ok(Map.of("ok", true, "requests", List.of())));
        client = builder(profileHandler()).build();
        client.start();
        await(() -> server.calls("/poll").size() >= 6, "polls");
        Set<String> seen = new HashSet<>();
        for (FakeTicketTool.Received r : server.received) {
            assertTrue(seen.add(r.nonce()), "nonce reused");
            assertEquals(22, r.nonce().length());
        }
    }

    @Test
    void sendsGoodbyeOnStop() throws Exception {
        client = builder(profileHandler()).build();
        client.start();
        await(() -> !server.calls("/poll").isEmpty(), "poll");
        long start = System.nanoTime();
        client.stop();
        assertTrue(System.nanoTime() - start < TimeUnit.SECONDS.toNanos(4));
        assertEquals(1, server.calls("/goodbye").size());
        assertEquals(ClientStatus.State.STOPPED, client.status().state());
        assertThrows(IllegalStateException.class, client::start);
    }

    @Test
    void linksAndMapsLinkErrors() throws Exception {
        client = builder(profileHandler()).build();
        UUID uuid = UUID.fromString(UUID_DASHED);

        LinkResult linked = client.link(uuid, "Notch", "abcd-efgh").get(5, TimeUnit.SECONDS);
        assertEquals(LinkResult.Outcome.LINKED, linked.outcome());
        assertEquals("alex", linked.discordUsername());
        Map<String, Object> sent = server.calls("/link").get(0).body();
        assertEquals("abcd-efgh", sent.get("code"));
        assertEquals(UUID_DASHED, JsonFields.string(JsonFields.object(sent, "player"), "uuid"));

        server.override("/link", r -> Canned.error(404, "invalid_code"));
        assertEquals(LinkResult.Outcome.INVALID_CODE, client.link(uuid, "Notch", "x").get(5, TimeUnit.SECONDS).outcome());

        server.override("/link", r -> new Canned(503, Map.of("ok", false, "error", "unavailable", "retryable", true), true));
        assertEquals(LinkResult.Outcome.UNAVAILABLE, client.link(uuid, "Notch", "x").get(5, TimeUnit.SECONDS).outcome());

        server.override("/link", r -> Canned.error(422, "unknown_player"));
        assertEquals(LinkResult.Outcome.UNKNOWN_PLAYER, client.link(uuid, "Notch", "x").get(5, TimeUnit.SECONDS).outcome());

        server.override("/link", r -> Canned.ok(Map.of("ok", true, "result", "relinked",
                "discord", Map.of("username", "alex"), "player", Map.of("name", "Notch"))));
        assertEquals(LinkResult.Outcome.RELINKED, client.link(uuid, "Notch", "x").get(5, TimeUnit.SECONDS).outcome());
    }

    @Test
    void refusesPlainHttpExceptForLocalhost() {
        assertThrows(IllegalArgumentException.class, () -> TicketToolClient.apiBase("http://example.com"));
        assertEquals("https://dash.ticket-tool.app/api/plugin/v1", TicketToolClient.apiBase(null).toString());
        assertEquals("https://api.ticket-tool.app/api/plugin/v1",
                TicketToolClient.apiBase("https://api.ticket-tool.app/api/plugin/v1/").toString());
        assertEquals("http://localhost:3001/api/plugin/v1", TicketToolClient.apiBase("http://localhost:3001").toString());
    }
}
