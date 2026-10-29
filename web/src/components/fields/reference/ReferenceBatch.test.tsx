import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { api } from "../../../api/client";
import { DocumentContext, DocumentStore, type DocumentContextValue } from "../../../state/documentStore";
import { ReferenceControl } from "./ReferenceControl";

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

function context(documentId = "d", revision = 0): DocumentContextValue {
  return { workspaceId: "w", documentId, store: new DocumentStore({ canUndo: false, canRedo: false, isDirty: false, revision }),
    structureVersion: 0, canEdit: true, commit: vi.fn(), mutate: async call => call(), acceptResult: vi.fn(),
    runAction: vi.fn(), refresh: vi.fn(async () => {}) };
}

function cells(ctx: DocumentContextValue, count = 50) {
  return <DocumentContext.Provider value={ctx}>
    {Array.from({ length: count }, (_, index) => <ReferenceControl key={index} id={`cell-${index}`} valueId={`field/${index}`}
      actionId={index === count - 1 ? "action" : undefined} value="same-key" spec={{ kind: "reference", catalogId: "items" }} onCommit={async () => true} />)}
  </DocumentContext.Provider>;
}

function response(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });
}

it("coalesces a page and action parameters, preserving field-specific results for the same key", async () => {
  const fetch = vi.fn(async (_url: string, init?: RequestInit) => {
    const body = JSON.parse(String(init?.body ?? "{}"));
    return response({ revision: body.revision, results: (body.pairs ?? []).map((pair: { fieldId: string }) => ({ ...pair, status: 200,
      candidate: { rawId: "same-key", label: pair.fieldId, eligibility: { available: true, eligible: true, selectable: true, reasons: [] } } })) });
  });
  vi.stubGlobal("fetch", fetch);
  render(cells(context()));
  await waitFor(() => expect(screen.getByTitle("field/49 (same-key)")).toBeTruthy());
  expect(fetch).toHaveBeenCalledTimes(1);
  const [url, init] = fetch.mock.calls[0]!;
  expect(url).toBe("/api/workspaces/w/documents/d/reference-fields/records");
  expect(init?.method).toBe("POST");
  const body = JSON.parse(String(init?.body));
  expect(body.pairs).toHaveLength(50);
  expect(body.pairs[49]).toEqual({ fieldId: "field/49", recordKey: "same-key", actionId: "action" });
  expect(body.revision).toBe(0);
  expect(screen.getByTitle("field/0 (same-key)")).toBeTruthy();
});

it("isolates documents and revisions and ignores obsolete responses", async () => {
  let finish!: (value: Response) => void;
  const fetch = vi.fn().mockImplementationOnce(() => new Promise<Response>(resolve => { finish = resolve; }))
    .mockImplementation(async () => response({ revision: 1, results: [{ fieldId: "field/0", recordKey: "same-key", actionId: "action", status: 200,
      candidate: { rawId: "same-key", label: "Current", eligibility: { available: true, eligible: true, selectable: true, reasons: [] } } }] }));
  vi.stubGlobal("fetch", fetch);
  const view = render(cells(context(), 1));
  await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1));
  view.rerender(cells(context("other", 1), 1));
  await waitFor(() => expect(screen.getByTitle("Current (same-key)")).toBeTruthy());
  await act(async () => finish(response({ revision: 0, results: [{ status: 200, candidate: { rawId: "same-key", label: "Obsolete" } }] })));
  expect(screen.queryByTitle("Obsolete (same-key)")).toBeNull();
  expect(fetch.mock.calls.map(call => call[0])).toEqual([
    "/api/workspaces/w/documents/d/reference-fields/records", "/api/workspaces/w/documents/other/reference-fields/records",
  ]);
});

it("has a typed batch client that sends exact opaque pairs and propagates problems", async () => {
  const pairs = [{ fieldId: "field/#? ", recordKey: "18446744073709551615", actionId: "action/?" }];
  const fetch = vi.fn<(url: string, init?: RequestInit) => Promise<Response>>(async () => response({ status: 413, error: "At most 256 pairs." }, 413));
  vi.stubGlobal("fetch", fetch);
  await expect(api.referenceRecords("w", "d", pairs, 7)).rejects.toMatchObject({ status: 413 });
  expect(JSON.parse(String(fetch.mock.calls[0]?.[1]?.body))).toEqual({ pairs, revision: 7 });
});

it("recovers a stale page with one document refresh across all affected cells", async () => {
  vi.stubGlobal("fetch", vi.fn(async () => response({ status: 409, code: "stale", error: "Refresh the document." }, 409)));
  const ctx = context();
  render(cells(ctx));
  await waitFor(() => expect(ctx.refresh).toHaveBeenCalledTimes(1));
  await act(async () => {});
  expect(ctx.refresh).toHaveBeenCalledTimes(1);
});
