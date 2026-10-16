package app.tickettool.sdk;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import app.tickettool.sdk.json.Json;
import app.tickettool.sdk.json.JsonFields;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;

/** The shared vectors in spec/test-vectors/v1.json; every SDK must pass them. */
class SignerTest {
    private static Map<String, Object> vectors;

    @BeforeAll
    static void load() throws IOException {
        try (InputStream in = SignerTest.class.getResourceAsStream("/v1.json")) {
            vectors = Json.parseObject(new String(in.readAllBytes(), StandardCharsets.UTF_8));
        }
    }

    private static String str(Map<String, Object> m, String k) {
        return JsonFields.string(m, k);
    }

    @SuppressWarnings("unchecked")
    private static List<Map<String, Object>> list(String k) {
        return (List<Map<String, Object>>) (List<?>) JsonFields.list(vectors, k);
    }

    private static byte[] utf8(String s) {
        return s.getBytes(StandardCharsets.UTF_8);
    }

    @Test
    void derivesTheSigningSecret() {
        assertEquals(str(vectors, "signingSecret"), Signer.deriveSigningSecret(str(vectors, "key")));
    }

    @Test
    void hashesBodies() {
        for (Map<String, Object> v : list("bodyHashes")) {
            assertEquals(str(v, "sha256"), Signer.sha256Hex(utf8(str(v, "body"))));
        }
    }

    @Test
    void signsRequests() {
        Signer signer = new Signer(str(vectors, "signingSecret"));
        for (Map<String, Object> v : list("requests")) {
            long ts = JsonFields.number(v, "timestamp");
            byte[] body = utf8(str(v, "body"));
            assertEquals(str(v, "canonical"),
                    Signer.canonical(str(v, "method"), str(v, "endpoint"), ts, str(v, "nonce"), body));
            assertEquals(str(v, "signature"),
                    signer.signRequest(str(v, "method"), str(v, "endpoint"), ts, str(v, "nonce"), body));
        }
    }

    @Test
    void signsAndVerifiesReplies() {
        Signer signer = new Signer(str(vectors, "signingSecret"));
        for (Map<String, Object> v : list("replies")) {
            long ts = JsonFields.number(v, "timestamp");
            byte[] body = utf8(str(v, "body"));
            String nonce = str(v, "nonce");
            String endpoint = str(v, "endpoint");
            assertEquals(str(v, "signature"), signer.signReply(endpoint, ts, nonce, body));
            assertTrue(signer.verifyReply(endpoint, ts, nonce, body, str(v, "signature")));
        }
    }

    @Test
    void rejectsTamperedReplies() {
        Signer signer = new Signer(str(vectors, "signingSecret"));
        String sig = "v1=57e67ded8456ec7d4a98676cfbd5d346e3e0d5d6a1e74f9685fb00496cc3837d";
        byte[] body = utf8("{\"ok\":true,\"requests\":[]}");
        assertTrue(signer.verifyReply("/poll", 1760529601, "abcdefghijklmnop", body, sig));
        assertFalse(signer.verifyReply("/poll", 1760529601, "abcdefghijklmnop", utf8("{\"ok\":true,\"requests\":[1]}"), sig));
        assertFalse(signer.verifyReply("/poll", 1760529602, "abcdefghijklmnop", body, sig));
        assertFalse(signer.verifyReply("/poll", 1760529601, "abcdefghijklmnoq", body, sig));
        assertFalse(signer.verifyReply("/hello", 1760529601, "abcdefghijklmnop", body, sig));
        assertFalse(signer.verifyReply("/poll", 1760529601, "abcdefghijklmnop", body, null));
        assertFalse(signer.verifyReply("/poll", 1760529601, "abcdefghijklmnop", body, "v1=00"));
    }

    @Test
    void usesTheHexStringBytesAsTheHmacKey() {
        // Decoding the hex to 32 raw bytes is the classic mistake; it must not match.
        String secretHex = str(vectors, "signingSecret");
        Signer right = new Signer(secretHex);
        String expected = "v1=d71627ae221c55861c68c1e5d230d7faec74072f75d952ccaa58a2b67b608d3d";
        assertEquals(expected, right.signRequest("POST", "/poll", 1760529600, "abcdefghijklmnop", utf8("{}")));
        String decodedAsLatin1 = new String(java.util.HexFormat.of().parseHex(secretHex), StandardCharsets.ISO_8859_1);
        Signer wrong = new Signer(decodedAsLatin1);
        assertFalse(expected.equals(wrong.signRequest("POST", "/poll", 1760529600, "abcdefghijklmnop", utf8("{}"))));
    }
}
