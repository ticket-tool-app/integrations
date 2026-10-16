package app.tickettool.sdk.json;

import java.math.BigDecimal;
import java.util.ArrayList;
import java.util.Collection;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * A small JSON reader and writer, so the SDK has no runtime dependencies and
 * can't clash with whichever Gson or Jackson the host platform ships.
 *
 * <p>Parsed values are {@code Map<String, Object>} (insertion ordered),
 * {@code List<Object>}, {@code String}, {@code Long} or {@code Double},
 * {@code Boolean} and {@code null}.
 */
public final class Json {
    /** Deeper than any protocol message; stops a hostile reply from blowing the stack. */
    private static final int MAX_DEPTH = 64;

    private Json() {}

    public static Object parse(String text) {
        Parser p = new Parser(text);
        p.skipWhitespace();
        Object value = p.readValue(0);
        p.skipWhitespace();
        if (p.pos != text.length()) {
            throw p.error("Unexpected trailing characters");
        }
        return value;
    }

    /** Parses text that must be a JSON object. */
    @SuppressWarnings("unchecked")
    public static Map<String, Object> parseObject(String text) {
        Object value = parse(text);
        if (!(value instanceof Map)) {
            throw new JsonException("Expected a JSON object");
        }
        return (Map<String, Object>) value;
    }

    public static String write(Object value) {
        StringBuilder sb = new StringBuilder();
        writeValue(sb, value, 0);
        return sb.toString();
    }

    private static void writeValue(StringBuilder sb, Object value, int depth) {
        if (depth > MAX_DEPTH) {
            throw new JsonException("Value nested too deeply");
        }
        if (value == null) {
            sb.append("null");
        } else if (value instanceof String s) {
            writeString(sb, s);
        } else if (value instanceof Boolean b) {
            sb.append(b ? "true" : "false");
        } else if (value instanceof Double || value instanceof Float) {
            double d = ((Number) value).doubleValue();
            if (Double.isNaN(d) || Double.isInfinite(d)) {
                throw new JsonException("JSON can't represent " + d);
            }
            if (d == Math.rint(d) && Math.abs(d) < 1e15) {
                sb.append((long) d);
            } else {
                sb.append(d);
            }
        } else if (value instanceof BigDecimal bd) {
            sb.append(bd.toPlainString());
        } else if (value instanceof Number n) {
            sb.append(n.longValue());
        } else if (value instanceof Map<?, ?> map) {
            sb.append('{');
            boolean first = true;
            for (Map.Entry<?, ?> e : map.entrySet()) {
                if (!first) {
                    sb.append(',');
                }
                first = false;
                writeString(sb, String.valueOf(e.getKey()));
                sb.append(':');
                writeValue(sb, e.getValue(), depth + 1);
            }
            sb.append('}');
        } else if (value instanceof Collection<?> list) {
            sb.append('[');
            boolean first = true;
            for (Object item : list) {
                if (!first) {
                    sb.append(',');
                }
                first = false;
                writeValue(sb, item, depth + 1);
            }
            sb.append(']');
        } else {
            throw new JsonException("Can't write " + value.getClass().getName() + " as JSON");
        }
    }

    private static void writeString(StringBuilder sb, String s) {
        sb.append('"');
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            switch (c) {
                case '"' -> sb.append("\\\"");
                case '\\' -> sb.append("\\\\");
                case '\n' -> sb.append("\\n");
                case '\r' -> sb.append("\\r");
                case '\t' -> sb.append("\\t");
                case '\b' -> sb.append("\\b");
                case '\f' -> sb.append("\\f");
                default -> {
                    // U+2028/2029 are valid JSON but break some JavaScript consumers.
                    if (c < 0x20 || c == ' ' || c == ' ') {
                        sb.append(String.format("\\u%04x", (int) c));
                    } else {
                        sb.append(c);
                    }
                }
            }
        }
        sb.append('"');
    }

    private static final class Parser {
        private final String s;
        private int pos;

        Parser(String s) {
            this.s = s;
        }

        JsonException error(String message) {
            return new JsonException(message + " at position " + pos);
        }

        void skipWhitespace() {
            while (pos < s.length()) {
                char c = s.charAt(pos);
                if (c == ' ' || c == '\n' || c == '\r' || c == '\t') {
                    pos++;
                } else {
                    break;
                }
            }
        }

        Object readValue(int depth) {
            if (depth > MAX_DEPTH) {
                throw error("Value nested too deeply");
            }
            if (pos >= s.length()) {
                throw error("Unexpected end of input");
            }
            char c = s.charAt(pos);
            return switch (c) {
                case '{' -> readObject(depth);
                case '[' -> readArray(depth);
                case '"' -> readString();
                case 't' -> literal("true", Boolean.TRUE);
                case 'f' -> literal("false", Boolean.FALSE);
                case 'n' -> literal("null", null);
                default -> {
                    if (c == '-' || (c >= '0' && c <= '9')) {
                        yield readNumber();
                    }
                    throw error("Unexpected character '" + c + "'");
                }
            };
        }

        private Object literal(String word, Object value) {
            if (!s.startsWith(word, pos)) {
                throw error("Expected " + word);
            }
            pos += word.length();
            return value;
        }

        private Map<String, Object> readObject(int depth) {
            Map<String, Object> map = new LinkedHashMap<>();
            pos++;
            skipWhitespace();
            if (peek() == '}') {
                pos++;
                return map;
            }
            while (true) {
                skipWhitespace();
                if (peek() != '"') {
                    throw error("Expected a property name");
                }
                String key = readString();
                skipWhitespace();
                expect(':');
                skipWhitespace();
                map.put(key, readValue(depth + 1));
                skipWhitespace();
                char c = next();
                if (c == '}') {
                    return map;
                }
                if (c != ',') {
                    pos--;
                    throw error("Expected ',' or '}'");
                }
            }
        }

        private List<Object> readArray(int depth) {
            List<Object> list = new ArrayList<>();
            pos++;
            skipWhitespace();
            if (peek() == ']') {
                pos++;
                return list;
            }
            while (true) {
                skipWhitespace();
                list.add(readValue(depth + 1));
                skipWhitespace();
                char c = next();
                if (c == ']') {
                    return list;
                }
                if (c != ',') {
                    pos--;
                    throw error("Expected ',' or ']'");
                }
            }
        }

        private String readString() {
            expect('"');
            StringBuilder sb = new StringBuilder();
            while (true) {
                if (pos >= s.length()) {
                    throw error("Unterminated string");
                }
                char c = s.charAt(pos++);
                if (c == '"') {
                    return sb.toString();
                }
                if (c < 0x20) {
                    pos--;
                    throw error("Control character in string");
                }
                if (c != '\\') {
                    sb.append(c);
                    continue;
                }
                char esc = next();
                switch (esc) {
                    case '"' -> sb.append('"');
                    case '\\' -> sb.append('\\');
                    case '/' -> sb.append('/');
                    case 'b' -> sb.append('\b');
                    case 'f' -> sb.append('\f');
                    case 'n' -> sb.append('\n');
                    case 'r' -> sb.append('\r');
                    case 't' -> sb.append('\t');
                    case 'u' -> {
                        if (pos + 4 > s.length()) {
                            throw error("Truncated unicode escape");
                        }
                        try {
                            sb.append((char) Integer.parseInt(s.substring(pos, pos + 4), 16));
                        } catch (NumberFormatException e) {
                            throw error("Bad unicode escape");
                        }
                        pos += 4;
                    }
                    default -> {
                        pos--;
                        throw error("Bad escape '\\" + esc + "'");
                    }
                }
            }
        }

        private Object readNumber() {
            int start = pos;
            if (peek() == '-') {
                pos++;
            }
            if (peek() == '0') {
                pos++;
            } else if (isDigit(peek())) {
                while (isDigit(peek())) {
                    pos++;
                }
            } else {
                throw error("Bad number");
            }
            boolean integral = true;
            if (peek() == '.') {
                integral = false;
                pos++;
                if (!isDigit(peek())) {
                    throw error("Bad number");
                }
                while (isDigit(peek())) {
                    pos++;
                }
            }
            if (peek() == 'e' || peek() == 'E') {
                integral = false;
                pos++;
                if (peek() == '+' || peek() == '-') {
                    pos++;
                }
                if (!isDigit(peek())) {
                    throw error("Bad number");
                }
                while (isDigit(peek())) {
                    pos++;
                }
            }
            String text = s.substring(start, pos);
            if (integral) {
                try {
                    return Long.parseLong(text);
                } catch (NumberFormatException overflow) {
                    return Double.parseDouble(text);
                }
            }
            return Double.parseDouble(text);
        }

        private static boolean isDigit(char c) {
            return c >= '0' && c <= '9';
        }

        private char peek() {
            return pos < s.length() ? s.charAt(pos) : '\0';
        }

        private char next() {
            if (pos >= s.length()) {
                throw error("Unexpected end of input");
            }
            return s.charAt(pos++);
        }

        private void expect(char c) {
            if (next() != c) {
                pos--;
                throw error("Expected '" + c + "'");
            }
        }
    }
}
