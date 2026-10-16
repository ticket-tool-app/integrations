package app.tickettool.sdk;

/**
 * Where the SDK reports what it's doing. The SDK never passes it the key or
 * the signing secret.
 */
public interface SdkLogger {
    SdkLogger NONE = new SdkLogger() {
        @Override
        public void info(String message) {}

        @Override
        public void warn(String message, Throwable error) {}

        @Override
        public void debug(String message) {}
    };

    void info(String message);

    /** @param error may be null */
    void warn(String message, Throwable error);

    void debug(String message);
}
