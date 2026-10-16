/** Clips by code point so a surrogate pair is never cut in half. */
export function clip(s: string, max: number): string;
export function clip(s: string | null | undefined, max: number): string | null;
export function clip(s: string | null | undefined, max: number): string | null {
  if (s == null) return null;
  if (s.length <= max) return s;
  let end = max;
  const code = s.charCodeAt(end - 1);
  if (code >= 0xd800 && code <= 0xdbff) end--;
  return s.slice(0, end);
}

/** Trimmed, or null when nothing is left. */
export function emptyToNull(s: unknown): string | null {
  if (typeof s !== "string") return null;
  const t = s.trim();
  return t === "" ? null : t;
}

// Section-sign codes plus the '&' codes most Minecraft plugins accept, including hex forms.
const MINECRAFT_CODES = /(?:[§&]x(?:[§&][0-9a-f]){6}|[§&]#[0-9a-f]{6}|[§&][0-9a-fk-or])/gi;
// Cfx.re (FiveM/RedM) chat colors: ^0-^9, plus the ^* ^_ ^~ ^r style toggles.
const CFX_CODES = /\^(?:[0-9]|[*_~r=])/g;

export function stripMinecraftColors(s: string | null | undefined): string | null {
  return s == null ? null : s.replace(MINECRAFT_CODES, "");
}

export function stripCfxColors(s: string | null | undefined): string | null {
  return s == null ? null : s.replace(CFX_CODES, "");
}
