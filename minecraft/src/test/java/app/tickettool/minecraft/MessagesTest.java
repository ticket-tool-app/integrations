package app.tickettool.minecraft;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import app.tickettool.sdk.ClientStatus;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Locale;
import java.util.Map;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import java.util.stream.Stream;
import org.bukkit.configuration.file.YamlConfiguration;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;

class MessagesTest {
    private static YamlConfiguration bundled;

    @BeforeAll
    static void load() throws IOException {
        try (InputStream in = MessagesTest.class.getResourceAsStream("/messages.yml")) {
            bundled = YamlConfiguration.loadConfiguration(new InputStreamReader(in, StandardCharsets.UTF_8));
        }
    }

    @Test
    void everyKeyTheCodeSendsExists() throws IOException {
        Pattern sent = Pattern.compile("\"((?:link|admin|report|action)\\.[a-z.-]+)\"");
        try (Stream<Path> files = Files.walk(Path.of("src/main/java"))) {
            for (Path file : files.filter(p -> p.toString().endsWith(".java")).toList()) {
                Matcher m = sent.matcher(Files.readString(file));
                while (m.find()) {
                    String key = m.group(1);
                    if (key.endsWith(".")) {
                        continue;
                    }
                    assertTrue(bundled.isString(key), file.getFileName() + " sends missing message " + key);
                }
            }
        }
        for (ClientStatus.State state : ClientStatus.State.values()) {
            String key = "admin.status.state." + state.name().toLowerCase(Locale.ROOT);
            assertTrue(bundled.isString(key), "missing " + key);
        }
    }

    @Test
    void fillsPlaceholdersAndColors() {
        ChatMessages messages = ChatMessages.of(bundled);
        assertEquals("§aLinked to Discord user §falex§a. You're all set!",
                messages.format("link.linked", Map.of("username", "alex")));
    }

    @Test
    void leavesColorCodesInStaffTextAsTyped() {
        ChatMessages messages = ChatMessages.of(bundled);
        assertEquals("§c§lWarning from staff: §r§c&4grief &kagain",
                messages.formatPlain("action.warn", Map.of("message", "&4grief §kagain", "actor", "mod")));
    }

    @Test
    void usesUsEnglish() {
        String all = bundled.saveToString().toLowerCase(Locale.ROOT);
        for (String british : new String[] {"colour", "recognise", "authorise", "licence", "behaviour", "cancelled"}) {
            assertTrue(!all.contains(british), "British spelling: " + british);
        }
    }
}
