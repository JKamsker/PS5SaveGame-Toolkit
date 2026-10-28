import { describe, expect, it } from "vitest";
import { ApiError, isStaleError } from "./client";

describe("isStaleError", () => {
  it("recognises the typed 409 stale rejection", () => {
    expect(isStaleError(new ApiError("out of date", 409, { error: "out of date", code: "stale" }))).toBe(true);
  });

  it("ignores other conflicts, statuses and non-API errors", () => {
    expect(isStaleError(new ApiError("dirty", 409, { error: "dirty" }))).toBe(false);
    expect(isStaleError(new ApiError("missing", 404, { error: "missing", code: "stale" }))).toBe(false);
    expect(isStaleError(new Error("boom"))).toBe(false);
    expect(isStaleError(undefined)).toBe(false);
  });
});
