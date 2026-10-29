package app.tickettool.sdk;

import java.util.concurrent.CompletableFuture;

/**
 * Carries out actions the server owner turned on; the SDK answers
 * {@code not_allowed} itself for any other kind. Called on an SDK worker
 * thread, so hop onto the game's thread before touching game state.
 */
@FunctionalInterface
public interface ActionHandler {
    CompletableFuture<ActionResult> handle(ActionRequest request);
}
