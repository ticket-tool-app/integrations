package app.tickettool.minecraft;

/** {@code &}-codes to section-sign codes; works on Spigot and Paper alike. */
final class Colors {
    private static final String CODES = "0123456789AaBbCcDdEeFfKkLlMmNnOoRrXx";

    private Colors() {}

    static String translate(String text) {
        char[] chars = text.toCharArray();
        for (int i = 0; i < chars.length - 1; i++) {
            if (chars[i] == '&' && CODES.indexOf(chars[i + 1]) >= 0) {
                chars[i] = '§';
                chars[i + 1] = Character.toLowerCase(chars[i + 1]);
            }
        }
        return new String(chars);
    }
}
