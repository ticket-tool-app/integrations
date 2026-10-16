package app.tickettool.sdk;

import java.util.Map;

/**
 * A reply with {@code ok: false} or a non-2xx status.
 *
 * @see #error() the protocol error code, e.g. {@code invalid_key}
 */
public final class ApiException extends Exception {
    private static final long serialVersionUID = 1L;

    private final int status;
    private final String error;
    private final transient Map<String, Object> body;

    public ApiException(int status, String error, Map<String, Object> body) {
        super("HTTP " + status + (error == null ? "" : " " + error));
        this.status = status;
        this.error = error;
        this.body = body == null ? Map.of() : body;
    }

    public int status() {
        return status;
    }

    /** May be null when the reply wasn't JSON, e.g. an HTML error page from a proxy. */
    public String error() {
        return error;
    }

    public Map<String, Object> body() {
        return body;
    }
}
