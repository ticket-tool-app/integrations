using System.Text.RegularExpressions;

namespace TicketTool.PluginSdk
{
    /// <summary>String clean-up shared by the message types.</summary>
    public static class Text
    {
        // Unity rich-text tags (Rust chat and ban reasons often carry them).
        private static readonly Regex RichTextTags = new Regex(
            "</?(?:color|b|i|size|material|quad|mark|u|s|sub|sup|noparse)(?:=[^>]*)?>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Source chat colors are control characters (\x01 to \x10); newlines are kept.
        private static readonly Regex ControlChars = new Regex("[\\x00-\\x09\\x0B-\\x1F\\x7F]",
            RegexOptions.CultureInvariant);

        /// <summary>Clips by UTF-16 unit without ever cutting a surrogate pair in half.</summary>
        public static string Clip(string s, int maxChars)
        {
            if (s == null || s.Length <= maxChars) return s;
            int end = maxChars;
            if (end > 0 && char.IsHighSurrogate(s[end - 1])) end--;
            return s.Substring(0, end);
        }

        public static string StripFormatting(string s)
        {
            if (s == null) return null;
            return ControlChars.Replace(RichTextTags.Replace(s, ""), "");
        }

        /// <summary>Trimmed, or null when nothing is left.</summary>
        public static string EmptyToNull(string s)
        {
            if (s == null) return null;
            string t = s.Trim();
            return t.Length == 0 ? null : t;
        }

        /// <summary>Formatting stripped, trimmed, clipped; null when empty.</summary>
        public static string Clean(string s, int maxChars)
        {
            return Clip(EmptyToNull(StripFormatting(s)), maxChars);
        }
    }
}
