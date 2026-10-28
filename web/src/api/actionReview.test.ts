import { describe, expect, it } from "vitest";
import { reviewValue } from "./actionReview";

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
