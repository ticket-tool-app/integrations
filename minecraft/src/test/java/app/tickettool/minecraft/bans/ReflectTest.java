package app.tickettool.minecraft.bans;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.UUID;
import org.junit.jupiter.api.Test;

class ReflectTest {
    public interface Api {
        String getBan(UUID uuid, String ip, String server);

        long getEnd();
    }

    // Package-private on purpose: ban plugins hide their implementations the same way.
    static final class Hidden implements Api {
        @Override
        public String getBan(UUID uuid, String ip, String server) {
            return uuid + "|" + ip + "|" + server;
        }

        @Override
        public long getEnd() {
            return -1L;
        }
    }

    @Test
    void callsThroughThePublicInterfaceOfAHiddenClass() throws Exception {
        UUID id = UUID.randomUUID();
        assertEquals(id + "|null|null", Reflect.call(new Hidden(), "getBan", id, null, null));
        assertEquals(-1L, Reflect.call(new Hidden(), "getEnd"));
        assertTrue(Reflect.has(new Hidden(), "getBan", 3));
    }

    @Test
    void reportsMissingMethods() {
        assertThrows(NoSuchMethodException.class, () -> Reflect.call(new Hidden(), "getBan", "not-a-uuid", null, null));
        assertThrows(NoSuchMethodException.class, () -> Reflect.call(null, "getBan"));
    }
}
