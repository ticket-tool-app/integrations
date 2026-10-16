package app.tickettool.minecraft;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

class LinkCodesTest {
    @Test
    void acceptsCodesWithOrWithoutTheDashInAnyCase() {
        assertTrue(LinkCodes.plausible("ABCD-EFGH"));
        assertTrue(LinkCodes.plausible("abcdefgh"));
        assertTrue(LinkCodes.plausible("abcd efgh"));
        assertTrue(LinkCodes.plausible("2345-6789"));
    }

    @Test
    void rejectsLettersTheServerNeverIssues() {
        assertFalse(LinkCodes.plausible("ABCD-EFG0"));
        assertFalse(LinkCodes.plausible("ABCD-EFGI"));
        assertFalse(LinkCodes.plausible("ABCD-EFGU"));
        assertFalse(LinkCodes.plausible("ABCD-EFG"));
        assertFalse(LinkCodes.plausible("ABCD-EFGHJ"));
        assertFalse(LinkCodes.plausible("Notch"));
    }
}
