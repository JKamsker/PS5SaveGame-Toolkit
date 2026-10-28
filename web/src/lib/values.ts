import type { FieldSpec, FieldValue, IntegerSpec } from "../api/protocol";

/** Result of turning what the user typed into a wire value. The server re-validates authoritatively. */
export type Parsed = { ok: true; value: FieldValue } | { ok: false; error: string };

const fail = (error: string): Parsed => ({ ok: false, error });

export function parseInteger(text: string): bigint | null {
  const t = text.trim().replace(/[_\s]/g, "");
  if (/^[+-]?\d+$/.test(t)) return BigInt(t);
  if (/^0x[0-9a-f]+$/i.test(t)) return BigInt(t);
  return null;
}

/** Converts an editor draft (always a string for text inputs) into a value valid for `spec`. */
export function parseDraft(spec: FieldSpec, draft: string | boolean): Parsed {
  switch (spec.kind) {
    case "bool":
      return typeof draft === "boolean" ? { ok: true, value: draft } : fail("Expected on or off.");
    case "integer":
    case "flags": {
      const n = parseInteger(String(draft));
      if (n === null) return fail("Enter a whole number (decimal or 0x hex).");
      const [min, max] = spec.kind === "integer" ? [BigInt(spec.min), BigInt(spec.max)] : [0n, (1n << BigInt(spec.bitWidth)) - 1n];
      if (n < min || n > max) return fail(`Must be between ${min} and ${max}.`);
      return { ok: true, value: n.toString() };
    }
    case "float": {
      const t = String(draft).trim();
      const n = Number(t);
      if (t === "" || !Number.isFinite(n)) return fail("Enter a number.");
      if (spec.min !== undefined && n < spec.min) return fail(`Must be at least ${spec.min}.`);
      if (spec.max !== undefined && n > spec.max) return fail(`Must be at most ${spec.max}.`);
      return { ok: true, value: spec.singlePrecision ? Math.fround(n) : n };
    }
    case "text": {
      const t = String(draft);
      if (spec.maxLength !== undefined && t.length > spec.maxLength) return fail(`At most ${spec.maxLength} characters.`);
      if (!spec.multiline && /[\r\n]/.test(t)) return fail("Line breaks are not allowed.");
      return { ok: true, value: t };
    }
    case "enum": {
      const t = String(draft);
      if (!spec.allowCustom && !spec.options.some((o) => o.value === t)) return fail("Pick one of the options.");
      return { ok: true, value: t };
    }
    case "datetime": {
      const t = String(draft);
      return Number.isNaN(Date.parse(t)) ? fail("Enter a date and time.") : { ok: true, value: t };
    }
    case "reference":
      return { ok: true, value: String(draft) }; // Membership/eligibility are checked against the pinned server catalog.
  }
}

/** The editable text shown for a value. */
export function toDraft(spec: FieldSpec, value: FieldValue): string | boolean {
  if (spec.kind === "bool") return value === true;
  if (value === null) return "";
  if (spec.kind === "integer" && spec.format === "Hex") return `0x${BigInt(String(value)).toString(16).toUpperCase()}`;
  if (spec.kind === "datetime") return String(value).slice(0, 19);
  return String(value);
}

export function sameValue(a: FieldValue, b: FieldValue): boolean {
  return a === b || (typeof a === "number" && typeof b === "number" && Number.isNaN(a) && Number.isNaN(b));
}

/** "25 h 41 min 52 s" for durations; returns null when the spec is not a duration. */
export function describeDuration(spec: IntegerSpec, value: FieldValue): string | null {
  if (spec.format !== "Duration" || !spec.unitsPerSecond || value === null) return null;
  const units = BigInt(String(value));
  const totalSeconds = Number(units / BigInt(spec.unitsPerSecond));
  const h = Math.floor(totalSeconds / 3600);
  const m = Math.floor((totalSeconds % 3600) / 60);
  const s = totalSeconds % 60;
  return [h ? `${h} h` : "", h || m ? `${m} min` : "", `${s} s`].filter(Boolean).join(" ");
}

/** Short human range like "0 – 4,294,967,295". */
export function describeRange(spec: IntegerSpec): string {
  const fmt = (v: string) => BigInt(v).toLocaleString("en-US");
  return `${fmt(spec.min)} – ${fmt(spec.max)}`;
}

export function formatBytes(length: number): string {
  if (length < 1024) return `${length} B`;
  const units = ["KiB", "MiB", "GiB"];
  let v = length / 1024;
  let i = 0;
  while (v >= 1024 && i < units.length - 1) {
    v /= 1024;
    i++;
  }
  return `${v.toFixed(v < 10 ? 1 : 0)} ${units[i]}`;
}

/** Plain-text rendering of any value for search and read-only display. */
export function displayValue(spec: FieldSpec, value: FieldValue): string {
  if (value === null) return "—";
  switch (spec.kind) {
    case "bool":
      return value ? "Yes" : "No";
    case "enum":
      return spec.options.find((o) => o.value === value)?.label ?? String(value);
    case "integer":
      return describeDuration(spec, value) ?? String(toDraft(spec, value));
    default:
      return String(value);
  }
}
