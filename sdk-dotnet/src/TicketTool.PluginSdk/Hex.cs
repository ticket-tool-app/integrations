namespace TicketTool.PluginSdk
{
    internal static class Hex
    {
        private const string Digits = "0123456789abcdef";

        internal static string Lower(byte[] bytes)
        {
            var chars = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = Digits[bytes[i] >> 4];
                chars[i * 2 + 1] = Digits[bytes[i] & 0xF];
            }
            return new string(chars);
        }
    }
}
