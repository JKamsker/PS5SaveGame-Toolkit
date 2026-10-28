import { describe, expect, it } from "vitest";
import type { IntegerSpec } from "../api/protocol";
import { describeDuration, displayValue, formatBytes, parseDraft, toDraft } from "./values";

const u64: IntegerSpec = { kind: "integer", min: "0", max: "18446744073709551615" };

describe("parseDraft", () => {
  it("keeps u64 precision beyond 2^53", () => {
    expect(parseDraft(u64, "18446744073709551615")).toEqual({ ok: true, value: "18446744073709551615" });
  });

  it("rejects integers out of range and accepts hex", () => {
    expect(parseDraft(u64, "-1").ok).toBe(false);
    expect(parseDraft(u64, "0xff")).toEqual({ ok: true, value: "255" });
    expect(parseDraft(u64, "1.5").ok).toBe(false);
  });

  it("rounds single precision floats", () => {
    expect(parseDraft({ kind: "float", singlePrecision: true }, "0.1")).toEqual({ ok: true, value: Math.fround(0.1) });
  });

  it("validates enums unless custom values are allowed", () => {
    const spec = { kind: "enum" as const, options: [{ value: "1", label: "Easy" }] };
    expect(parseDraft(spec, "2").ok).toBe(false);
    expect(parseDraft({ ...spec, allowCustom: true }, "2").ok).toBe(true);
  });

  it("limits flags to the bit width", () => {
    const spec = { kind: "flags" as const, bits: [], bitWidth: 4 };
    expect(parseDraft(spec, "15").ok).toBe(true);
    expect(parseDraft(spec, "16").ok).toBe(false);
  });

  it("enforces text constraints", () => {
    expect(parseDraft({ kind: "text", maxLength: 2 }, "abc").ok).toBe(false);
    expect(parseDraft({ kind: "text" }, "a\nb").ok).toBe(false);
  });
});

describe("formatting", () => {
  it("renders hex drafts", () => {
    expect(toDraft({ ...u64, format: "Hex" }, "3208943443")).toBe("0xBF449753");
  });

  it("describes microsecond durations", () => {
    const spec: IntegerSpec = { ...u64, format: "Duration", unitsPerSecond: 1_000_000 };
    expect(describeDuration(spec, "92512000000")).toBe("25 h 41 min 52 s");
    expect(displayValue(spec, "5000000")).toBe("5 s");
  });

  it("formats byte sizes", () => {
    expect(formatBytes(512)).toBe("512 B");
    expect(formatBytes(3301244)).toBe("3.1 MiB");
  });
});
