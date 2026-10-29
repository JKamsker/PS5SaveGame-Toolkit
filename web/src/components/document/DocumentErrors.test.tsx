import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { api, ApiError } from "../../api/client";
import type { EditResult, FieldNode, SaveDocument } from "../../api/protocol";
import { DocumentEditor } from "./DocumentEditor";

const clients: QueryClient[] = [];
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); vi.restoreAllMocks(); });

const state = { canUndo: true, canRedo: false, isDirty: true, revision: 1 };
const field = (): FieldNode => ({ type: "field", label: "Quantity", valueId: "quantity", value: "2",
  spec: { kind: "integer", min: "0", max: "99" } });
function snapshot(lazy: boolean, revision = 1): SaveDocument {
  return { id: "d", fileId: "f", filePath: "save.dat", title: "Save", canEdit: true,
    plugin: { id: "test", name: "Test", version: "1", maturity: "Experimental", games: [], references: [] },
    state: { ...state, revision }, view: lazy ? { type: "section", title: "Inventory", lazyId: "items", children: [] } : field() };
}
const rejected = (extra: Partial<EditResult> = {}): EditResult => ({ ok: false, errors: [], changes: [], warnings: [],
  structureChanged: false, state, ...extra });

function mount(lazy = false) {
  vi.spyOn(api, "sources").mockResolvedValue({ datasets: [] });
  vi.spyOn(api, "expand").mockImplementation(async () => [field()]);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
  clients.push(client);
  const toast = vi.fn();
  render(<QueryClientProvider client={client}><DocumentEditor workspaceId="w" documentId="d" file={undefined}
    onSwitchPlugin={vi.fn()} onToast={toast} /></QueryClientProvider>);
  return { client, toast, lazy };
}

it.each([false, true])("keeps a rejected draft through stale revision recovery (lazy=%s)", async lazy => {
  vi.spyOn(api, "document").mockResolvedValueOnce(snapshot(lazy)).mockResolvedValue(snapshot(lazy, 2));
  const edit = vi.spyOn(api, "edit").mockResolvedValueOnce(rejected({ stale: true, state: { ...state, revision: 2 } }))
    .mockResolvedValue({ ...rejected(), ok: true, changes: [{ valueId: "quantity", oldValue: "2", newValue: "9" }], state: { ...state, revision: 2 } });
  const { toast } = mount(lazy);
  const input = await screen.findByRole<HTMLInputElement>("textbox", { name: /Quantity/ });
  fireEvent.change(input, { target: { value: "9" } });
  fireEvent.keyDown(input, { key: "Enter" });
  await waitFor(() => expect(toast).toHaveBeenCalledWith(expect.stringContaining("document changed"), true));
  const current = await screen.findByRole<HTMLInputElement>("textbox", { name: /Quantity/ });
  expect(current).toBe(input);
  expect(current.value).toBe("9");
  expect(edit).toHaveBeenCalledOnce();
  fireEvent.keyDown(current, { key: "Enter" });
  await waitFor(() => expect(edit).toHaveBeenCalledTimes(2));
  expect(edit.mock.calls.map(call => call[3])).toEqual([1, 2]);
  expect(edit.mock.calls[1]![2]).toEqual([{ valueId: "quantity", value: "9" }]);
});

it.each([400, 409, 422, 503, 0])("keeps draft/history after HTTP %s edit failure and supports explicit retry", async status => {
  vi.spyOn(api, "document").mockResolvedValue(snapshot(false));
  const edit = vi.spyOn(api, "edit").mockRejectedValueOnce(new ApiError("Edit unavailable; try again", status))
    .mockResolvedValue({ ...rejected(), ok: true, changes: [{ valueId: "quantity", oldValue: "2", newValue: "9" }] });
  const { toast } = mount();
  const input = await screen.findByRole<HTMLInputElement>("textbox", { name: /Quantity/ });
  fireEvent.change(input, { target: { value: "9" } });
  fireEvent.keyDown(input, { key: "Enter" });
  await waitFor(() => expect(input.getAttribute("aria-invalid")).toBe("true"));
  expect(input.value).toBe("9");
  expect(toast).toHaveBeenCalledWith("Edit unavailable; try again", true);
  expect(screen.getByRole("button", { name: "Undo" })).toHaveProperty("disabled", false);
  expect(api.document).toHaveBeenCalledOnce();
  expect(edit).toHaveBeenCalledOnce();
  fireEvent.keyDown(input, { key: "Enter" });
  await waitFor(() => expect(input.getAttribute("aria-invalid")).toBe("false"));
  expect(edit).toHaveBeenCalledTimes(2);
});

it("retries the initial document load without navigating away", async () => {
  vi.spyOn(api, "document").mockRejectedValueOnce(new ApiError("Document unavailable", 503)).mockResolvedValue(snapshot(false));
  mount();
  expect(await screen.findByRole("alert")).toHaveProperty("textContent", "Document unavailable");
  fireEvent.click(screen.getByRole("button", { name: "Retry document" }));
  await screen.findByRole("textbox", { name: /Quantity/ });
  expect(api.document).toHaveBeenCalledTimes(2);
});

it("retains a loaded lazy field through a failed background read and retry", async () => {
  vi.spyOn(api, "document").mockResolvedValue(snapshot(true));
  const { client } = mount(true);
  const input = await screen.findByRole<HTMLInputElement>("textbox", { name: /Quantity/ });
  fireEvent.change(input, { target: { value: "9" } });
  vi.mocked(api.expand).mockRejectedValueOnce(new ApiError("Inventory unavailable", 400));
  await act(async () => { await client.refetchQueries({ queryKey: ["nodes", "w", "d"] }); });
  expect(await screen.findByRole("alert")).toHaveProperty("textContent", "Inventory unavailable");
  expect(screen.getByRole("textbox", { name: /Quantity/ })).toBe(input);
  expect(input.value).toBe("9");
  fireEvent.click(screen.getByRole("button", { name: "Retry section" }));
  await waitFor(() => expect(screen.queryByRole("alert")).toBeNull());
  expect(input.value).toBe("9");
  expect(api.expand).toHaveBeenCalledTimes(3);
});

it("retains a lazy draft if the nodes fail after a document generation refresh", async () => {
  vi.spyOn(api, "document").mockImplementation(async () => snapshot(true));
  const { client } = mount(true);
  const input = await screen.findByRole<HTMLInputElement>("textbox", { name: /Quantity/ });
  fireEvent.change(input, { target: { value: "9" } });
  vi.mocked(api.expand).mockRejectedValueOnce(new ApiError("Inventory unavailable", 400));
  await act(async () => { await client.refetchQueries({ queryKey: ["document", "w", "d"] }); });
  expect(await screen.findByRole("alert")).toHaveProperty("textContent", "Inventory unavailable");
  expect(input.isConnected).toBe(true);
  expect(input.value).toBe("9");
  // Old generation controls remain visible but inert until current bindings are loaded.
  expect(input.closest("[inert]")).not.toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "Retry section" }));
  await waitFor(() => expect(screen.queryByRole("alert")).toBeNull());
  expect(screen.getByRole("textbox", { name: /Quantity/ })).toBe(input);
  expect(input.closest("[inert]")).toBeNull();
  expect(input.value).toBe("9");
});
