import type { FieldSpec, FieldValue, FloatSpec, IntegerSpec } from "../api/protocol";
import { exactNumberError } from "./exactNumbers";
import { textCapacityError } from "./textCapacity";

const MILLISECONDS_PER_SECOND = 1000;
const SECONDS_PER_MINUTE = 60;
const SECONDS_PER_HOUR = 3600;
const MILLISECONDS_PER_MINUTE = SECONDS_PER_MINUTE * MILLISECONDS_PER_SECOND;
const MILLISECONDS_PER_HOUR = SECONDS_PER_HOUR * MILLISECONDS_PER_SECOND;
const DURATION_DECIMAL_PLACES = 3;
const FLOAT32_MAX = 3.4028234663852886e38;

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
    case "exactNumber": {
      const text = String(draft);
      const error = exactNumberError(text, spec);
      return error ? fail(error) : { ok: true, value: { kind: "exactNumber", text } };
    }
    case "nullable":
      return parseDraft(spec.inner, draft);
    case "union":
      return fail("Select a union type.");
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
    case "float":
      return parseFloatDraft(spec, String(draft));
    case "text": {
      const t = String(draft);
      if (spec.maxUtf8Bytes != null) {
        const error = textCapacityError(t, spec.maxUtf8Bytes);
        if (error) return fail(error);
      }
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

function parseFloatDraft(spec: FloatSpec, draft: string): Parsed {
  const text = draft.trim();
  // Decimal keyboards use the browser locale. Dot decimals stay valid for raw save values;
  // grouping and mixed separators are rejected instead of guessing a different magnitude.
  const decimal = new Intl.NumberFormat().formatToParts(1.1).find(part => part.type === "decimal")?.value ?? ".";
  const normalized = text.includes(".") ? text : text.replace(decimal, ".");
  let number = Number(normalized);
  if (text === "" || !Number.isFinite(number)) return fail("Enter a number without thousands separators.");
  if (spec.singlePrecision) {
    if (Math.abs(number) > FLOAT32_MAX) return fail("Number is out of float32 range.");
    number = Math.fround(number);
  }
  if (spec.min !== undefined && number < spec.min) return fail(`Must be at least ${spec.min}.`);
  if (spec.max !== undefined && number > spec.max) return fail(`Must be at most ${spec.max}.`);
  return { ok: true, value: number };
}

/** The editable text shown for a value. */
export function toDraft(spec: FieldSpec, value: FieldValue): string | boolean {
  if (spec.kind === "bool") return value === true;
  if (value === null) return "";
  if (typeof value === "object") return value.kind === "exactNumber" ? value.text : displayValue(spec, value);
  if (spec.kind === "integer" && spec.format === "Hex") return `0x${BigInt(String(value)).toString(16).toUpperCase()}`;
  if (spec.kind === "datetime") return String(value).slice(0, 19);
  return String(value);
}

export function sameValue(a: FieldValue, b: FieldValue): boolean {
  if (a !== null && b !== null && typeof a === "object" && typeof b === "object") {
    if (a.kind === "exactNumber" && b.kind === "exactNumber") return a.text === b.text;
    if (a.kind === "union" && b.kind === "union") return a.type === b.type && sameValue(a.value, b.value);
    return false;
  }
  return a === b || (typeof a === "number" && typeof b === "number" && Number.isNaN(a) && Number.isNaN(b));
}

/** "25 h 41 min 52 s" for durations; returns null when the spec is not a duration. */
export function describeDuration(spec: IntegerSpec | FloatSpec, value: FieldValue): string | null {
  if (spec.format !== "Duration" || value === null) return null;
  if (spec.kind === "float") return describeFloatDuration(spec, value);
  // Keep the existing integer display in seconds, without scaling large integer timers to milliseconds.
  if (!spec.unitsPerSecond || !Number.isSafeInteger(spec.unitsPerSecond) || spec.unitsPerSecond <= 0) return null;
  const seconds = Number(BigInt(String(value)) / BigInt(spec.unitsPerSecond));
  const h = Math.floor(seconds / SECONDS_PER_HOUR);
  const m = Math.floor((seconds % SECONDS_PER_HOUR) / SECONDS_PER_MINUTE);
  const s = seconds % SECONDS_PER_MINUTE;
  return [h ? `${h} h` : "", h || m ? `${m} min` : "", `${s} s`].filter(Boolean).join(" ");
}

function describeFloatDuration(spec: FloatSpec, value: FieldValue): string | null {
  const rate = spec.unitsPerSecond ?? 1;
  if (!Number.isFinite(rate) || rate <= 0) return null;
  const seconds = Number(value) / rate;
  if (!Number.isFinite(seconds)) return null;
  const negative = seconds < 0;
  // Millisecond display precision; editing always uses the original raw numeric value.
  const milliseconds = Math.round(Math.abs(seconds) * MILLISECONDS_PER_SECOND);
  if (!Number.isFinite(milliseconds)) return null;
  const h = Math.floor(milliseconds / MILLISECONDS_PER_HOUR);
  const m = Math.floor((milliseconds % MILLISECONDS_PER_HOUR) / MILLISECONDS_PER_MINUTE);
  const s = Number(((milliseconds % MILLISECONDS_PER_MINUTE) / MILLISECONDS_PER_SECOND).toFixed(DURATION_DECIMAL_PLACES));
  const text = [h ? `${h} h` : "", h || m ? `${m} min` : "", `${s} s`].filter(Boolean).join(" ");
  return `${negative && milliseconds ? "−" : ""}${text}`;
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
  if (value === null) return "null";
  if (spec.kind === "nullable") return displayValue(spec.inner, value);
  if (typeof value === "object") {
    if (value.kind === "exactNumber") return value.text;
    const option = spec.kind === "union" ? spec.options.find((o) => o.id === value.type) : undefined;
    return option ? displayValue(option.spec, value.value) : value.type;
  }
  switch (spec.kind) {
    case "bool":
      return value ? "Yes" : "No";
    case "enum":
      return spec.options.find((o) => o.value === value)?.label ?? String(value);
    case "integer":
    case "float":
      return describeDuration(spec, value) ?? String(toDraft(spec, value));
    default:
      return String(value);
  }
}
