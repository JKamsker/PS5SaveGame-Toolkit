import { defaultScheduler, notifyManager, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { Profiler, type ProfilerOnRenderCallback } from "react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { actionReview } from "../../api/actionReview";
import { api } from "../../api/client";
import type { EditResult, SaveDocument } from "../../api/protocol";
import { DocumentStore } from "../../state/documentStore";
import { DocumentEditor } from "./DocumentEditor";
import { field, stubViewport, table } from "./table/tableTestUtils";

const clients: QueryClient[] = [];
let restoreViewport: () => void;
beforeEach(() => {
  notifyManager.setScheduler(queueMicrotask);
  restoreViewport = stubViewport();
});
afterEach(() => {
  cleanup(); restoreViewport(); clients.splice(0).forEach(client => client.clear());
  vi.restoreAllMocks(); notifyManager.setScheduler(defaultScheduler);
});

function snapshot(size: number): SaveDocument {
  return { id: "d", fileId: "f", title: "Save", filePath: "save.dat", canEdit: true,
    plugin: { id: "test", name: "Test", version: "1", maturity: "Experimental", games: [], references: [] },
    state: { revision: 1, isDirty: false, canUndo: false, canRedo: false },
    view: table(Array.from({ length: size }, (_, i) => ({ key: `row${i}`, cells: {
      value: field(`flag${i}`, { kind: "bool" }, i % 2 === 0),
    } })), ["value"]) };
}

async function mount(size = 20_000) {
  const document = snapshot(size);
  vi.spyOn(api, "document").mockResolvedValue(document);
  vi.spyOn(api, "sources").mockResolvedValue({ datasets: [] });
  const reads = vi.spyOn(DocumentStore.prototype, "value");
  const durations: number[] = [];
  const profile: ProfilerOnRenderCallback = (_id, _phase, duration) => { durations.push(duration); };
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  clients.push(client);
  const toast = vi.fn();
  const tree = (status = "Ready") => <QueryClientProvider client={client}>
    <p role="status">{status}</p>
    <Profiler id="document" onRender={profile}>
      <DocumentEditor workspaceId="w" documentId="d" file={undefined} onSwitchPlugin={() => {}} onToast={toast} />
    </Profiler>
  </QueryClientProvider>;
  const started = performance.now();
  let view!: ReturnType<typeof render>;
  await act(async () => { view = render(tree()); });
  const mountMs = performance.now() - started;
  const mountedRows = view.container.querySelectorAll(".table__row").length - 1;
  return { ...view, client, document, reads, durations, tree, mountMs, mountedRows };
}

it("keeps a 20,000-row document idle through 30 workspace status updates", async () => {
  const { rerender, reads, durations, tree, mountMs, mountedRows } = await mount();
  expect(mountedRows).toBeGreaterThan(0);
  expect(mountedRows).toBeLessThan(100);
  reads.mockClear(); durations.length = 0;
  const started = performance.now();
  for (let i = 0; i < 30; i++) act(() => rerender(tree(`Workspace update ${i}`)));
  // eslint-disable-next-line no-console -- PERF_REPORT explicitly enables these performance measurements.
  if (process.env.PERF_REPORT) console.info(JSON.stringify({ rows: 20_000, mountedRows, mountMs,
    updates: 30, fieldReads: reads.mock.calls.length, elapsedMs: performance.now() - started,
    profilerMs: durations.reduce((sum, duration) => sum + duration, 0) }));
  expect(screen.getByRole("status").textContent).toBe("Workspace update 29");
  expect(reads.mock.calls.length).toBe(0);
  expect(api.document).toHaveBeenCalledOnce();
  expect(api.sources).toHaveBeenCalledOnce();
});

it("keeps live values, errors and replacement snapshots responsive behind the render boundary", async () => {
  const { client, document } = await mount(10);
  const edit = vi.spyOn(api, "edit").mockResolvedValue({ ok: true, errors: [], warnings: [], structureChanged: false,
    changes: [{ valueId: "flag0", oldValue: true, newValue: false }],
    state: { revision: 1, isDirty: true, canUndo: true, canRedo: false } });
  await act(async () => { fireEvent.click(screen.getByRole("switch", { name: /^flag0/ })); });
  expect(edit).toHaveBeenCalledOnce();
  expect(screen.getByRole("switch", { name: /^flag0/ }).getAttribute("aria-checked")).toBe("false");
  edit.mockResolvedValue({ ok: false, errors: [{ valueId: "flag1", message: "Value rejected" }], warnings: [],
    structureChanged: false, changes: [], state: document.state });
  await act(async () => { fireEvent.click(screen.getByRole("switch", { name: /^flag1/ })); });
  expect(screen.getAllByText("Value rejected").length).toBeGreaterThan(0);
  await act(async () => { client.setQueryData(["document", "w", "d"], { ...document,
    view: { type: "text", text: "Replacement snapshot" } }); });
  expect(screen.getByText("Replacement snapshot")).toBeTruthy();
  expect(screen.queryByRole("table")).toBeNull();
});

it("loads one current Changes snapshot after a revert instead of refetching the retired query", async () => {
  const changes = vi.spyOn(actionReview, "changes").mockResolvedValue({ revision: 1, complete: true, warnings: [], changes: [{
    valueId: "flag0", label: "Flag", path: [], originalValue: false, currentValue: true,
    originalExists: true, currentExists: true, canRevert: true,
  }] });
  vi.spyOn(actionReview, "revert").mockResolvedValue({ ok: true, errors: [], warnings: [], structureChanged: false,
    changes: [{ valueId: "flag0", oldValue: true, newValue: false }],
    state: { revision: 1, isDirty: false, canUndo: true, canRedo: false } } satisfies EditResult);
  await mount(10);
  await act(async () => {
    const details = screen.getByText("Changes against original").closest("details")!;
    details.open = true; fireEvent(details, new Event("toggle"));
  });
  expect(changes).toHaveBeenCalledOnce();
  changes.mockResolvedValue({ revision: 1, complete: true, warnings: [], changes: [] });
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: "Revert Flag" })); });
  expect(screen.getByText("No semantic changes.")).toBeTruthy();
  expect(screen.getByRole("switch", { name: /^flag0/ }).getAttribute("aria-checked")).toBe("false");
  // eslint-disable-next-line no-console -- PERF_REPORT explicitly enables these performance measurements.
  if (process.env.PERF_REPORT) console.info(JSON.stringify({ changesRequestsIncludingOpen: changes.mock.calls.length }));
  expect(changes).toHaveBeenCalledTimes(2);
});
