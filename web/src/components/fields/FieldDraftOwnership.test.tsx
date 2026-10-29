import { useEffect } from "react";
import { defaultScheduler, notifyManager, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { api, ApiError } from "../../api/client";
import type { EditResult, FieldNode, SaveDocument } from "../../api/protocol";
import { DocumentContext, DocumentStore, type DocumentContextValue } from "../../state/documentStore";
import { deferred } from "../../test/deferred";
import { TableCell } from "../document/table/TableCell";
import { useDocumentSession } from "../document/useDocumentSession";
import { FieldRow } from "./FieldRow";

const clients: QueryClient[] = [];
beforeEach(() => { notifyManager.setScheduler(queueMicrotask); });
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); vi.restoreAllMocks(); notifyManager.setScheduler(defaultScheduler); });
const state = { revision: 1, isDirty: false, canUndo: false, canRedo: false };
const node: FieldNode = { type: "field", valueId: "quantity", label: "Quantity", value: "2",
  spec: { kind: "integer", min: "0", max: "99" }, description: "Synthetic shared field" };
const snapshot: SaveDocument = { id: "d", fileId: "f", filePath: "synthetic.json", title: "Synthetic", canEdit: true, state,
  plugin: { id: "test", name: "Test", version: "1", maturity: "Experimental", games: [], references: [] }, view: node };
const rejected = (errors: EditResult["errors"]): EditResult => ({ ok: false, errors, changes: [], warnings: [], structureChanged: false, state });
const quantityRejected = () => rejected([{ valueId: "quantity", message: "9 is rejected" }]);

function Editors({ onContext, toast }: { onContext: (context: DocumentContextValue) => void; toast: (message: string, error?: boolean) => void }) {
  const { context } = useDocumentSession("w", "d", toast);
  useEffect(() => { if (context) onContext(context); }, [context, onContext]);
  if (!context) return null;
  return <DocumentContext.Provider value={context}>
    <FieldRow node={node} />
    <TableCell node={node} rowLabel="Potion" onOpenDetails={() => {}} />
  </DocumentContext.Provider>;
}

async function setup() {
  vi.spyOn(api, "document").mockResolvedValue(snapshot);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  clients.push(client);
  const capture = vi.fn<(context: DocumentContextValue) => void>();
  const toast = vi.fn();
  await act(async () => { render(<QueryClientProvider client={client}><Editors onContext={capture} toast={toast} /></QueryClientProvider>); });
  const first = screen.getByRole<HTMLInputElement>("textbox", { name: "Quantity" });
  const second = screen.getByRole<HTMLInputElement>("textbox", { name: "Quantity (Potion)" });
  const context = capture.mock.lastCall![0];
  return { first, second, context, toast };
}

async function enter(input: HTMLInputElement, value: string) {
  await act(async () => { fireEvent.change(input, { target: { value } }); fireEvent.keyDown(input, { key: "Enter" }); });
}

const queuedFailures = ["http", "global", "other-field"] as const;
const foreignErrors = ["original", "identical", "replacement"] as const;
it.each(queuedFailures.flatMap(failure => foreignErrors.map(foreign => ({ failure, foreign }))))(
  "keeps another editor's $foreign diagnostic when a queued submission fails with $failure", async ({ failure, foreign }) => {
    const a = deferred<EditResult>(); const b = deferred<EditResult>();
    const edit = vi.spyOn(api, "edit").mockReturnValueOnce(a.promise).mockReturnValueOnce(b.promise);
    const { first, second, context, toast } = await setup();
    await enter(first, "9");
    await enter(second, "8");
    expect(edit).toHaveBeenCalledTimes(1); // B is waiting in the real document-session queue.
    await act(async () => { a.resolve(quantityRejected()); await a.promise; });
    expect(edit.mock.calls.map(call => call[2])).toEqual([[{ valueId: "quantity", value: "9" }], [{ valueId: "quantity", value: "8" }]]);
    const message = foreign === "replacement" ? "External quantity diagnostic" : "9 is rejected";
    if (foreign !== "original") act(() => context.store.setError("quantity", message));
    await act(async () => {
      if (failure === "http") b.reject(new ApiError("B unavailable", 503));
      else b.resolve(rejected([{ valueId: failure === "global" ? "" : "other", message: "B rejected elsewhere" }]));
      await b.promise.catch(() => {});
    });
    expect(second.value).toBe("8");
    fireEvent.keyDown(second, { key: "Escape" });
    expect(second.value).toBe("2");
    expect(first.value).toBe("9");
    expect(context.store.error("quantity")).toBe(message);
    expect(first.getAttribute("aria-invalid")).toBe("true");
    expect(screen.getByRole("textbox", { name: "Quantity", description: new RegExp(message) })).toBe(first);
    if (failure === "http") expect(toast).toHaveBeenCalledWith("B unavailable", true);
    if (failure === "global") expect(context.store.error("")).toBe("B rejected elsewhere");
    if (failure === "other-field") expect(context.store.error("other")).toBe("B rejected elsewhere");
    await act(async () => { fireEvent.keyDown(second, { key: "Enter" }); fireEvent.blur(second); });
    expect(edit).toHaveBeenCalledTimes(2);
    expect(context.store.state).toEqual(state);
  },
);

it("lets the submitting editor discard its own settled field rejection", async () => {
  const edit = vi.spyOn(api, "edit").mockResolvedValue(quantityRejected());
  const { first, second, context } = await setup();
  await enter(first, "9");
  expect(context.store.error("quantity")).toBe("9 is rejected");
  fireEvent.keyDown(first, { key: "Escape" });
  expect(first.value).toBe("2");
  expect(second.value).toBe("2");
  expect(first.getAttribute("aria-invalid")).toBe("false");
  expect(second.getAttribute("aria-invalid")).toBe("false");
  expect(context.store.error("quantity")).toBeUndefined();
  expect(edit).toHaveBeenCalledTimes(1);
});

it.each(["9 is rejected", "External diagnostic"])("keeps a fresh '%s' diagnostic installed before the submitting promise settles", async message => {
  vi.spyOn(api, "edit").mockResolvedValue(quantityRejected());
  const { first, context } = await setup();
  let replaced = false;
  const unsubscribe = context.store.subscribe("quantity", () => {
    if (replaced) return;
    replaced = true;
    context.store.setError("quantity", message);
  });
  await enter(first, "9");
  unsubscribe();
  fireEvent.keyDown(first, { key: "Escape" });
  expect(first.value).toBe("2");
  expect(context.store.error("quantity")).toBe(message);
  expect(first.getAttribute("aria-invalid")).toBe("true");
});

it("does not dismiss a shared rejection while its own retry is pending, or cancel the retry on Escape", async () => {
  const pending = deferred<EditResult>();
  const edit = vi.spyOn(api, "edit").mockResolvedValueOnce(quantityRejected()).mockReturnValueOnce(pending.promise);
  const { first, second, context } = await setup();
  await enter(first, "9");
  await enter(first, "8");
  fireEvent.keyDown(first, { key: "Escape" });
  expect(first.value).toBe("2");
  expect(context.store.error("quantity")).toBe("9 is rejected");
  await act(async () => { pending.resolve(rejected([{ valueId: "quantity", message: "8 is rejected" }])); await pending.promise; });
  expect(context.store.error("quantity")).toBe("8 is rejected");
  fireEvent.keyDown(first, { key: "Escape" }); // An unchanged saved draft cannot dismiss the fresh failure.
  fireEvent.keyDown(second, { key: "Escape" }); // The other editor never submitted it.
  expect(context.store.error("quantity")).toBe("8 is rejected");
  expect(edit).toHaveBeenCalledTimes(2);
});

it("keeps foreign diagnostics when a failed write cancels the next queued submission", async () => {
  const pending = deferred<EditResult>();
  const edit = vi.spyOn(api, "edit").mockReturnValueOnce(pending.promise);
  const { first, second, context } = await setup();
  await enter(first, "9");
  await enter(second, "8");
  act(() => context.store.setError("quantity", "External diagnostic"));
  await act(async () => { pending.reject(new ApiError("Write unavailable", 503)); await pending.promise.catch(() => {}); });
  expect(edit).toHaveBeenCalledTimes(1); // The failed write invalidates B before its API request starts.
  fireEvent.keyDown(second, { key: "Escape" });
  expect(second.value).toBe("2");
  expect(first.value).toBe("9");
  expect(context.store.error("quantity")).toBe("External diagnostic");
  expect(second.getAttribute("aria-invalid")).toBe("true");
  expect(context.store.state).toEqual(state);
});

it("deduplicates boolean and detailed callers with both editors against the same pending request", async () => {
  const pending = deferred<EditResult>();
  const edit = vi.spyOn(api, "edit").mockReturnValueOnce(pending.promise);
  const { first, second, context } = await setup();
  await enter(first, "9");
  await enter(second, "9");
  const legacy = context.commit("quantity", "9");
  const detailed = context.commitWithDiagnostics!("quantity", "9");
  expect(edit).toHaveBeenCalledTimes(1);
  await act(async () => { pending.resolve(quantityRejected()); await pending.promise; });
  expect(await legacy).toBe(false);
  const outcome = await detailed;
  expect(outcome.accepted).toBe(false);
  expect(outcome.rejection).toBeDefined();
  expect(outcome.rejection).toBe(context.store.diagnostic("quantity"));
  expect(first.value).toBe("9");
  expect(second.value).toBe("9");
  expect(edit).toHaveBeenCalledTimes(1);
});

it("does not cancel a queued edit when its editor restores the saved draft before the request starts", async () => {
  const a = deferred<EditResult>(); const b = deferred<EditResult>();
  const edit = vi.spyOn(api, "edit").mockReturnValueOnce(a.promise).mockReturnValueOnce(b.promise);
  const { first, second, context } = await setup();
  await enter(first, "9");
  await enter(second, "8");
  fireEvent.keyDown(second, { key: "Escape" });
  expect(second.value).toBe("2");
  expect(edit).toHaveBeenCalledTimes(1);
  await act(async () => { a.resolve(quantityRejected()); await a.promise; });
  expect(edit).toHaveBeenCalledTimes(2);
  expect(context.store.error("quantity")).toBe("9 is rejected");
  await act(async () => {
    b.resolve({ ok: true, errors: [], changes: [{ valueId: "quantity", oldValue: "2", newValue: "8" }],
      warnings: [], structureChanged: false, state: { revision: 2, canUndo: true, canRedo: false, isDirty: true } });
    await b.promise;
  });
  expect(second.value).toBe("8");
  expect(first.value).toBe("9");
  expect(context.store.value("quantity", null)).toBe("8");
  expect(context.store.state.canUndo).toBe(true);
  expect(context.store.error("quantity")).toBeUndefined();
});

it("does not clear a replacement installation even when the same result object is applied again", () => {
  const store = new DocumentStore(state);
  const result = quantityRejected();
  const original = store.applyWithDiagnostics(result).get("quantity")!;
  const replacement = store.applyWithDiagnostics(result).get("quantity")!;
  store.clearError("quantity", original);
  expect(store.error("quantity")).toBe("9 is rejected");
  store.clearError("quantity", replacement);
  expect(store.error("quantity")).toBeUndefined();
});
