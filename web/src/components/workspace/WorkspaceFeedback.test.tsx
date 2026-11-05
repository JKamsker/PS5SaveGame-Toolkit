import { defaultScheduler, notifyManager, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { Link, MemoryRouter, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import { api } from "../../api/client";
import type { SaveDocument, Workspace } from "../../api/protocol";
import { WorkspacePage } from "./WorkspacePage";

const clients: QueryClient[] = [];
beforeAll(async () => { await import("../document/DocumentEditor"); });
beforeEach(() => { notifyManager.setScheduler(queueMicrotask); });
afterEach(() => {
  cleanup(); clients.splice(0).forEach(client => client.clear());
  vi.restoreAllMocks(); vi.useRealTimers(); notifyManager.setScheduler(defaultScheduler);
});

async function mount() {
  const workspace: Workspace = { id: "w", name: "Test dump", createdAt: "", titles: [], documents: [
    { id: "d", fileId: "f", title: "Save", filePath: "save.dat", pluginId: "test", isDirty: false },
  ] };
  const snapshot: SaveDocument = {
    id: "d", fileId: "f", title: "Save", filePath: "save.dat", canEdit: true,
    plugin: { id: "test", name: "Test", version: "1", maturity: "Experimental", games: [], references: [] },
    state: { revision: 1, isDirty: false, canUndo: false, canRedo: false },
    view: { type: "field", valueId: "flag", label: "Flag", value: false, spec: { kind: "bool" } },
  };
  vi.spyOn(api, "workspace").mockImplementation(async id => ({ ...workspace, id }));
  vi.spyOn(api, "document").mockResolvedValue(snapshot);
  vi.spyOn(api, "sources").mockResolvedValue({ datasets: [] });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  clients.push(client);
  render(<MemoryRouter initialEntries={["/w/w?doc=d"]}><QueryClientProvider client={client}>
    <Link to="/w/other">Other workspace</Link>
    <Routes><Route path="/w/:workspaceId" element={<WorkspacePage />} /></Routes>
  </QueryClientProvider></MemoryRouter>);
  await screen.findByRole("switch", { name: "Flag" });
  return snapshot.state;
}

const texts = (role: "status" | "alert") => screen.queryAllByRole(role).map(element => element.textContent);

it("retires notifications when switching workspace on the same route", async () => {
  const state = await mount();
  vi.spyOn(api, "edit").mockResolvedValue({
    ok: true, errors: [], warnings: ["Warning from the first workspace"], changes: [],
    structureChanged: false, state,
  });
  await act(async () => { fireEvent.click(screen.getByRole("switch", { name: "Flag" })); });
  expect(screen.getByText("Warning from the first workspace")).toBeTruthy();
  await act(async () => { fireEvent.click(screen.getByRole("link", { name: "Other workspace" })); });
  await screen.findByRole("heading", { name: "Pick a save file" });
  expect(screen.queryByText("Warning from the first workspace")).toBeNull();
});

it.each([true, false])("shows every diagnostic from one edit at once and expires each on its own interval (ok=%s)", async ok => {
  const state = await mount();
  const warnings = ["Earlier undo history was cleared.", "Some original fields could not be matched."];
  const message = ok ? "Flag updated." : undefined;
  const failure = "The edit could not be completed.";
  vi.spyOn(api, "edit").mockResolvedValue({
    ok, errors: ok ? [] : [{ valueId: "", message: failure }], warnings, message,
    changes: ok ? [{ valueId: "flag", oldValue: false, newValue: true }] : [],
    structureChanged: false, state: { ...state, isDirty: ok },
  });
  vi.useFakeTimers();
  await act(async () => { fireEvent.click(screen.getByRole("switch", { name: "Flag" })); });
  // Nothing waits behind earlier feedback: a confirmation emitted after several warnings is visible immediately.
  expect(texts("status")).toEqual([...(message ? [message] : []), ...warnings]);
  expect(texts("alert")).toEqual(ok ? [] : [failure]);
  await act(async () => { await vi.advanceTimersByTimeAsync(4500); });
  expect(texts("status")).toEqual([]);
  expect(texts("alert")).toEqual(ok ? [] : [failure]);
  await act(async () => { await vi.advanceTimersByTimeAsync(4500); });
  expect(texts("alert")).toEqual([]);
});

it("keeps identical consecutive diagnostics as separate notifications", async () => {
  const state = await mount();
  const warning = "Earlier undo history was cleared.";
  vi.spyOn(api, "edit").mockResolvedValue({
    ok: true, errors: [], warnings: [warning, warning], message: undefined,
    changes: [{ valueId: "flag", oldValue: false, newValue: true }],
    structureChanged: false, state: { ...state, isDirty: true },
  });
  vi.useFakeTimers();
  await act(async () => { fireEvent.click(screen.getByRole("switch", { name: "Flag" })); });
  expect(texts("status")).toEqual([warning, warning]);
  await act(async () => { await vi.advanceTimersByTimeAsync(4500); });
  expect(texts("status")).toEqual([]);
});
