using System;
using System.Text.RegularExpressions;

namespace TicketTool.PluginSdk
{
    /// <summary>
    /// An integration key (ttp_&lt;16 hex&gt;.&lt;43 base64url&gt;). Only the derived
    /// signing secret is kept, and ToString shows the public part alone so a key
    /// can't leak into logs.
    /// </summary>
    public sealed class PluginKey
    {
        private static readonly Regex Pattern = new Regex("^ttp_([0-9a-f]{16})\\.([A-Za-z0-9_-]{43})$",
            RegexOptions.CultureInvariant);

        private PluginKey(string keyId, Signer signer)
        {
            KeyId = keyId;
            Signer = signer;
        }

        public string KeyId { get; private set; }

        internal Signer Signer { get; private set; }

        /// <summary>What may be shown or logged: ttp_&lt;keyId&gt;.</summary>
        public string DisplayPrefix
        {
            get { return "ttp_" + KeyId; }
        }

        /// <exception cref="ArgumentException">The text isn't a key; the message never repeats it.</exception>
        public static PluginKey Parse(string key)
        {
            if (key == null || key.Trim().Length == 0) throw new ArgumentException("No integration key set");
            string trimmed = key.Trim();
            Match m = Pattern.Match(trimmed);
            if (!m.Success)
                throw new ArgumentException(
                    "That isn't an integration key. Keys look like ttp_0123456789abcdef.<43 characters>");
            return new PluginKey(m.Groups[1].Value, new Signer(Signer.DeriveSigningSecret(trimmed)));
        }

        public static bool LooksLikeKey(string key)
        {
            return key != null && Pattern.IsMatch(key.Trim());
        }

        public override string ToString()
        {
            return DisplayPrefix;
        }

        public override bool Equals(object obj)
        {
            var other = obj as PluginKey;
            return other != null && other.KeyId == KeyId && other.Signer.SameSecret(Signer);
        }

        public override int GetHashCode()
        {
            return KeyId.GetHashCode();
        }
    }
}
