import { ClientStatus, SdkLogger, TicketToolClient } from "@ticket-tool/plugin-sdk";
import { activeSources, providerNames } from "./bans";
import { EasyAdminBans } from "./bans/easyadmin";
import { QbBans } from "./bans/qbcore";
import { TxAdminBans } from "./bans/txadmin";
import { BanSource } from "./bans/types";
import { Settings, platformString, readSettings } from "./config";
import { Frameworks } from "./frameworks";
import { onlinePlayer } from "./identity";
import { createLookupHandler } from "./lookup";
import { Natives, fromGlobals } from "./natives";
import { SeenStore } from "./seen-store";

const HEARTBEAT_MS = 5 * 60 * 1000;

export interface Resource {
  reload(): Promise<void>;
  stop(): Promise<void>;
  status(): string[];
  readonly client: TicketToolClient | null;
}

export function main(n: Natives = fromGlobals()): Resource {
  const resourceName = n.GetCurrentResourceName();
  const version = (n.GetResourceMetadata(resourceName, "version", 0) ?? "").trim() || "0.0.0-dev";
  let settings: Settings = readSettings(n);
  let client: TicketToolClient | null = null;
  let keyProblem: string | null = null;

  const print = (color: string, message: string) => n.print(`^5[Ticket Tool]^7 ${color}${message}^7`);
  const logger: SdkLogger = {
    debug: (m) => settings.debug && print("^8", m),
    info: (m) => print("", m),
    warn: (m, e) => {
      print("^3", m);
      if (settings.debug && e instanceof Error && e.stack) print("^8", e.stack);
    },
  };

  const store = new SeenStore(n);
  const frameworks = new Frameworks(n, (m) => logger.debug(m));
  const txadmin = new TxAdminBans(n, {
    get overridePath() {
      return settings.txAdminDb;
    },
  });
  txadmin.listen();
  const banSources: BanSource[] = [txadmin, new EasyAdminBans(n), new QbBans(n)];
  const sources = () => activeSources(banSources, settings.banProvider);

  const handler = createLookupHandler({
    natives: n,
    store,
    frameworks,
    banSources,
    banProvider: () => settings.banProvider,
    serverName: () => settings.serverName,
    warn: (m) => logger.warn(m),
  });

  const touchSource = (source: string) => {
    try {
      store.touch(onlinePlayer(n, source), frameworks.fields(source));
    } catch (e) {
      logger.debug(`Couldn't record player ${source}: ${(e as Error).message}`);
    }
  };
  const touchAll = () => n.GetPlayers().forEach(touchSource);

  n.on("playerJoining", () => touchSource(n.currentSource()));
  // Read before the framework forgets the character, so offline lookups can still show it.
  n.on("playerDropped", () => touchSource(n.currentSource()));
  const heartbeat = setInterval(touchAll, HEARTBEAT_MS);
  (heartbeat as { unref?: () => void }).unref?.();

  function connect(): void {
    keyProblem = null;
    for (const p of settings.problems) logger.warn(p);
    if (!settings.key) {
      logger.warn("No integration key set. Add set ticket_tool_key \"ttp_...\" to server.cfg, then run ticketlink reload.");
      return;
    }
    try {
      client = new TicketToolClient({
        key: settings.key,
        apiUrl: settings.apiUrl,
        plugin: { type: "fivem", name: "ticket-tool-fivem", version, platform: platformString(n) },
        banProviders: () => providerNames(sources()),
        logger,
        handler,
      });
    } catch (e) {
      keyProblem = (e as Error).message;
      logger.warn(keyProblem);
      return;
    }
    client.start();
  }

  async function reload(): Promise<void> {
    const old = client;
    client = null;
    if (old) await old.stop();
    settings = readSettings(n);
    touchAll();
    connect();
  }

  async function stop(): Promise<void> {
    clearInterval(heartbeat);
    const old = client;
    client = null;
    if (old) await old.stop();
  }

  function status(): string[] {
    const lines = [`Ticket Tool Link ${version} on ${platformString(n)}`];
    if (!client) {
      lines.push(keyProblem ? `Key problem: ${keyProblem}` : "No key set (ticket_tool_key)");
    } else {
      const s: ClientStatus = client.status();
      lines.push(`State: ${describe(s.state)}`);
      lines.push(`Key: ${client.key.displayPrefix}${s.hello?.keyName ? ` ("${s.hello.keyName}")` : ""}`);
      if (s.hello) lines.push(`Discord server: ${s.hello.serverName ?? s.hello.serverId}`);
      lines.push(`Lookups answered: ${s.answered}`);
      if (Math.abs(s.clockOffsetSeconds) > 5) lines.push(`Clock offset: ${s.clockOffsetSeconds}s from Ticket Tool`);
      if (s.lastError) lines.push(`Last problem: ${s.lastError}${s.lastErrorAt ? ` (${ago(s.lastErrorAt)} ago)` : ""}`);
    }
    const active = sources();
    lines.push(`Ban sources: ${active.length ? active.map((s) => s.name).join(", ") : "none"} (setting: ${settings.banProvider})`);
    if (active.includes(txadmin)) lines.push(`txAdmin database: ${txadmin.file ?? "not found yet"}`);
    lines.push(`Framework: ${frameworks.active()?.name ?? "none detected"}`);
    return lines;
  }

  n.RegisterCommand(
    "ticketlink",
    (source, args) => {
      const reply = (lines: string[]) => {
        if (source === 0) {
          for (const l of lines) print("", l);
        } else {
          for (const l of lines) n.emitNet("chat:addMessage", source, { args: ["Ticket Tool", l] });
        }
      };
      const sub = (args[0] ?? "status").toLowerCase();
      if (sub === "status") {
        reply(status());
      } else if (sub === "reload") {
        reply(["Reloading..."]);
        void reload().then(() => setTimeout(() => reply(status()), 1500));
      } else {
        reply(["Usage: ticketlink status | reload"]);
      }
    },
    // Restricted: console, or players granted the command.ticketlink ace.
    true,
  );

  n.on("onResourceStop", (name: string) => {
    if (name === resourceName) void stop();
  });

  touchAll();
  connect();

  return {
    reload,
    stop,
    status,
    get client() {
      return client;
    },
  };
}

function describe(state: ClientStatus["state"]): string {
  switch (state) {
    case "starting":
      return "connecting";
    case "connected":
      return "connected to Ticket Tool";
    case "reconnecting":
      return "reconnecting";
    case "paused_plan":
      return "paused: the Discord server's plan doesn't include game lookups";
    case "stopped_invalid_key":
      return "stopped: Ticket Tool doesn't recognize this key";
    case "stopped_error":
      return "stopped";
    default:
      return "stopped";
  }
}

function ago(at: Date): string {
  const s = Math.max(0, Math.floor((Date.now() - at.getTime()) / 1000));
  if (s < 60) return `${s}s`;
  if (s < 3600) return `${Math.floor(s / 60)}m`;
  return `${Math.floor(s / 3600)}h`;
}
