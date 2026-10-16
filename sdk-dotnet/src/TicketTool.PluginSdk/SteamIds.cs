using System.Globalization;
using System.Text.RegularExpressions;

namespace TicketTool.PluginSdk
{
    /// <summary>SteamID64 checks and conversion from the older text forms ban lists use.</summary>
    public static class SteamIds
    {
        private const ulong IndividualBase = 76561197960265728UL;
        private static readonly Regex Id64 = new Regex("^7656119[0-9]{10}$", RegexOptions.CultureInvariant);
        private static readonly Regex Legacy = new Regex("^STEAM_[0-5]:([01]):([0-9]{1,10})$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex Steam3 = new Regex("^\\[?U:1:([0-9]{1,10})\\]?$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>A 17-digit individual-account SteamID64, as Ticket Tool sends it.</summary>
        public static bool IsSteamId64(string s)
        {
            return s != null && Id64.IsMatch(s);
        }

        /// <summary>
        /// Accepts SteamID64, STEAM_X:Y:Z and [U:1:N]; returns the SteamID64 string or
        /// null when the text is none of those.
        /// </summary>
        public static string Normalize(string s)
        {
            if (s == null) return null;
            string t = s.Trim();
            if (IsSteamId64(t)) return t;
            Match m = Legacy.Match(t);
            ulong n;
            if (m.Success && ulong.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out n))
            {
                ulong y = m.Groups[1].Value == "1" ? 1UL : 0UL;
                return (IndividualBase + n * 2 + y).ToString(CultureInfo.InvariantCulture);
            }
            m = Steam3.Match(t);
            if (m.Success && ulong.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out n))
                return (IndividualBase + n).ToString(CultureInfo.InvariantCulture);
            return null;
        }
    }
}
