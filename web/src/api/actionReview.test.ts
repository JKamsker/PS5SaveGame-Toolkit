import { describe, expect, it } from "vitest";
import { reviewEffectExactValue, reviewEffectValue, reviewValue } from "./actionReview";
import type { FieldSpec, FieldValue } from "./protocol";

describe("reviewValue", () => {
  it("shows scalars, null and absent values", () => {
    expect(reviewValue(42)).toBe("42");
    expect(reviewValue(true)).toBe("true");
    expect(reviewValue(null)).toBe("null");
    expect(reviewValue("x", false)).toBe("(absent)");
  });

  it("shows the exact lexeme of tagged exact numbers", () => {
    expect(reviewValue({ kind: "exactNumber", text: "12345678901234567890" })).toBe("12345678901234567890");
  });

  it("shows union values with their chosen type", () => {
    expect(reviewValue({ kind: "union", type: "number", value: { kind: "exactNumber", text: "1.5" } })).toBe("number: 1.5");
    expect(reviewValue({ kind: "union", type: "null", value: null })).toBe("null: null");
  });
});

describe("exact effect union evidence", () => {
  const spec: FieldSpec = { kind: "union", options: [
    { id: "a", label: "A", spec: { kind: "text" } },
    { id: "a: text", label: "Opaque delimiter tag", spec: { kind: "text" } },
  ] };
  it("distinguishes delimiter IDs from typed text in display and exact copy", () => {
    const first: FieldValue = { kind: "union", type: "a", value: "text: x" };
    const second: FieldValue = { kind: "union", type: "a: text", value: "x" };
    expect(reviewEffectValue(first, true, spec)).not.toBe(reviewEffectValue(second, true, spec));
    const firstCopy = reviewEffectExactValue(first, true, spec);
    const secondCopy = reviewEffectExactValue(second, true, spec);
    expect(firstCopy).not.toBe(secondCopy);
    expect(JSON.parse(firstCopy)).toEqual({ kind: "union", type: "a", value: { kind: "text", value: "text: x" } });
    expect(JSON.parse(secondCopy)).toEqual({ kind: "union", type: "a: text", value: { kind: "text", value: "x" } });
  });
  it("escapes opaque nested tags and preserves exact numbers and long text", () => {
    const tag = 'a: text"\\\n';
    const nested: FieldSpec = { kind: "union", options: [{ id: tag, label: "Nested", spec: {
      kind: "union", options: [{ id: "count", label: "Count", spec: { kind: "integer", min: "0", max: "9999999999999999999" } }],
    } }] };
    const value: FieldValue = { kind: "union", type: tag, value: { kind: "union", type: "count", value: "9007199254740993" } };
    expect(JSON.parse(reviewEffectExactValue(value, true, nested))).toEqual({ kind: "union", type: tag,
      value: { kind: "union", type: "count", value: { kind: "integer", value: "9007199254740993" } } });
    const text = `exact-long-${"Z".repeat(700)}`;
    expect(reviewEffectExactValue(text, true, { kind: "text" })).toBe(`text: ${text}`);
    expect(reviewEffectExactValue(null, false, { kind: "text" })).toBe("(absent)");
    expect(reviewEffectExactValue(null, true, { kind: "nullable", inner: { kind: "text" } })).toBe("null (explicit)");
    expect(reviewEffectExactValue("null", true, { kind: "text" })).toBe("text: null");
  });
});
