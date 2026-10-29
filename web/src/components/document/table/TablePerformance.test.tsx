import { act, cleanup, renderHook } from "@testing-library/react";
import { StrictMode } from "react";
import { afterEach, expect, it, vi } from "vitest";
import { DocumentContext, DocumentStore, type DocumentContextValue } from "../../../state/documentStore";
import { field, table } from "./tableTestUtils";
import { useTableRows } from "./useTableRows";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

function setup(size = 10_000) {
  const node = table(Array.from({ length: size }, (_, i) => ({ key: `row${i}`, cells: {
    value: field(`flag${i}`, { kind: "bool" }, i % 2 === 0),
  } })), ["value"]);
  const store = new DocumentStore({ canUndo: false, canRedo: false, isDirty: false });
  const context: DocumentContextValue = { workspaceId: "w", documentId: "d", store, canEdit: true,
    structureVersion: 0, refresh: vi.fn(), commit: vi.fn(), acceptResult: vi.fn(), runAction: vi.fn() };
  const reads = vi.spyOn(store, "value");
  const hook = renderHook(() => useTableRows(node, "yes", {}), {
    wrapper: ({ children }) => <StrictMode><DocumentContext.Provider value={context}>{children}</DocumentContext.Provider></StrictMode>,
  });
  const change = (id: string, value: boolean) => store.apply({ ok: true, errors: [], warnings: [],
    changes: [{ valueId: id, oldValue: !value, newValue: value }], structureChanged: false, state: store.state });
  return { ...hook, store, reads, change };
}

it("does no table work for edits to fields outside a 10,000-row table", () => {
  const { result, reads, change } = setup();
  const before = result.current;
  reads.mockClear();
  const started = performance.now();
  for (let i = 0; i < 30; i++) act(() => change("other-field", i % 2 === 0));
  if (process.env.PERF_REPORT) console.info(JSON.stringify({ unrelatedEdits: 30, fieldReads: reads.mock.calls.length,
    elapsedMs: performance.now() - started }));
  expect(reads.mock.calls.length).toBe(0);
  expect(result.current).toBe(before);
});

it("keeps filters cached for validation notifications while fields retain their own errors", () => {
  const { result, reads, store } = setup(10);
  const before = result.current;
  reads.mockClear();
  act(() => store.apply({ ok: false, errors: [{ valueId: "flag0", message: "Rejected" }], changes: [],
    warnings: [], structureChanged: false, state: store.state }));
  act(() => store.setError("flag0", "Rejected"));
  expect(store.error("flag0")).toBe("Rejected");
  expect(reads.mock.calls.length).toBe(0);
  expect(result.current).toBe(before);
});

it("updates filters for unmounted rows, bulk edits and reset", () => {
  const { result, change, store } = setup();
  expect(result.current.rows).toHaveLength(5000);
  act(() => change("flag9999", true));
  expect(result.current.rows).toHaveLength(5001);
  act(() => store.apply({ ok: true, errors: [], warnings: [], structureChanged: false, state: store.state,
    changes: [0, 2].map(i => ({ valueId: `flag${i}`, oldValue: true, newValue: false })) }));
  expect(result.current.rows).toHaveLength(4999);
  act(() => store.reset(store.state));
  expect(result.current.rows).toHaveLength(5000);
});
