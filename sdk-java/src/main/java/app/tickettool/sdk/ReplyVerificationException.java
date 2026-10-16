package app.tickettool.sdk;

import java.io.IOException;

/** A reply whose signature, nonce or timestamp didn't check out. Never trusted. */
public final class ReplyVerificationException extends IOException {
    private static final long serialVersionUID = 1L;

    public ReplyVerificationException(String message) {
        super(message);
    }
}
