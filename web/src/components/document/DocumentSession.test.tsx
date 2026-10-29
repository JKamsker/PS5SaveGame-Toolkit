import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, render, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { api } from "../../api/client";
import type { EditResult, SaveDocument } from "../../api/protocol";
import { useDocument, type DocumentContextValue } from "../../state/documentStore";
import { deferred } from "../../test/deferred";
import { DocumentEditor } from "./DocumentEditor";

let context: DocumentContextValue | undefined;
vi.mock("./ViewRenderer", () => ({ ViewRenderer: function Probe() { context = useDocument(); return null; } }));
const clients: QueryClient[] = [];
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); vi.restoreAllMocks(); context = undefined; });

const snapshot: SaveDocument = { id: "d", fileId: "f", title: "Save", filePath: "save.dat", canEdit: true,
  plugin: { id: "test", name: "Test", version: "1", maturity: "Experimental", games: [], references: [] },
  state: { revision: 1, isDirty: false, canUndo: false, canRedo: false }, view: { type: "text", text: "Save" } };
const accepted: EditResult = { ok: true, errors: [], changes: [], warnings: [], structureChanged: false,
  state: { ...snapshot.state, revision: 2, isDirty: true } };

async function mount() {
  vi.spyOn(api, "document").mockResolvedValue(snapshot);
  vi.spyOn(api, "sources").mockResolvedValue({ datasets: [] });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  clients.push(client);
  const toast = vi.fn();
  render(<QueryClientProvider client={client}><DocumentEditor workspaceId="w" documentId="d" file={undefined}
    onSwitchPlugin={vi.fn()} onToast={toast} /></QueryClientProvider>);
  await waitFor(() => expect(context).toBeDefined());
  return { toast };
}

it("shares identical pending commits and releases the entry after completion", async () => {
  const pending = deferred<EditResult>();
  const edit = vi.spyOn(api, "edit").mockReturnValueOnce(pending.promise).mockResolvedValue(accepted);
  await mount();
  let first!: Promise<boolean>;
  await act(async () => {
    first = context!.commit("item", { kind: "union", type: "text", value: "potion" });
    expect(context!.commit("item", { kind: "union", type: "text", value: "potion" })).toBe(first);
  });
  expect(edit).toHaveBeenCalledOnce();
  await act(async () => { pending.resolve(accepted); expect(await first).toBe(true); });
  await act(async () => { await context!.commit("item", { kind: "union", type: "text", value: "potion" }); });
  expect(edit).toHaveBeenCalledTimes(2);
  expect(edit.mock.calls[1]![3]).toBe(2);
});

it("accepts a result before reporting its message and each warning", async () => {
  const { toast } = await mount();
  await act(async () => { await context!.acceptResult({ ...accepted, message: "Saved", warnings: ["First warning", "Second warning"] }); });
  expect(context!.store.state).toEqual(accepted.state);
  expect(toast.mock.calls).toEqual([["Saved"], ["First warning"], ["Second warning"]]);
});
