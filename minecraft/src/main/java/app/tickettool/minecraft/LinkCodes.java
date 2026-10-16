package app.tickettool.minecraft;

import java.util.Locale;

/** The server's link-code rules, so a typo gets a helpful message without a round trip. */
final class LinkCodes {
    /** No 0/O, 1/I/L or U. Must match the server's alphabet. */
    static final String ALPHABET = "ABCDEFGHJKMNPQRSTVWXYZ23456789";
    static final int LENGTH = 8;

    private LinkCodes() {}

    static boolean plausible(String typed) {
        String code = typed.toUpperCase(Locale.ROOT).replaceAll("[\\s-]", "");
        if (code.length() != LENGTH) {
            return false;
        }
        for (char c : code.toCharArray()) {
            if (ALPHABET.indexOf(c) < 0) {
                return false;
            }
        }
        return true;
    }
}
