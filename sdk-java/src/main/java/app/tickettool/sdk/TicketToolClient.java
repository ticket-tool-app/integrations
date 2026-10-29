package app.tickettool.sdk;

import java.io.IOException;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpTimeoutException;
import java.time.Duration;
import java.time.Instant;
import java.time.InstantSource;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Objects;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionException;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;
import java.util.concurrent.atomic.AtomicLong;
import java.util.function.Supplier;
import java.util.random.RandomGenerator;

/**
 * A Ticket Tool game plugin client: {@code /hello}, one long-poll loop on its
 * own thread, answers on worker threads, {@code /link} on demand and
 * {@code /goodbye} on {@link #stop()}.
 *
 * <pre>{@code
 * TicketToolClient client = TicketToolClient.builder()
 *         .key(config.getString("key"))
 *         .plugin(new PluginInfo("minecraft", "ticket-tool-minecraft", "1.0.0", "Paper 1.21.1"))
 *         .handler(request -> answer(request))
 *         .build();
 * client.start();
 * }</pre>
 */
public final class TicketToolClient {
    public static final int PROTOCOL_VERSION = 1;
    public static final String DEFAULT_API_URL = "https://dash.ticket-tool.app";
    static final String API_PATH = "/api/plugin/v1";

    static final Duration CONNECT_TIMEOUT = Duration.ofSeconds(10);
    static final Duration CALL_TIMEOUT = Duration.ofSeconds(10);
    static final Duration ACTION_BUDGET = Duration.ofSeconds(8);
    static final Duration GOODBYE_TIMEOUT = Duration.ofSeconds(2);
    static final Duration PLAN_RETRY = Duration.ofHours(1);
    static final Duration RATE_LIMIT_WAIT = Duration.ofMinutes(1);
    // A crashed process's poll lease lapses on the server within 40 s.
    static final Duration TOO_MANY_POLLS_MIN_WAIT = Duration.ofSeconds(15);

    private final PluginKey key;
    private final PluginInfo plugin;
    private final RequestHandler handler;
    private final Supplier<List<String>> banProviders;
    private final ActionHandler actionHandler;
    private final Supplier<List<String>> actions;
    private final java.util.function.BooleanSupplier reportsEnabled;
    private final SdkLogger log;
    private final InstantSource clock;
    private final Sleeper sleeper;
    private final RandomGenerator random;
    private final Duration answerBudget;
    private final HttpClient http;
    private final Transport transport;
    private final ExecutorService workers;
    private final Backoff backoff;

    private final Object lock = new Object();
    private Thread pollThread;
    private volatile boolean running;
    private volatile boolean closed;
    private volatile boolean everConnected;
    private volatile boolean needHello = true;

    private volatile ClientStatus.State state = ClientStatus.State.STOPPED;
    private volatile String lastError;
    private volatile Instant lastErrorAt;
    private volatile HelloReply hello;
    private volatile Instant lastPollAt;
    private final AtomicLong answered = new AtomicLong();
    private int failureStreak;

    private TicketToolClient(Builder b) {
        this.key = PluginKey.parse(b.key);
        this.plugin = Objects.requireNonNull(b.plugin, "plugin");
        this.handler = Objects.requireNonNull(b.handler, "handler");
        this.banProviders = b.banProviders;
        this.actionHandler = b.actionHandler;
        this.actions = b.actions;
        this.reportsEnabled = b.reportsEnabled;
        this.log = b.logger;
        this.clock = b.clock;
        this.sleeper = b.sleeper;
        this.random = b.random;
        this.answerBudget = b.answerBudget;
        this.http = HttpClient.newBuilder().connectTimeout(CONNECT_TIMEOUT).build();
        this.transport = new Transport(http, apiBase(b.apiUrl), key, plugin.userAgent(), clock);
        this.workers = Executors.newThreadPerTaskExecutor(
                Thread.ofVirtual().name("TicketTool-worker-", 0).factory());
        this.backoff = Backoff.standard(random);
    }

    public static Builder builder() {
        return new Builder();
    }

    /** The URL every endpoint hangs off, from a host root or an already complete base. */
    static URI apiBase(String apiUrl) {
        String url = apiUrl == null || apiUrl.isBlank() ? DEFAULT_API_URL : apiUrl.trim();
        while (url.endsWith("/")) {
            url = url.substring(0, url.length() - 1);
        }
        if (!url.endsWith(API_PATH)) {
            url += API_PATH;
        }
        URI uri;
        try {
            uri = URI.create(url);
        } catch (IllegalArgumentException e) {
            throw new IllegalArgumentException("api-url isn't a valid URL: " + apiUrl);
        }
        String scheme = uri.getScheme() == null ? "" : uri.getScheme().toLowerCase(Locale.ROOT);
        String host = uri.getHost() == null ? "" : uri.getHost();
        boolean loopback = host.equals("localhost") || host.startsWith("127.") || host.equals("[::1]")
                || host.equals("::1");
        if (!scheme.equals("https") && !(scheme.equals("http") && loopback)) {
            throw new IllegalArgumentException("api-url must use https:// (plain http is only allowed for localhost)");
        }
        return uri;
    }

    public PluginKey key() {
        return key;
    }

    public ClientStatus status() {
        return new ClientStatus(state, lastError, lastErrorAt, hello, lastPollAt, answered.get(),
                transport.offsetSeconds());
    }

    /** Starts the poll loop. Returns at once; {@code /hello} is sent from the poll thread. */
    public void start() {
        synchronized (lock) {
            if (closed) {
                throw new IllegalStateException("A stopped client can't be restarted; build a new one");
            }
            if (running) {
                return;
            }
            running = true;
            state = ClientStatus.State.STARTING;
            pollThread = Thread.ofPlatform().daemon().name("TicketTool-poll").unstarted(this::runLoop);
            pollThread.start();
        }
    }

    /**
     * Stops polling and sends {@code /goodbye} so lookups show the plugin as
     * offline at once. Blocks for at most about three seconds.
     */
    public void stop() {
        Thread thread;
        synchronized (lock) {
            if (closed) {
                return;
            }
            closed = true;
            running = false;
            thread = pollThread;
        }
        if (thread != null) {
            thread.interrupt();
            try {
                thread.join(1000);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
            }
        }
        if (everConnected && state != ClientStatus.State.STOPPED_INVALID_KEY) {
            try {
                CompletableFuture.supplyAsync(() -> {
                    try {
                        return transport.call("/goodbye", Map.of(), GOODBYE_TIMEOUT);
                    } catch (Exception e) {
                        throw new CompletionException(e);
                    }
                }, workers).get(GOODBYE_TIMEOUT.toMillis() + 500, TimeUnit.MILLISECONDS);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
            } catch (ExecutionException | TimeoutException e) {
                log.debug("Goodbye not delivered: " + rootMessage(e));
            }
        }
        if (state.running()) {
            state = ClientStatus.State.STOPPED;
        }
        workers.shutdownNow();
        http.shutdownNow();
    }

    /**
     * Reports that a player typed a link code. Pass the UUID and name of the
     * player who ran the command, never values from its arguments. Never
     * completes exceptionally.
     */
    public CompletableFuture<LinkResult> link(UUID playerUuid, String playerName, String code) {
        Objects.requireNonNull(playerUuid, "playerUuid");
        Objects.requireNonNull(playerName, "playerName");
        Objects.requireNonNull(code, "code");
        if (closed) {
            return CompletableFuture.completedFuture(LinkResult.failed(LinkResult.Outcome.FAILED, "not_running"));
        }
        try {
            return CompletableFuture.supplyAsync(() -> {
                try {
                    return Messages.linkReply(
                            transport.call("/link", Messages.link(code, playerUuid, playerName), CALL_TIMEOUT));
                } catch (ApiException e) {
                    return Messages.linkError(e);
                } catch (InterruptedException e) {
                    Thread.currentThread().interrupt();
                    return LinkResult.failed(LinkResult.Outcome.FAILED, "interrupted");
                } catch (IOException | RuntimeException e) {
                    log.warn("Couldn't reach Ticket Tool to link " + playerName + ": " + rootMessage(e), null);
                    return LinkResult.failed(LinkResult.Outcome.FAILED, "network");
                }
            }, workers);
        } catch (java.util.concurrent.RejectedExecutionException e) {
            return CompletableFuture.completedFuture(LinkResult.failed(LinkResult.Outcome.FAILED, "not_running"));
        }
    }

    /**
     * Sends an in-game report. Pass the UUID and name of the player who ran the
     * command, never values from its arguments. Never completes exceptionally.
     *
     * @param targetName the player the report is about, when the command names one; may be null
     * @param server     backend or world name; may be null
     */
    public CompletableFuture<ReportResult> report(UUID playerUuid, String playerName, String message,
            String targetName, String server) {
        Objects.requireNonNull(playerUuid, "playerUuid");
        Objects.requireNonNull(playerName, "playerName");
        Objects.requireNonNull(message, "message");
        if (closed) {
            return CompletableFuture.completedFuture(ReportResult.failed(ReportResult.Outcome.FAILED, "not_running"));
        }
        if (!reportsEnabled.getAsBoolean()) {
            return CompletableFuture.completedFuture(
                    ReportResult.failed(ReportResult.Outcome.NOT_ENABLED, "not_enabled"));
        }
        if (message.isBlank()) {
            return CompletableFuture.completedFuture(ReportResult.failed(ReportResult.Outcome.FAILED, "empty_message"));
        }
        try {
            return CompletableFuture.supplyAsync(() -> {
                try {
                    return Messages.reportReply(transport.call("/event",
                            Messages.report(playerUuid, playerName, message, targetName, server), CALL_TIMEOUT));
                } catch (ApiException e) {
                    return Messages.reportError(e);
                } catch (InterruptedException e) {
                    Thread.currentThread().interrupt();
                    return ReportResult.failed(ReportResult.Outcome.FAILED, "interrupted");
                } catch (IOException | RuntimeException e) {
                    log.warn("Couldn't reach Ticket Tool to send " + playerName + "'s report: " + rootMessage(e), null);
                    return ReportResult.failed(ReportResult.Outcome.FAILED, "network");
                }
            }, workers);
        } catch (java.util.concurrent.RejectedExecutionException e) {
            return CompletableFuture.completedFuture(ReportResult.failed(ReportResult.Outcome.FAILED, "not_running"));
        }
    }

    /** Sends {@code /hello} again so the dashboard sees changed actions or reports, e.g. after a reload. */
    public void refreshHello() {
        needHello = true;
    }

    private void runLoop() {
        needHello = true;
        while (running) {
            try {
                if (needHello) {
                    HelloReply reply = Messages.helloReply(transport.call("/hello",
                            Messages.hello(plugin, banProviders.get(),
                                    actionHandler == null ? List.of() : actions.get(),
                                    reportsEnabled.getAsBoolean()),
                            CALL_TIMEOUT));
                    hello = reply;
                    needHello = false;
                    everConnected = true;
                    log.info("Connected to Ticket Tool as \"" + reply.keyName() + "\" for Discord server "
                            + (reply.serverName() == null ? reply.serverId() : reply.serverName()) + " (" + key + ")");
                }
                Duration pollTimeout = Duration.ofSeconds(Math.max(35, hello.pollWaitSeconds() + 10));
                lastPollAt = clock.instant();
                state = ClientStatus.State.CONNECTED;
                Map<String, Object> body = transport.call("/poll", Map.of(), pollTimeout);
                recovered();
                for (LookupRequest request : Messages.pollRequests(body)) {
                    dispatch(request);
                }
                for (ActionRequest action : Messages.pollActions(body)) {
                    dispatchAction(action);
                }
            } catch (InterruptedException e) {
                break;
            } catch (ApiException e) {
                if (!running) {
                    break;
                }
                if (handleApiError(e)) {
                    needHello = true;
                }
            } catch (HttpTimeoutException e) {
                if (!running) {
                    break;
                }
                failed("Ticket Tool didn't answer in time", e, backoff.next());
            } catch (IOException | RuntimeException e) {
                if (!running) {
                    break;
                }
                failed("Can't reach Ticket Tool: " + rootMessage(e), e, backoff.next());
            }
        }
    }

    /** Returns true when the next attempt should start over with {@code /hello}. */
    private boolean handleApiError(ApiException e) {
        String error = e.error() == null ? "http_" + e.status() : e.error();
        switch (error) {
            case "invalid_key" -> fatal(ClientStatus.State.STOPPED_INVALID_KEY,
                    "Ticket Tool doesn't recognize key " + key
                            + ". It was revoked, rotated or mistyped: paste the current key from the dashboard and reload.");
            case "wrong_plugin_type" -> fatal(ClientStatus.State.STOPPED_ERROR,
                    "Key " + key + " was made for a different game (" + e.body().getOrDefault("expected", "?")
                            + "). Create a " + plugin.type() + " key on the dashboard.");
            case "unsupported_protocol" -> fatal(ClientStatus.State.STOPPED_ERROR,
                    "Ticket Tool no longer serves protocol " + PROTOCOL_VERSION + ". Update the plugin.");
            case "plan_not_entitled" -> {
                state = ClientStatus.State.PAUSED_PLAN;
                note("The Discord server's plan doesn't include game lookups. Checking again in an hour.", null);
                pause(PLAN_RETRY);
                return true;
            }
            case "rate_limited" -> failed("Ticket Tool is rate limiting this key; waiting a minute", null,
                    RATE_LIMIT_WAIT.plusMillis(random.nextLong(5000)));
            case "too_many_polls" -> {
                Duration wait = backoff.next();
                failed("Three polls are already open for key " + key
                        + ". Is the same key in use on another server? Each server needs its own key.", null,
                        wait.compareTo(TOO_MANY_POLLS_MIN_WAIT) < 0
                                ? TOO_MANY_POLLS_MIN_WAIT.plusMillis(random.nextLong(5000))
                                : wait);
            }
            case "bad_signature", "missing_headers", "bad_nonce" -> failed(
                    "Ticket Tool rejected the request signature (" + error + "). Check the key was pasted whole.",
                    null, backoff.next());
            default -> failed("Ticket Tool returned " + error + " (HTTP " + e.status() + ")", null, backoff.next());
        }
        return false;
    }

    private void dispatch(LookupRequest request) {
        if (ActionRequest.TYPE_PLAYER_ACTION.equals(request.type())) {
            return;
        }
        if (!LookupRequest.TYPE_PLAYER_LOOKUP.equals(request.type())) {
            log.debug("Ignoring request type " + request.type());
            return;
        }
        Instant received = serverNow();
        if (!request.expiresAt().isAfter(received)) {
            log.debug("Dropping expired request " + request.id());
            return;
        }
        workers.execute(() -> answer(request, received));
    }

    private void answer(LookupRequest request, Instant received) {
        if (request.playerUuid() == null || request.include().isEmpty()) {
            respond(request, LookupResult.failure(LookupResult.UNSUPPORTED));
            return;
        }
        Instant budgetEnd = received.plus(answerBudget);
        Instant deadline = budgetEnd.isBefore(request.expiresAt()) ? budgetEnd : request.expiresAt();
        long remaining = Math.max(1, Duration.between(received, deadline).toMillis());

        CompletableFuture<LookupResult> future;
        try {
            future = handler.handle(request);
            if (future == null) {
                future = CompletableFuture.completedFuture(null);
            }
        } catch (RuntimeException e) {
            future = CompletableFuture.failedFuture(e);
        }
        future.orTimeout(remaining, TimeUnit.MILLISECONDS).whenCompleteAsync((result, error) -> {
            LookupResult out = result;
            if (error != null) {
                Throwable cause = error instanceof CompletionException && error.getCause() != null
                        ? error.getCause()
                        : error;
                if (cause instanceof TimeoutException) {
                    out = LookupResult.failure(LookupResult.ERROR, "The game server took too long to answer");
                } else {
                    log.warn("Lookup " + request.id() + " failed", cause);
                    out = LookupResult.failure(LookupResult.ERROR, "The plugin hit an error answering this lookup");
                }
            } else if (out == null) {
                out = LookupResult.failure(LookupResult.ERROR, "The plugin had no answer");
            }
            if (!serverNow().isBefore(request.expiresAt())) {
                log.debug("Answer for " + request.id() + " is too late; not sending it");
                return;
            }
            respond(request, out);
        }, workers);
    }

    private void dispatchAction(ActionRequest request) {
        Instant received = serverNow();
        if (!request.expiresAt().isAfter(received)) {
            log.debug("Dropping expired action " + request.id());
            return;
        }
        workers.execute(() -> act(request, received));
    }

    private void act(ActionRequest request, Instant received) {
        if (actionHandler == null || !actions.get().contains(request.kind())) {
            log.info("Refused a " + request.kind() + " from " + request.actorName()
                    + ": that action is off in the plugin's config");
            respondAction(request, ActionResult.failure(ActionResult.NOT_ALLOWED));
            return;
        }
        if (request.playerUuid() == null) {
            respondAction(request, ActionResult.failure(ActionResult.UNKNOWN_PLAYER));
            return;
        }
        Instant budgetEnd = received.plus(ACTION_BUDGET);
        Instant deadline = budgetEnd.isBefore(request.expiresAt()) ? budgetEnd : request.expiresAt();
        long remaining = Math.max(1, Duration.between(received, deadline).toMillis());
        CompletableFuture<ActionResult> future;
        try {
            future = actionHandler.handle(request);
            if (future == null) {
                future = CompletableFuture.completedFuture(null);
            }
        } catch (RuntimeException e) {
            future = CompletableFuture.failedFuture(e);
        }
        future.orTimeout(remaining, TimeUnit.MILLISECONDS).whenCompleteAsync((result, error) -> {
            ActionResult out = result;
            if (error != null) {
                Throwable cause = error instanceof CompletionException && error.getCause() != null
                        ? error.getCause()
                        : error;
                if (cause instanceof TimeoutException) {
                    out = ActionResult.failure(ActionResult.ERROR, "The game server took too long to carry it out");
                } else {
                    log.warn("Action " + request.id() + " failed", cause);
                    out = ActionResult.failure(ActionResult.ERROR, "The plugin hit an error carrying out this action");
                }
            } else if (out == null) {
                out = ActionResult.failure(ActionResult.ERROR, "The plugin had no answer");
            }
            // Unlike a lookup, a late action already happened in game, so staff still hear about it.
            respondAction(request, out);
        }, workers);
    }

    private void respondAction(ActionRequest request, ActionResult result) {
        try {
            transport.call("/respond", Messages.respondAction(request, result), CALL_TIMEOUT);
            answered.incrementAndGet();
        } catch (ApiException e) {
            if ("unknown_request".equals(e.error())) {
                log.debug("Action " + request.id() + " expired before the answer arrived");
            } else {
                log.warn("Ticket Tool refused the result of action " + request.id() + ": " + e.getMessage(), null);
            }
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        } catch (IOException | RuntimeException e) {
            log.warn("Couldn't send the result of action " + request.id() + ": " + rootMessage(e), null);
        }
    }

    private void respond(LookupRequest request, LookupResult result) {
        try {
            transport.call("/respond", Messages.respond(request, result), CALL_TIMEOUT);
            answered.incrementAndGet();
        } catch (ApiException e) {
            if ("unknown_request".equals(e.error())) {
                log.debug("Request " + request.id() + " expired before the answer arrived");
            } else {
                log.warn("Ticket Tool refused the answer to " + request.id() + ": " + e.getMessage(), null);
            }
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        } catch (IOException | RuntimeException e) {
            log.warn("Couldn't send the answer to " + request.id() + ": " + rootMessage(e), null);
        }
    }

    private Instant serverNow() {
        return clock.instant().plusSeconds(transport.offsetSeconds());
    }

    private void recovered() {
        if (failureStreak > 0) {
            log.info("Reconnected to Ticket Tool");
        }
        failureStreak = 0;
        backoff.reset();
    }

    /** Logs the first failure of a streak as a warning and the rest at debug, then waits. */
    private void failed(String message, Throwable error, Duration wait) {
        state = ClientStatus.State.RECONNECTING;
        note(message, error);
        log.debug("Retrying in " + wait.toMillis() + " ms");
        pause(wait);
    }

    private void note(String message, Throwable error) {
        lastError = message;
        lastErrorAt = clock.instant();
        if (failureStreak++ == 0) {
            log.warn(message, error);
        } else {
            log.debug(message);
        }
    }

    private void fatal(ClientStatus.State stopState, String message) {
        lastError = message;
        lastErrorAt = clock.instant();
        state = stopState;
        running = false;
        log.warn(message, null);
    }

    private void pause(Duration wait) {
        try {
            sleeper.sleep(wait);
        } catch (InterruptedException e) {
            running = false;
            Thread.currentThread().interrupt();
        }
    }

    private static String rootMessage(Throwable e) {
        Throwable t = e;
        while (t.getCause() != null && t.getCause() != t) {
            t = t.getCause();
        }
        String m = t.getMessage();
        return m == null || m.isBlank() ? t.getClass().getSimpleName() : m;
    }

    public static final class Builder {
        private String key;
        private String apiUrl = DEFAULT_API_URL;
        private PluginInfo plugin;
        private RequestHandler handler;
        private Supplier<List<String>> banProviders = List::of;
        private ActionHandler actionHandler;
        private Supplier<List<String>> actions = List::of;
        private java.util.function.BooleanSupplier reportsEnabled = () -> false;
        private SdkLogger logger = SdkLogger.NONE;
        private InstantSource clock = InstantSource.system();
        private Sleeper sleeper = Sleeper.SYSTEM;
        // Not RandomGenerator.getDefault(): it goes through ServiceLoader, which finds no
        // providers from inside some plugin class loaders (Paper's, for one).
        private RandomGenerator random = new java.util.Random();
        private Duration answerBudget = Duration.ofSeconds(3);

        private Builder() {}

        /** The full integration key; only the derived signing secret is kept. */
        public Builder key(String key) {
            this.key = key;
            return this;
        }

        /** Host root such as {@code https://dash.ticket-tool.app}, or a base ending in {@code /api/plugin/v1}. */
        public Builder apiUrl(String apiUrl) {
            this.apiUrl = apiUrl;
            return this;
        }

        public Builder plugin(PluginInfo plugin) {
            this.plugin = plugin;
            return this;
        }

        public Builder handler(RequestHandler handler) {
            this.handler = handler;
            return this;
        }

        /** Read at each {@code /hello}, e.g. {@code ["litebans"]}. */
        public Builder banProviders(Supplier<List<String>> banProviders) {
            this.banProviders = Objects.requireNonNull(banProviders);
            return this;
        }

        /** Carries out {@code player.action} requests. Without it every action is answered {@code not_allowed}. */
        public Builder actionHandler(ActionHandler actionHandler) {
            this.actionHandler = actionHandler;
            return this;
        }

        /**
         * The actions the server owner turned on, read at each {@code /hello} and
         * checked for every request; anything else is answered {@code not_allowed}.
         */
        public Builder actions(Supplier<List<String>> actions) {
            this.actions = Objects.requireNonNull(actions);
            return this;
        }

        /** Whether in-game reports are on, read at each {@code /hello} and before each report. */
        public Builder reportsEnabled(java.util.function.BooleanSupplier reportsEnabled) {
            this.reportsEnabled = Objects.requireNonNull(reportsEnabled);
            return this;
        }

        public Builder logger(SdkLogger logger) {
            this.logger = Objects.requireNonNull(logger);
            return this;
        }

        /** Time allowed per answer before a timeout error is sent instead; capped by the request's expiry. */
        public Builder answerBudget(Duration answerBudget) {
            this.answerBudget = Objects.requireNonNull(answerBudget);
            return this;
        }

        public Builder clock(InstantSource clock) {
            this.clock = Objects.requireNonNull(clock);
            return this;
        }

        public Builder sleeper(Sleeper sleeper) {
            this.sleeper = Objects.requireNonNull(sleeper);
            return this;
        }

        public Builder random(RandomGenerator random) {
            this.random = Objects.requireNonNull(random);
            return this;
        }

        public TicketToolClient build() {
            return new TicketToolClient(this);
        }
    }
}
