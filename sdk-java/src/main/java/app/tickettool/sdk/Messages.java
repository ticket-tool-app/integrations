package app.tickettool.sdk;

import app.tickettool.sdk.json.JsonFields;
import java.time.Instant;
import java.time.format.DateTimeParseException;
import java.time.temporal.ChronoUnit;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.UUID;

/** Protocol v1 bodies to and from plain maps. */
final class Messages {
    static final Set<String> KNOWN_PARTS = Set.of(LookupRequest.PART_PROFILE, LookupRequest.PART_BANS);

    private Messages() {}

    static final int MAX_ACTION_MESSAGE = 500;
    static final int MAX_REPORT_MESSAGE = 1000;
    static final String REPORT_EVENT = "player.report";

    static Map<String, Object> hello(PluginInfo info, List<String> banProviders) {
        return hello(info, banProviders, List.of(), false);
    }

    static Map<String, Object> hello(PluginInfo info, List<String> banProviders, List<String> actions,
            boolean reports) {
        Map<String, Object> plugin = new LinkedHashMap<>();
        plugin.put("type", info.type());
        plugin.put("version", info.version());
        if (info.platform() != null) {
            plugin.put("platform", info.platform());
        }
        Map<String, Object> body = new LinkedHashMap<>();
        body.put("protocol", TicketToolClient.PROTOCOL_VERSION);
        body.put("plugin", plugin);
        if (banProviders != null && !banProviders.isEmpty()) {
            body.put("banProviders", banProviders.stream().limit(8).map(p -> Text.clip(p, 32)).toList());
        }
        List<String> on = ActionRequest.ORDER.stream().filter(k -> actions != null && actions.contains(k)).toList();
        if (!on.isEmpty()) {
            body.put("actions", on);
        }
        if (reports) {
            body.put("events", List.of(REPORT_EVENT));
        }
        return body;
    }

    static HelloReply helloReply(Map<String, Object> body) {
        Map<String, Object> key = JsonFields.object(body, "key");
        Map<String, Object> server = JsonFields.object(body, "server");
        Map<String, Object> poll = JsonFields.object(body, "poll");
        Long protocol = JsonFields.number(body, "protocol");
        Long wait = JsonFields.number(poll, "waitSeconds");
        return new HelloReply(
                protocol == null ? TicketToolClient.PROTOCOL_VERSION : protocol.intValue(),
                JsonFields.string(body, "pluginType"),
                JsonFields.string(key, "name"),
                JsonFields.string(server, "id"),
                JsonFields.string(server, "name"),
                wait == null ? 25 : (int) Math.max(1, Math.min(120, wait)));
    }

    /** Requests the SDK can act on; malformed entries are skipped, not fatal. */
    static List<LookupRequest> pollRequests(Map<String, Object> body) {
        List<LookupRequest> out = new ArrayList<>();
        for (Object item : JsonFields.list(body, "requests")) {
            if (!(item instanceof Map<?, ?>)) {
                continue;
            }
            @SuppressWarnings("unchecked")
            Map<String, Object> req = (Map<String, Object>) item;
            String id = JsonFields.string(req, "id");
            String type = JsonFields.string(req, "type");
            Instant expiresAt = instant(JsonFields.string(req, "expiresAt"));
            Map<String, Object> player = JsonFields.object(req, "player");
            UUID uuid = uuid(JsonFields.string(player, "uuid"));
            if (id == null || type == null || expiresAt == null) {
                continue;
            }
            Set<String> include = new LinkedHashSet<>();
            for (Object part : JsonFields.list(req, "include")) {
                if (part instanceof String s && KNOWN_PARTS.contains(s)) {
                    include.add(s);
                }
            }
            out.add(new LookupRequest(id, type, expiresAt, uuid, JsonFields.string(player, "name"),
                    Set.copyOf(include)));
        }
        return out;
    }

    /** {@code player.action} requests; malformed entries and unknown kinds are skipped. */
    static List<ActionRequest> pollActions(Map<String, Object> body) {
        List<ActionRequest> out = new ArrayList<>();
        for (Object item : JsonFields.list(body, "requests")) {
            if (!(item instanceof Map<?, ?>)) {
                continue;
            }
            @SuppressWarnings("unchecked")
            Map<String, Object> req = (Map<String, Object>) item;
            if (!ActionRequest.TYPE_PLAYER_ACTION.equals(JsonFields.string(req, "type"))) {
                continue;
            }
            String id = JsonFields.string(req, "id");
            Instant expiresAt = instant(JsonFields.string(req, "expiresAt"));
            Map<String, Object> action = JsonFields.object(req, "action");
            String kind = JsonFields.string(action, "kind");
            if (id == null || expiresAt == null || kind == null || !ActionRequest.KINDS.contains(kind)) {
                continue;
            }
            Map<String, Object> player = JsonFields.object(req, "player");
            String message = ActionRequest.UNBAN.equals(kind) ? null
                    : Text.clip(Text.emptyToNull(JsonFields.string(action, "message")), MAX_ACTION_MESSAGE);
            String actor = Text.clip(Text.emptyToNull(JsonFields.string(JsonFields.object(req, "actor"), "name")), 64);
            out.add(new ActionRequest(id, expiresAt, uuid(JsonFields.string(player, "uuid")),
                    JsonFields.string(player, "name"), kind, message, actor == null ? "Ticket Tool" : actor));
        }
        return out;
    }

    static Map<String, Object> respondAction(ActionRequest request, ActionResult result) {
        Map<String, Object> body = new LinkedHashMap<>();
        body.put("requestId", request.id());
        if (result instanceof ActionResult.Failure f) {
            body.put("ok", false);
            body.put("error", f.error());
            if (f.message() != null) {
                body.put("message", f.message());
            }
        } else {
            body.put("ok", true);
        }
        return body;
    }

    static Map<String, Object> report(UUID uuid, String name, String message, String targetName, String server) {
        Map<String, Object> player = new LinkedHashMap<>();
        player.put("uuid", uuid.toString());
        player.put("name", Text.clip(name, 64));
        Map<String, Object> body = new LinkedHashMap<>();
        body.put("type", REPORT_EVENT);
        body.put("player", player);
        body.put("message", Text.clip(message.trim(), MAX_REPORT_MESSAGE));
        String target = Text.emptyToNull(targetName);
        body.put("target", target == null ? null : Map.of("name", Text.clip(target, 64)));
        body.put("server", Text.clip(Text.emptyToNull(server), 64));
        return body;
    }

    static ReportResult reportReply(Map<String, Object> body) {
        return "opened".equals(JsonFields.string(body, "result"))
                ? new ReportResult(ReportResult.Outcome.OPENED, null)
                : new ReportResult(ReportResult.Outcome.IGNORED, null);
    }

    static ReportResult reportError(ApiException e) {
        String error = e.error() == null ? "http_" + e.status() : e.error();
        return ReportResult.failed("rate_limited".equals(error)
                ? ReportResult.Outcome.RATE_LIMITED
                : ReportResult.Outcome.FAILED, error);
    }

    static Map<String, Object> respond(LookupRequest request, LookupResult result) {
        Map<String, Object> body = new LinkedHashMap<>();
        body.put("requestId", request.id());
        if (result instanceof LookupResult.Failure f) {
            body.put("ok", false);
            body.put("error", f.error());
            if (f.message() != null) {
                body.put("message", f.message());
            }
            return body;
        }
        LookupResult.Answer a = (LookupResult.Answer) result;
        body.put("ok", true);
        if (a.profile() != null && request.wantsProfile()) {
            body.put("profile", profile(a.profile()));
        }
        if (a.bans() != null && request.wantsBans()) {
            body.put("bans", bans(a.bans()));
        }
        return body;
    }

    static Map<String, Object> profile(PlayerProfile p) {
        Map<String, Object> m = new LinkedHashMap<>();
        m.put("uuid", p.uuid().toString());
        m.put("name", p.name());
        m.put("online", p.online());
        m.put("lastSeen", iso(p.lastSeen()));
        m.put("firstSeen", iso(p.firstSeen()));
        m.put("server", p.server());
        return m;
    }

    static Map<String, Object> bans(BanInfo b) {
        Map<String, Object> m = new LinkedHashMap<>();
        m.put("banned", b.banned());
        m.put("provider", b.provider());
        m.put("reason", b.banned() ? b.reason() : null);
        m.put("createdAt", b.banned() ? iso(b.createdAt()) : null);
        m.put("expiresAt", b.banned() ? iso(b.expiresAt()) : null);
        m.put("actor", b.banned() ? b.actor() : null);
        if (b.banned() && b.activeCount() != null && b.activeCount() > 1) {
            m.put("activeCount", b.activeCount());
        }
        return m;
    }

    static Map<String, Object> link(String code, UUID uuid, String name) {
        Map<String, Object> player = new LinkedHashMap<>();
        player.put("uuid", uuid.toString());
        player.put("name", name);
        Map<String, Object> body = new LinkedHashMap<>();
        body.put("code", Text.clip(code.trim(), 32));
        body.put("player", player);
        return body;
    }

    static LinkResult linkReply(Map<String, Object> body) {
        String result = JsonFields.string(body, "result");
        LinkResult.Outcome outcome = "relinked".equals(result) ? LinkResult.Outcome.RELINKED : LinkResult.Outcome.LINKED;
        return new LinkResult(outcome, JsonFields.string(JsonFields.object(body, "discord"), "username"),
                JsonFields.string(JsonFields.object(body, "player"), "name"), null);
    }

    static LinkResult linkError(ApiException e) {
        String error = e.error() == null ? "http_" + e.status() : e.error();
        LinkResult.Outcome outcome = switch (error) {
            case "invalid_code" -> LinkResult.Outcome.INVALID_CODE;
            case "unknown_player" -> LinkResult.Outcome.UNKNOWN_PLAYER;
            case "unavailable" -> Boolean.TRUE.equals(JsonFields.bool(e.body(), "retryable"))
                    ? LinkResult.Outcome.UNAVAILABLE
                    : LinkResult.Outcome.FAILED;
            case "rate_limited" -> LinkResult.Outcome.RATE_LIMITED;
            default -> LinkResult.Outcome.FAILED;
        };
        return LinkResult.failed(outcome, error);
    }

    static String iso(Instant t) {
        return t == null ? null : t.truncatedTo(ChronoUnit.MILLIS).toString();
    }

    static Instant instant(String s) {
        if (s == null) {
            return null;
        }
        try {
            return Instant.parse(s);
        } catch (DateTimeParseException e) {
            return null;
        }
    }

    /** Accepts dashed or undashed forms, like the server. */
    static UUID uuid(String s) {
        if (s == null) {
            return null;
        }
        String hex = s.trim().toLowerCase(Locale.ROOT).replace("-", "");
        if (!hex.matches("[0-9a-f]{32}")) {
            return null;
        }
        return new UUID(Long.parseUnsignedLong(hex.substring(0, 16), 16), Long.parseUnsignedLong(hex.substring(16), 16));
    }
}
