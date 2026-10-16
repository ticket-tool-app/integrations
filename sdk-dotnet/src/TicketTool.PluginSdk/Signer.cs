using System.Security.Cryptography;
using System.Text;

namespace TicketTool.PluginSdk
{
    /// <summary>Request and reply signatures, protocol v1.</summary>
    public sealed class Signer
    {
        internal const string SigningContext = "ticket-tool plugin signing v1";

        // The HMAC key is the UTF-8 bytes of the hex secret, not the decoded bytes.
        private readonly byte[] _secret;

        public Signer(string signingSecretHex)
        {
            _secret = Encoding.UTF8.GetBytes(signingSecretHex);
        }

        public static string DeriveSigningSecret(string fullKey)
        {
            return Hex.Lower(Hmac(Encoding.UTF8.GetBytes(fullKey), Encoding.UTF8.GetBytes(SigningContext)));
        }

        public static string Sha256Hex(byte[] body)
        {
            using (var sha = SHA256.Create())
            {
                return Hex.Lower(sha.ComputeHash(body));
            }
        }

        public static string Canonical(string method, string endpoint, long timestamp, string nonce, byte[] body)
        {
            return string.Join("\n", "v1", method, endpoint,
                timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture), nonce, Sha256Hex(body));
        }

        public string SignRequest(string method, string endpoint, long timestamp, string nonce, byte[] body)
        {
            return Sign(Canonical(method, endpoint, timestamp, nonce, body));
        }

        public string SignReply(string endpoint, long timestamp, string nonce, byte[] body)
        {
            return Sign(Canonical("RESPONSE", endpoint, timestamp, nonce, body));
        }

        /// <summary>Constant-time, so a forger can't learn the signature a byte at a time.</summary>
        public bool VerifyReply(string endpoint, long timestamp, string nonce, byte[] body, string signature)
        {
            if (signature == null) return false;
            return FixedTimeEquals(Encoding.UTF8.GetBytes(SignReply(endpoint, timestamp, nonce, body)),
                Encoding.UTF8.GetBytes(signature.Trim()));
        }

        internal bool SameSecret(Signer other)
        {
            return FixedTimeEquals(_secret, other._secret);
        }

        private string Sign(string canonical)
        {
            return "v1=" + Hex.Lower(Hmac(_secret, Encoding.UTF8.GetBytes(canonical)));
        }

        private static byte[] Hmac(byte[] key, byte[] message)
        {
            using (var mac = new HMACSHA256(key))
            {
                return mac.ComputeHash(message);
            }
        }

        // CryptographicOperations.FixedTimeEquals isn't on every Mono the Rust server ships.
        internal static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        public override string ToString()
        {
            return "Signer[redacted]";
        }
    }
}
