import { afterEach, describe, expect, it, vi } from "vitest";
import { describeRange, displayValue, parseDraft, toDraft } from "./values";

afterEach(() => vi.restoreAllMocks());

describe("German numeric drafts", () => {
  const NumberFormat = Intl.NumberFormat;
  function germanLocale() {
    vi.spyOn(Intl, "NumberFormat").mockImplementation(function (locale, options) {
      return new NumberFormat(locale ?? "de-DE", options);
    });
  }

  it.each(["1,5", "-1234,5", "1,5e2"])("accepts the locale decimal separator in %s", text => {
    germanLocale();
    expect(parseDraft({ kind: "float", singlePrecision: false }, text)).toEqual({ ok: true, value: Number(text.replace(",", ".")) });
  });

  it("keeps invariant decimal-point editing and JSON numeric values", () => {
    germanLocale();
    const spec = { kind: "float" as const, singlePrecision: false };
    expect(parseDraft(spec, "1234.5")).toEqual({ ok: true, value: 1234.5 });
    expect(toDraft(spec, 1234.5)).toBe("1234.5");
    expect(displayValue(spec, 1234.5)).toBe("1234.5");
    expect(JSON.stringify(parseDraft(spec, "1234,5"))).toBe('{"ok":true,"value":1234.5}');
  });

  it.each(["1.234,5", "1,234.5", "1,2,3", "1,5oops"])("rejects grouping, mixed separators and partial input %s", text => {
    germanLocale();
    expect(parseDraft({ kind: "float", singlePrecision: false }, text).ok).toBe(false);
  });

  it("checks bounds and single precision after decimal-comma normalization", () => {
    germanLocale();
    expect(parseDraft({ kind: "float", singlePrecision: true }, "0,1")).toEqual({ ok: true, value: Math.fround(0.1) });
    expect(parseDraft({ kind: "float", singlePrecision: false, max: 1 }, "1,5").ok).toBe(false);
    expect(parseDraft({ kind: "exactNumber" }, "1,5").ok).toBe(false);
    expect(parseDraft({ kind: "integer", min: "0", max: "9999" }, "1,234").ok).toBe(false);
    expect(describeRange({ kind: "integer", min: "0", max: "1234" })).toBe("0 – 1,234");
  });

  it("does not reinterpret commas on an English decimal keyboard", () => {
    vi.spyOn(Intl, "NumberFormat").mockImplementation(function (locale, options) {
      return new NumberFormat(locale ?? "en-US", options);
    });
    expect(parseDraft({ kind: "float", singlePrecision: false }, "1,5").ok).toBe(false);
    expect(parseDraft({ kind: "float", singlePrecision: false }, "1,234").ok).toBe(false);
  });
});
