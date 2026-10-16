package app.tickettool.sdk;

import java.util.concurrent.CompletableFuture;

/**
 * Answers lookups. Called on an SDK worker thread, never the poll thread; the
 * returned future may complete on any thread, including the game's, because
 * the SDK hops off it before sending the answer.
 */
@FunctionalInterface
public interface RequestHandler {
    CompletableFuture<LookupResult> handle(LookupRequest request);
}
