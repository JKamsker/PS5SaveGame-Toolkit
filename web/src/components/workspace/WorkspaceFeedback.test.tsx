import { defaultScheduler, notifyManager, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
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
  vi.spyOn(api, "workspace").mockResolvedValue(workspace);
  vi.spyOn(api, "document").mockResolvedValue(snapshot);
  vi.spyOn(api, "sources").mockResolvedValue({ datasets: [] });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  clients.push(client);
  render(<MemoryRouter initialEntries={["/w/w?doc=d"]}><QueryClientProvider client={client}>
    <Routes><Route path="/w/:workspaceId" element={<WorkspacePage />} /></Routes>
  </QueryClientProvider></MemoryRouter>);
  await screen.findByRole("switch", { name: "Flag" });
  return snapshot.state;
}

it.each([true, false])("renders every diagnostic from one edit instead of overwriting earlier feedback (ok=%s)", async ok => {
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
  const messages = [...(message ? [message] : []), ...warnings, ...(ok ? [] : [failure])];
  for (const text of messages) {
    const isError = text === failure;
    expect(screen.getByRole(isError ? "alert" : "status")).toHaveProperty("textContent", text);
    await act(async () => { await vi.advanceTimersByTimeAsync(isError ? 9000 : 4500); });
  }
  expect(screen.queryByRole("status")).toBeNull();
  expect(screen.queryByRole("alert")).toBeNull();
});
