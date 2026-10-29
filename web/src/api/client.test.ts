import { afterEach, describe, expect, it, vi } from "vitest";
import { api, ApiError, isStaleError } from "./client";

afterEach(() => vi.unstubAllGlobals());

it("passes lazy expansion cancellation to fetch and preserves opaque IDs", async () => {
  const fetch = vi.fn().mockResolvedValue(new Response("[]", { headers: { "content-type": "application/json" } }));
  vi.stubGlobal("fetch", fetch);
  const controller = new AbortController();
  await api.expand("w", "d", "tree/%2F?#", controller.signal);
  const [path, init] = fetch.mock.calls[0]!;
  expect(new URL(path as string, "http://localhost").searchParams.get("id")).toBe("tree/%2F?#");
  expect(init.signal.aborted).toBe(false);
  // The transport composes the caller's cancellation with its own deadline.
  expect(init.signal).toBeInstanceOf(AbortSignal);
});

describe("collection requests", () => {
  it.each([true, false, undefined])("transports includeEmpty=%s without losing explicit false", async includeEmpty => {
    const fetch = vi.fn().mockResolvedValue(new Response("{}", { headers: { "content-type": "application/json" } }));
    vi.stubGlobal("fetch", fetch);
    await api.queryCollection("w", "d", "items", { includeEmpty });
    const url = new URL(fetch.mock.calls[0]![0] as string, "http://localhost");
    expect(url.searchParams.get("includeEmpty")).toBe(includeEmpty === undefined ? null : String(includeEmpty));
  });
  it.each(["gvas/properties", "literal%2Fitems", "inventory/#? + items"])("preserves the opaque ID %s in the query string", async collectionId => {
    const fetch = vi.fn().mockResolvedValue(new Response("{}", { headers: { "content-type": "application/json" } }));
    vi.stubGlobal("fetch", fetch);
    await api.queryCollection("w", "d", collectionId, { q: "Item 050000", offset: 50, limit: 50, sort: "name", descending: true, filters: { type: "item" } }, 3);
    const url = new URL(fetch.mock.calls[0]![0] as string, "http://localhost");
    expect(url.pathname).toBe("/api/workspaces/w/documents/d/collections");
    expect(url.searchParams.get("collectionId")).toBe(collectionId);
    expect(url.searchParams.get("q")).toBe("Item 050000");
    expect(url.searchParams.get("offset")).toBe("50");
    expect(url.searchParams.get("revision")).toBe("3");
    expect(JSON.parse(url.searchParams.get("filters")!)).toEqual({ type: "item" });
  });
});

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
