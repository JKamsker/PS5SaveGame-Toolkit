import { afterEach, describe, expect, expectTypeOf, it, vi } from "vitest";
import { api, ApiError } from "./client";
import { cancelledMessage } from "./http";
import type { ReferenceRecordDetail } from "./protocol";

afterEach(() => vi.unstubAllGlobals());

// GET /references/{catalogId}/record, as documented in docs/API.md and ReferenceDtos.cs.
const detail = {
  record: {
    key: "potion",
    name: "Potion",
    category: "Consumable",
    limit: 99,
    description: "Restores health.",
    assetId: "items:icons/potion.png",
    source: "Community item catalog",
    attributes: { rarity: "Common" },
    packId: "items",
    datasetId: "consumables",
  },
  packTitle: "Community items",
  packVersion: "1.2.3",
  dataset: {
    packId: "items",
    id: "consumables",
    kind: "Records",
    title: "Consumable items",
    itemCount: 42,
    attribution: {
      sourceUrl: "https://example.com/items",
      author: "Community contributors",
      license: "CC-BY-4.0",
      licenseUrl: "https://creativecommons.org/licenses/by/4.0/",
      retrieved: "2026-10-29",
      modifications: "Normalized item names.",
    },
  },
} satisfies ReferenceRecordDetail;

function respond(body: unknown, status = 200, contentType = "application/json") {
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(body), {
    status, headers: { "content-type": contentType },
  }));
  vi.stubGlobal("fetch", fetch);
  return fetch;
}

describe("catalog record details", () => {
  it.each(["item/%2F?# + &雪", ""])("gets a record with encoded opaque IDs and key %j", async key => {
    const fetch = respond(detail);
    const result = await api.referenceRecord("workspace #", "document?", "items%?# &", key);

    expect(result).toEqual(detail);
    expect(fetch).toHaveBeenCalledOnce();
    const [path, init] = fetch.mock.calls[0]!;
    const url = new URL(path as string, "http://localhost");
    expect(url.pathname).toBe("/api/workspaces/workspace%20%23/documents/document%3F/references/items%25%3F%23%20%26/record");
    expect([...url.searchParams]).toEqual([["key", key]]);
    expect(init.method ?? "GET").toBe("GET");
    expect(init.body).toBeUndefined();
  });

  it.each(["application/json", "application/problem+json"])("normalizes a 404 ProblemDetails response (%s)", async contentType => {
    const problem = { type: "about:blank", title: "Not Found", status: 404,
      detail: "No record 'missing' in catalog 'items'." };
    const fetch = respond(problem, 404, contentType);
    const outcome = api.referenceRecord("w", "d", "items", "missing");

    await expect(outcome).rejects.toBeInstanceOf(ApiError);
    await expect(outcome).rejects.toMatchObject({ status: 404, body: problem,
      message: `${problem.title}: ${problem.detail}` });
    expect(fetch).toHaveBeenCalledOnce();
  });

  it.each([false, true])("normalizes cancellation (already aborted: %s)", async alreadyAborted => {
    const controller = new AbortController();
    if (alreadyAborted) controller.abort();
    const fetch = vi.fn((_path: string, init: RequestInit) => new Promise<Response>((_resolve, reject) => {
      const signal = init.signal!;
      if (signal.aborted) reject(signal.reason);
      else signal.addEventListener("abort", () => reject(signal.reason), { once: true });
    }));
    vi.stubGlobal("fetch", fetch);
    const outcome = api.referenceRecord("w", "d", "items", "potion", controller.signal);
    if (!alreadyAborted) controller.abort();

    await expect(outcome).rejects.toBeInstanceOf(ApiError);
    await expect(outcome).rejects.toMatchObject({ status: 0, message: cancelledMessage });
    expect(fetch).toHaveBeenCalledOnce();
    expect(fetch.mock.calls[0]![1].signal!.aborted).toBe(true);
  });

  it("maps the documented DTO envelope including record metadata and dataset attribution", async () => {
    respond(detail);
    const result = await api.referenceRecord("w", "d", "items", "potion");

    expectTypeOf(result).toEqualTypeOf<ReferenceRecordDetail>();
    expect(Object.keys(result).sort()).toEqual(["dataset", "packTitle", "packVersion", "record"]);
    expect(result.record).toEqual(detail.record);
    expect(result.packTitle).toBe(detail.packTitle);
    expect(result.packVersion).toBe(detail.packVersion);
    expect(result.dataset).toEqual(detail.dataset);
  });

  it("preserves omitted optional DTO fields", async () => {
    const minimal = { record: { key: "unknown", name: "Unknown", packId: "items", datasetId: "consumables" },
      packTitle: detail.packTitle, packVersion: detail.packVersion,
      dataset: { packId: "items", id: "consumables", kind: "Records", itemCount: 1,
        attribution: detail.dataset.attribution } } satisfies ReferenceRecordDetail;
    respond(minimal);

    expect(await api.referenceRecord("w", "d", "items", "unknown")).toEqual(minimal);
  });
});
