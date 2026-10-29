import { ActionHandler, ActionKind, ActionResult } from "@ticket-tool/plugin-sdk";
import { findOnlineByDiscord } from "./identity";
import { Natives } from "./natives";

/**
 * The actions this resource can carry out. Unban isn't one: txAdmin and the
 * framework ban lists are read from their files, with no supported way to
 * lift a ban from another resource.
 */
export const SUPPORTED_ACTIONS: readonly ActionKind[] = ["message", "warn", "kick"];

/** Carries out `player.action`; the SDK has already refused kinds the owner left off. */
export function createActionHandler(n: Natives, log: (m: string) => void): ActionHandler {
  return (request) => {
    if (!SUPPORTED_ACTIONS.includes(request.kind)) return ActionResult.failure("unsupported");
    const discordId = request.player.discordId;
    const player = discordId ? findOnlineByDiscord(n, discordId) : null;
    if (!player) return ActionResult.failure("player_offline");
    const message = request.message ?? "";
    switch (request.kind) {
      case "message":
        n.emitNet("chat:addMessage", player.source, {
          color: [80, 140, 255],
          args: [`[Staff] ${request.actorName}`, message],
        });
        break;
      case "warn":
        n.emitNet("chat:addMessage", player.source, { color: [255, 80, 80], args: ["Warning from staff", message] });
        break;
      case "kick":
        n.DropPlayer(player.source, message ? `Kicked by staff: ${message}` : "Kicked by staff");
        break;
    }
    log(`${request.actorName} (Ticket Tool) sent a ${request.kind} to ${player.name}`);
    return ActionResult.done();
  };
}
