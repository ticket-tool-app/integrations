package app.tickettool.sdk;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

class PluginKeyTest {
    private static final String KEY = FakeTicketTool.KEY;
    private static final String SECRET_PART = KEY.substring(KEY.indexOf('.') + 1);

    @Test
    void parsesTheKeyId() {
        PluginKey key = PluginKey.parse("  " + KEY + "\n");
        assertEquals("0123456789abcdef", key.keyId());
        assertEquals("ttp_0123456789abcdef", key.displayPrefix());
    }

    @Test
    void neverPrintsTheSecret() {
        PluginKey key = PluginKey.parse(KEY);
        assertFalse(key.toString().contains(SECRET_PART));
        assertFalse(key.signer().toString().contains("20998da3"));
    }

    @Test
    void rejectsMalformedKeysWithoutEchoingThem() {
        String bad = "ttp_0123456789ABCDEF." + SECRET_PART;
        IllegalArgumentException e = assertThrows(IllegalArgumentException.class, () -> PluginKey.parse(bad));
        assertFalse(e.getMessage().contains(SECRET_PART));
        assertThrows(IllegalArgumentException.class, () -> PluginKey.parse(null));
        assertThrows(IllegalArgumentException.class, () -> PluginKey.parse("ttp_0123456789abcdef"));
        assertThrows(IllegalArgumentException.class, () -> PluginKey.parse(KEY + "A"));
        assertTrue(PluginKey.looksLikeKey(KEY));
        assertFalse(PluginKey.looksLikeKey(""));
    }
}
