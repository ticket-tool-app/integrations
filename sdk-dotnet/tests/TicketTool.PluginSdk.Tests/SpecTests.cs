using System.Text;

namespace TicketTool.PluginSdk.Tests;

public class SpecVectorTests
{
    private static readonly Dictionary<string, object> V =
        Json.ParseObject(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "test-vectors", "v1.json")));

    [Fact]
    public void DerivesTheSigningSecret()
    {
        Assert.Equal(JsonFields.String(V, "signingSecret"), Signer.DeriveSigningSecret(JsonFields.String(V, "key")!));
    }

    [Fact]
    public void ParsesTheKeyId()
    {
        Assert.Equal(JsonFields.String(V, "keyId"), PluginKey.Parse(JsonFields.String(V, "key")).KeyId);
    }

    [Fact]
    public void HashesBodies()
    {
        foreach (Dictionary<string, object> c in JsonFields.List(V, "bodyHashes").Cast<Dictionary<string, object>>())
            Assert.Equal(JsonFields.String(c, "sha256"),
                Signer.Sha256Hex(Encoding.UTF8.GetBytes(JsonFields.String(c, "body")!)));
    }

    [Fact]
    public void SignsRequests()
    {
        var signer = new Signer(JsonFields.String(V, "signingSecret")!);
        foreach (Dictionary<string, object> c in JsonFields.List(V, "requests").Cast<Dictionary<string, object>>())
        {
            byte[] body = Encoding.UTF8.GetBytes(JsonFields.String(c, "body")!);
            long ts = JsonFields.Long(c, "timestamp")!.Value;
            string method = JsonFields.String(c, "method")!, endpoint = JsonFields.String(c, "endpoint")!,
                nonce = JsonFields.String(c, "nonce")!;
            Assert.Equal(JsonFields.String(c, "canonical"), Signer.Canonical(method, endpoint, ts, nonce, body));
            Assert.Equal(JsonFields.String(c, "signature"), signer.SignRequest(method, endpoint, ts, nonce, body));
        }
    }

    [Fact]
    public void SignsAndVerifiesReplies()
    {
        var signer = new Signer(JsonFields.String(V, "signingSecret")!);
        foreach (Dictionary<string, object> c in JsonFields.List(V, "replies").Cast<Dictionary<string, object>>())
        {
            byte[] body = Encoding.UTF8.GetBytes(JsonFields.String(c, "body")!);
            long ts = JsonFields.Long(c, "timestamp")!.Value;
            string endpoint = JsonFields.String(c, "endpoint")!, nonce = JsonFields.String(c, "nonce")!;
            string want = JsonFields.String(c, "signature")!;
            Assert.Equal(want, signer.SignReply(endpoint, ts, nonce, body));
            Assert.True(signer.VerifyReply(endpoint, ts, nonce, body, want));
            Assert.False(signer.VerifyReply(endpoint, ts + 1, nonce, body, want));
            Assert.False(signer.VerifyReply(endpoint, ts, nonce, body, null!));
        }
    }
}

public class PluginKeyTests
{
    [Fact]
    public void NeverShowsTheSecret()
    {
        var key = PluginKey.Parse("  " + FakeTicketTool.Key + "\n");
        Assert.Equal("ttp_0123456789abcdef", key.ToString());
        Assert.DoesNotContain("AAAA", key.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("ttp_0123456789ABCDEF.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("ttp_0123456789abcdef.short")]
    [InlineData("ttp_0123456789abcdef.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    public void RejectsMalformedKeysWithoutEchoingThem(string text)
    {
        var e = Assert.Throws<ArgumentException>(() => PluginKey.Parse(text));
        if (text.Length > 0) Assert.DoesNotContain(text, e.Message);
        Assert.False(PluginKey.LooksLikeKey(text));
    }
}

public class JsonTests
{
    [Fact]
    public void RoundTripsProtocolShapes()
    {
        const string text = "{\"ok\":true,\"n\":-12,\"f\":1.5,\"s\":\"a\\\"b\\\\c\\n\\u00e9\",\"l\":[1,null,false],\"o\":{}}";
        var m = Json.ParseObject(text);
        Assert.Equal(true, m["ok"]);
        Assert.Equal(-12L, m["n"]);
        Assert.Equal(1.5, m["f"]);
        Assert.Equal("a\"b\\c\n\u00e9", m["s"]);
        Assert.Equal(text.Replace("\\u00e9", "\u00e9"), Json.Write(m));
    }

    [Fact]
    public void EscapesControlCharactersAndLineSeparators()
    {
        Assert.Equal("\"\\u0001\\u2028\"", Json.Write("\u0001\u2028"));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"a\":}")]
    [InlineData("[1,]")]
    [InlineData("{} x")]
    [InlineData("\"\\q\"")]
    public void RejectsMalformedJson(string text)
    {
        Assert.Throws<JsonException>(() => Json.Parse(text));
    }

    [Fact]
    public void RefusesDeepNesting()
    {
        Assert.Throws<JsonException>(() => Json.Parse(new string('[', 200) + new string(']', 200)));
    }
}

public class BackoffTests
{
    [Fact]
    public void DoublesUpToSixtySecondsWithJitterInTheUpperHalf()
    {
        var b = Backoff.Standard(new Random(1));
        long[] steps = { 1000, 2000, 4000, 8000, 16000, 32000, 60000, 60000 };
        foreach (long step in steps)
        {
            double ms = b.Next().TotalMilliseconds;
            Assert.InRange(ms, step / 2, step);
        }
        b.Reset();
        Assert.InRange(b.Next().TotalMilliseconds, 500, 1000);
    }
}

public class SteamIdTests
{
    [Theory]
    [InlineData("76561198000000000", "76561198000000000")]
    [InlineData("STEAM_1:0:19867136", "76561198000000000")]
    [InlineData("STEAM_0:1:19867136", "76561198000000001")]
    [InlineData("[U:1:39734272]", "76561198000000000")]
    [InlineData("U:1:39734272", "76561198000000000")]
    [InlineData("1234", null)]
    [InlineData("STEAM_1:2:3", null)]
    public void Normalizes(string input, string? expected)
    {
        Assert.Equal(expected, SteamIds.Normalize(input));
    }
}

public class TextTests
{
    [Fact]
    public void StripsRichTextAndSourceColors()
    {
        Assert.Equal("Cheating  (aimbot)", Text.Clean("<color=red>Cheating</color> \u0002 (<b>aimbot</b>)", 100));
    }

    [Fact]
    public void ClipsWithoutSplittingSurrogates()
    {
        Assert.Equal("a", Text.Clip("a\U0001F600", 2));
    }
}
