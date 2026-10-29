/**
 * The Cfx.re server natives this resource uses, behind an interface so the
 * logic can be tested under plain Node with stubs. `fromGlobals()` binds to the
 * real globals FXServer's JS runtime defines.
 */
export interface Natives {
  GetConvar(name: string, fallback: string): string;
  GetCurrentResourceName(): string;
  GetResourceState(resource: string): string;
  GetResourceMetadata(resource: string, key: string, index: number): string | null;
  LoadResourceFile(resource: string, file: string): string | null;
  GetPlayers(): string[];
  GetPlayerName(source: string): string | null;
  GetNumPlayerIdentifiers(source: string): number;
  GetPlayerIdentifier(source: string, index: number): string | null;
  SetResourceKvp(key: string, value: string): void;
  GetResourceKvpString(key: string): string | null;
  RegisterCommand(name: string, handler: (source: number, args: string[], raw: string) => void, restricted: boolean): void;
  /** Registers a local event handler; `source` is read from the global while it runs. */
  on(event: string, handler: (...args: any[]) => void): void;
  emitNet(event: string, target: number | string, ...args: unknown[]): void;
  DropPlayer(source: string, reason: string): void;
  /** The current event's source, for handlers registered with `on`. */
  currentSource(): string;
  /** Calls `exports[resource][name](...args)`; throws when the export is missing. */
  callExport(resource: string, name: string, ...args: unknown[]): unknown;
  print(line: string): void;
}

type AnyFn = (...args: any[]) => any;

export function fromGlobals(): Natives {
  const g = globalThis as unknown as Record<string, any>;
  const fn = (name: string): AnyFn => {
    const f = g[name];
    if (typeof f !== "function") throw new Error(`Native ${name} isn't available`);
    return f as AnyFn;
  };
  return {
    GetConvar: (n, d) => fn("GetConvar")(n, d),
    GetCurrentResourceName: () => fn("GetCurrentResourceName")(),
    GetResourceState: (r) => fn("GetResourceState")(r),
    GetResourceMetadata: (r, k, i) => fn("GetResourceMetadata")(r, k, i),
    LoadResourceFile: (r, f) => fn("LoadResourceFile")(r, f),
    GetPlayers: () => {
      const list = fn("getPlayers" in g ? "getPlayers" : "GetPlayers")();
      return Array.isArray(list) ? list.map(String) : [];
    },
    GetPlayerName: (s) => fn("GetPlayerName")(s),
    GetNumPlayerIdentifiers: (s) => fn("GetNumPlayerIdentifiers")(s),
    GetPlayerIdentifier: (s, i) => fn("GetPlayerIdentifier")(s, i),
    SetResourceKvp: (k, v) => fn("SetResourceKvp")(k, v),
    GetResourceKvpString: (k) => fn("GetResourceKvpString")(k),
    RegisterCommand: (n, h, r) => fn("RegisterCommand")(n, h, r),
    on: (e, h) => fn("on")(e, h),
    emitNet: (e, t, ...a) => fn("emitNet")(e, t, ...a),
    DropPlayer: (s, r) => fn("DropPlayer")(s, r),
    currentSource: () => String(g.source),
    callExport: (resource, name, ...args) => {
      const target = g.exports?.[resource];
      const f = target?.[name];
      if (typeof f !== "function") throw new Error(`Export ${resource}.${name} isn't available`);
      return (f as AnyFn)(...args);
    },
    print: (line) => console.log(line),
  };
}

export const isStarted = (n: Natives, resource: string): boolean => {
  try {
    return n.GetResourceState(resource) === "started";
  } catch {
    return false;
  }
};
