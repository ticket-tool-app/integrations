import type { ProfileField } from "@ticket-tool/plugin-sdk";
import { stripCfxColors } from "@ticket-tool/plugin-sdk";
import { Natives, isStarted } from "./natives";

/**
 * Character name and job from whichever roleplay framework is running. Each
 * reader is detected at call time, since frameworks can start after this
 * resource, and every one is optional: a failure just means fewer fields.
 */
export interface FrameworkReader {
  readonly name: string;
  detect(): boolean;
  fields(source: string): ProfileField[];
}

type Obj = Record<string, any>;
const text = (v: unknown): string | null => {
  if (typeof v === "number") return String(v);
  if (typeof v !== "string") return null;
  const t = stripCfxColors(v)!.trim();
  return t === "" ? null : t;
};
const join = (...parts: unknown[]): string | null => {
  const t = parts.map(text).filter((p): p is string => p != null).join(" ");
  return t === "" ? null : t;
};
const jobLabel = (label: unknown, grade: unknown): string | null => {
  const l = text(label);
  const g = text(grade);
  if (!l) return null;
  return g && g.toLowerCase() !== l.toLowerCase() ? `${l} (${g})` : l;
};
const push = (out: ProfileField[], label: string, value: string | null) => {
  if (value) out.push({ label, value });
};

export function esxReader(n: Natives): FrameworkReader {
  let esx: Obj | null = null;
  return {
    name: "ESX",
    detect: () => isStarted(n, "es_extended"),
    fields(source) {
      esx ??= n.callExport("es_extended", "getSharedObject") as Obj;
      const x = esx?.GetPlayerFromId?.(Number(source)) as Obj | undefined;
      if (!x) return [];
      const out: ProfileField[] = [];
      const name = typeof x.getName === "function" ? x.getName() : x.name;
      push(out, "Character", text(name));
      const job = (typeof x.getJob === "function" ? x.getJob() : x.job) as Obj | undefined;
      if (job) push(out, "Job", jobLabel(job.label ?? job.name, job.grade_label));
      return out;
    },
  };
}

function qbStyle(n: Natives, resource: string, name: string, getPlayer: (source: number) => Obj | undefined): FrameworkReader {
  return {
    name,
    detect: () => isStarted(n, resource),
    fields(source) {
      const data = getPlayer(Number(source))?.PlayerData as Obj | undefined;
      if (!data) return [];
      const out: ProfileField[] = [];
      push(out, "Character", join(data.charinfo?.firstname, data.charinfo?.lastname));
      push(out, "Job", jobLabel(data.job?.label ?? data.job?.name, data.job?.grade?.name));
      if (data.gang && data.gang.name && data.gang.name !== "none") {
        push(out, "Gang", jobLabel(data.gang.label ?? data.gang.name, data.gang.grade?.name));
      }
      return out;
    },
  };
}

export function qbcoreReader(n: Natives): FrameworkReader {
  let core: Obj | null = null;
  return qbStyle(n, "qb-core", "QBCore", (source) => {
    core ??= n.callExport("qb-core", "GetCoreObject") as Obj;
    return core?.Functions?.GetPlayer?.(source) as Obj | undefined;
  });
}

export function qboxReader(n: Natives): FrameworkReader {
  return qbStyle(n, "qbx_core", "Qbox", (source) => n.callExport("qbx_core", "GetPlayer", source) as Obj | undefined);
}

/** ox_core: character name only; its job ("groups") model has no single label to show. */
export function oxReader(n: Natives): FrameworkReader {
  return {
    name: "ox_core",
    detect: () => isStarted(n, "ox_core"),
    fields(source) {
      const p = n.callExport("ox_core", "GetPlayer", Number(source)) as Obj | undefined;
      if (!p) return [];
      const get = (k: string) => (typeof p.get === "function" ? p.get(k) : undefined) ?? p[k];
      const out: ProfileField[] = [];
      push(out, "Character", join(get("firstName"), get("lastName")));
      return out;
    },
  };
}

export class Frameworks {
  readonly readers: FrameworkReader[];

  constructor(n: Natives, private readonly onError: (message: string) => void = () => {}) {
    // qbx_core also starts a qb-core bridge, so it goes first to avoid reading through the shim.
    this.readers = [qboxReader(n), qbcoreReader(n), esxReader(n), oxReader(n)];
  }

  active(): FrameworkReader | null {
    return this.readers.find((r) => r.detect()) ?? null;
  }

  fields(source: string): ProfileField[] {
    const reader = this.active();
    if (!reader) return [];
    try {
      return reader.fields(source);
    } catch (e) {
      this.onError(`Couldn't read ${reader.name} character data: ${(e as Error).message}`);
      return [];
    }
  }
}
