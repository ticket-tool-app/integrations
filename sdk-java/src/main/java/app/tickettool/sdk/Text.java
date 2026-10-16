package app.tickettool.sdk;

import java.util.regex.Pattern;

/** String clean-up shared by the message types. */
public final class Text {
    // Section-sign codes plus the '&' codes most plugins accept in config, including &#rrggbb and &x&r&r&g&g&b&b hex forms.
    private static final Pattern COLOR_CODES = Pattern.compile("(?i)(?:[§&]x(?:[§&][0-9a-f]){6}|[§&]#[0-9a-f]{6}|[§&][0-9a-fk-or])");

    private Text() {}

    /** Clips by code point so a surrogate pair is never cut in half. */
    public static String clip(String s, int maxChars) {
        if (s == null || s.length() <= maxChars) {
            return s;
        }
        int end = maxChars;
        if (Character.isHighSurrogate(s.charAt(end - 1))) {
            end--;
        }
        return s.substring(0, end);
    }

    public static String stripColorCodes(String s) {
        return s == null ? null : COLOR_CODES.matcher(s).replaceAll("");
    }

    /** Trimmed, or null when nothing is left. */
    public static String emptyToNull(String s) {
        if (s == null) {
            return null;
        }
        String t = s.trim();
        return t.isEmpty() ? null : t;
    }
}
