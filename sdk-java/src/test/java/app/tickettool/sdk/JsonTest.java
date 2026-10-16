package app.tickettool.sdk;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;

import app.tickettool.sdk.json.Json;
import app.tickettool.sdk.json.JsonException;
import java.util.Arrays;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

class JsonTest {
    @Test
    void parsesProtocolShapes() {
        Map<String, Object> m = Json.parseObject("""
                {"ok":true,"requests":[{"id":"x","include":["profile","bans"],"n":-12,"f":1.5e2,"z":null}]}
                """);
        assertEquals(true, m.get("ok"));
        @SuppressWarnings("unchecked")
        Map<String, Object> r = (Map<String, Object>) ((List<?>) m.get("requests")).get(0);
        assertEquals(List.of("profile", "bans"), r.get("include"));
        assertEquals(-12L, r.get("n"));
        assertEquals(150.0, r.get("f"));
        assertNull(r.get("z"));
    }

    @Test
    void roundTripsEscapes() {
        String tricky = "quote\" backslash\\ newline\n tab\t ctrl\u0001 line\u2028 emoji \uD83D\uDE00 §cred";
        Map<String, Object> in = new LinkedHashMap<>();
        in.put("s", tricky);
        String text = Json.write(in);
        assertEquals(tricky, Json.parseObject(text).get("s"));
        assertEquals("\u00e9\uD83D\uDE00", Json.parse("\"\\u00e9\\ud83d\\ude00\""));
    }

    @Test
    void writesCompactlyInInsertionOrder() {
        Map<String, Object> in = new LinkedHashMap<>();
        in.put("b", 1);
        in.put("a", Arrays.asList(true, null, 2.5, "x"));
        assertEquals("{\"b\":1,\"a\":[true,null,2.5,\"x\"]}", Json.write(in));
        assertEquals("{}", Json.write(Map.of()));
    }

    @Test
    void rejectsMalformedInput() {
        for (String bad : new String[] {"", "{", "{\"a\"}", "[1,]", "{\"a\":1,}", "01", "\"\\x\"", "tru", "{} x",
                "\"a\nb\"", "-", "1."}) {
            assertThrows(JsonException.class, () -> Json.parse(bad), bad);
        }
    }

    @Test
    void refusesDeepNesting() {
        String deep = "[".repeat(500) + "]".repeat(500);
        assertThrows(JsonException.class, () -> Json.parse(deep));
    }
}
