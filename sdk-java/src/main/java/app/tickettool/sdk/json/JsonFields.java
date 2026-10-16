package app.tickettool.sdk.json;

import java.util.List;
import java.util.Map;

/**
 * Lenient typed reads from a parsed object: a missing or mistyped field reads
 * as absent, because the protocol says unknown and malformed extras are ignored.
 */
public final class JsonFields {
    private JsonFields() {}

    public static String string(Map<String, Object> obj, String key) {
        return obj != null && obj.get(key) instanceof String s ? s : null;
    }

    public static Boolean bool(Map<String, Object> obj, String key) {
        return obj != null && obj.get(key) instanceof Boolean b ? b : null;
    }

    public static Long number(Map<String, Object> obj, String key) {
        if (obj == null) {
            return null;
        }
        Object v = obj.get(key);
        if (v instanceof Long l) {
            return l;
        }
        if (v instanceof Double d && d == Math.rint(d)) {
            return d.longValue();
        }
        return null;
    }

    @SuppressWarnings("unchecked")
    public static Map<String, Object> object(Map<String, Object> obj, String key) {
        return obj != null && obj.get(key) instanceof Map<?, ?> m ? (Map<String, Object>) m : null;
    }

    @SuppressWarnings("unchecked")
    public static List<Object> list(Map<String, Object> obj, String key) {
        return obj != null && obj.get(key) instanceof List<?> l ? (List<Object>) l : List.of();
    }
}
