import { Natives } from "../src/natives";

export interface FakePlayer {
  name: string;
  identifiers: string[];
}

/** In-memory stand-ins for the FXServer natives, for unit tests. */
export class FakeNatives implements Natives {
  convars: Record<string, string> = {};
  resources: Record<string, string> = { "ticket-tool-link": "started" };
  files: Record<string, string> = {};
  players = new Map<string, FakePlayer>();
  kvp = new Map<string, string>();
  exportsTable: Record<string, Record<string, (...args: any[]) => unknown>> = {};
  handlers = new Map<string, Array<(...args: any[]) => void>>();
  commands = new Map<string, { handler: (source: number, args: string[], raw: string) => void; restricted: boolean }>();
  printed: string[] = [];
  chat: Array<{ target: number | string; args: unknown[] }> = [];
  source = "0";

  addPlayer(source: string, name: string, identifiers: string[]): void {
    this.players.set(source, { name, identifiers });
  }

  emit(event: string, source: string, ...args: unknown[]): void {
    const prev = this.source;
    this.source = source;
    try {
      for (const h of this.handlers.get(event) ?? []) h(...args);
    } finally {
      this.source = prev;
    }
  }

  GetConvar(name: string, fallback: string): string {
    return this.convars[name] ?? fallback;
  }
  GetCurrentResourceName(): string {
    return "ticket-tool-link";
  }
  GetResourceState(resource: string): string {
    return this.resources[resource] ?? "missing";
  }
  GetResourceMetadata(_resource: string, key: string): string | null {
    return key === "version" ? "1.2.3" : null;
  }
  LoadResourceFile(resource: string, file: string): string | null {
    return this.files[`${resource}/${file}`] ?? null;
  }
  GetPlayers(): string[] {
    return [...this.players.keys()];
  }
  GetPlayerName(source: string): string | null {
    return this.players.get(source)?.name ?? null;
  }
  GetNumPlayerIdentifiers(source: string): number {
    return this.players.get(source)?.identifiers.length ?? 0;
  }
  GetPlayerIdentifier(source: string, index: number): string | null {
    return this.players.get(source)?.identifiers[index] ?? null;
  }
  SetResourceKvp(key: string, value: string): void {
    this.kvp.set(key, value);
  }
  GetResourceKvpString(key: string): string | null {
    return this.kvp.get(key) ?? null;
  }
  RegisterCommand(name: string, handler: (source: number, args: string[], raw: string) => void, restricted: boolean): void {
    this.commands.set(name, { handler, restricted });
  }
  on(event: string, handler: (...args: any[]) => void): void {
    const list = this.handlers.get(event) ?? [];
    list.push(handler);
    this.handlers.set(event, list);
  }
  emitNet(event: string, target: number | string, ...args: unknown[]): void {
    if (event === "chat:addMessage") this.chat.push({ target, args });
  }
  currentSource(): string {
    return this.source;
  }
  callExport(resource: string, name: string, ...args: unknown[]): unknown {
    const f = this.exportsTable[resource]?.[name];
    if (!f) throw new Error(`Export ${resource}.${name} isn't available`);
    return f(...args);
  }
  print(line: string): void {
    this.printed.push(line);
  }
}

export const DISCORD = "123456789012345678";
// Gabe Newell's well-known SteamID64 and its Cfx.re hex form.
export const STEAM64 = "76561197960287930";
export const STEAM_HEX = "1100001000056ba";
export const LICENSE = "license:abcdef0123456789abcdef0123456789abcdef01";

export function janeIdentifiers(): string[] {
  return [LICENSE, `steam:${STEAM_HEX}`, `discord:${DISCORD}`, "ip:203.0.113.7", "fivem:1234"];
}
