import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { afterEach, expect, it, vi } from "vitest";
import { api, ApiError } from "../../api/client";
import type { SaveDocument, Workspace } from "../../api/protocol";
import { WorkspacePage } from "./WorkspacePage";
import { deferred } from "../../test/deferred";

const clients: QueryClient[] = [];
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); vi.restoreAllMocks(); });

function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
  clients.push(client);
  render(<MemoryRouter initialEntries={["/w/w?doc=d"]}><QueryClientProvider client={client}>
    <Routes><Route path="/w/:workspaceId" element={<WorkspacePage />} /></Routes>
  </QueryClientProvider></MemoryRouter>);
  return client;
}

it.each([400, 409, 503, 0])("retains drafts, selection and live undo state after a background HTTP %s failure", async status => {
  const workspace: Workspace = { id: "w", name: "Test dump", createdAt: "2026-10-29", titles: [],
    documents: [{ id: "d", fileId: "f", filePath: "save.dat", title: "Save", pluginId: "test", isDirty: true }] };
  const snapshot: SaveDocument = { id: "d", fileId: "f", filePath: "save.dat", title: "Save", canEdit: true,
    plugin: { id: "test", name: "Test", version: "1", maturity: "Experimental", games: [], references: [] },
    state: { canUndo: false, canRedo: false, isDirty: false, revision: 1 },
    view: { type: "section", title: "Overview", children: [
      { type: "field", label: "Quantity", valueId: "quantity", value: "2", spec: { kind: "integer", min: "0", max: "99" } },
      { type: "queryableTable", title: "Items", collectionId: "items", totalCount: 1, columns: [{ key: "name", label: "Name" }] },
    ] } };
  vi.spyOn(api, "workspace").mockResolvedValue(workspace);
  vi.spyOn(api, "document").mockResolvedValue(snapshot);
  vi.spyOn(api, "sources").mockResolvedValue({ datasets: [] });
  vi.spyOn(api, "queryCollection").mockResolvedValue({ collectionId: "items", columns: [{ key: "name", label: "Name" }],
    totalCount: 1, filteredCount: 1, offset: 0, limit: 50, revision: 1, rows: [{ key: "a", cells: { name: { type: "text", text: "Potion" } } }] });
  vi.spyOn(api, "edit").mockResolvedValue({ ok: true, errors: [], warnings: [], structureChanged: false,
    changes: [{ valueId: "quantity", oldValue: "2", newValue: "3" }], state: { ...snapshot.state, canUndo: true, isDirty: true } });
  const undo = vi.spyOn(api, "undo").mockRejectedValue(new ApiError("Undo unavailable", 503));
  const client = mount();
  const field = await screen.findByRole<HTMLInputElement>("textbox", { name: "Quantity" });
  const selected = await screen.findByRole<HTMLInputElement>("checkbox", { name: "Select Potion" });
  fireEvent.click(selected);
  fireEvent.change(field, { target: { value: "3" } });
  fireEvent.keyDown(field, { key: "Enter" });
  await waitFor(() => expect(screen.getByRole("button", { name: "Undo" })).toHaveProperty("disabled", false));
  fireEvent.change(field, { target: { value: "9" } });
  field.focus();
  vi.mocked(api.workspace).mockRejectedValueOnce(new ApiError("Workspace temporarily unavailable", status));
  await act(async () => { await client.refetchQueries({ queryKey: ["workspace", "w"] }); });
  // Query notifications are scheduled separately from the completed network promise.
  expect((await screen.findByRole("alert")).textContent).toContain("Workspace temporarily unavailable");
  expect(screen.getByRole("textbox", { name: /Quantity/ })).toBe(field);
  expect(field.value).toBe("9");
  expect(selected.checked).toBe(true);
  expect(document.activeElement).toBe(field);
  expect(screen.getByRole("button", { name: "Undo" })).toHaveProperty("disabled", false);
  fireEvent.click(screen.getByRole("button", { name: "Retry workspace" }));
  await waitFor(() => expect(screen.queryByRole("alert")).toBeNull());
  expect(api.document).toHaveBeenCalledOnce();
  fireEvent.click(screen.getByRole("button", { name: "Undo" }));
  expect(await screen.findByRole("alert")).toHaveProperty("textContent", "Undo unavailable");
  expect(field.value).toBe("9");
  expect(selected.checked).toBe(true);
  expect(screen.getByRole("button", { name: "Undo" })).toHaveProperty("disabled", false);
  expect(undo).toHaveBeenCalledOnce();
});

it.each([404, 503])("distinguishes an initial HTTP %s failure and retries in place", async status => {
  vi.spyOn(api, "workspace").mockRejectedValueOnce(new ApiError("Workspace unavailable", status))
    .mockResolvedValue({ id: "w", name: "Recovered dump", createdAt: "2026-10-29", titles: [], documents: [] });
  mount();
  await screen.findByRole("heading", { name: status === 404 ? "This workspace is gone" : "Could not load this workspace" });
  fireEvent.click(screen.getByRole("button", { name: "Retry workspace" }));
  await screen.findByText("Recovered dump");
  expect(api.workspace).toHaveBeenCalledTimes(2);
});

it.each([404, 503])("retains useful focus when retrying a workspace still returns HTTP %s", async status => {
  const pending = deferred<Workspace>();
  vi.spyOn(api, "workspace").mockRejectedValueOnce(new ApiError("Workspace unavailable", status))
    .mockReturnValueOnce(pending.promise)
    .mockResolvedValue({ id: "w", name: "Recovered dump", createdAt: "2026-10-29", titles: [], documents: [] });
  mount();
  const headingName = status === 404 ? "This workspace is gone" : "Could not load this workspace";
  await screen.findByRole("heading", { name: headingName });
  const retry = screen.getByRole("button", { name: "Retry workspace" });
  retry.focus();
  fireEvent.click(retry);
  const loading = await screen.findByRole("status");
  expect(loading.textContent).toBe("Loading…");
  expect(document.activeElement).toBe(loading);
  await act(async () => { pending.reject(new ApiError("Still unavailable. Try again.", status)); });
  const again = await screen.findByRole("button", { name: "Retry workspace" });
  expect(screen.getByRole("alert").textContent).toBe("Still unavailable. Try again.");
  expect(document.activeElement).toBe(screen.getByRole("heading", { name: headingName }));
  fireEvent.click(again);
  await screen.findByText("Recovered dump");
  expect(document.activeElement).toBe(screen.getByRole("heading", { name: "Pick a save file" }));
});
