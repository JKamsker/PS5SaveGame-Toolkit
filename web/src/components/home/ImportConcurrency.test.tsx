import { defaultScheduler, notifyManager, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { Link, MemoryRouter, Route, Routes, useLocation } from "react-router";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { api } from "../../api/client";
import type { Workspace } from "../../api/protocol";
import * as libraryStore from "../../state/libraryStore";
import { deferred } from "../../test/deferred";
import { HomePage } from "./HomePage";

const clients: QueryClient[] = [];
beforeEach(() => { notifyManager.setScheduler(queueMicrotask); });
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); vi.restoreAllMocks(); notifyManager.setScheduler(defaultScheduler); });
const ws: Workspace = { id: "new", name: "Dump", createdAt: "2026-10-29", documents: [], titles: [] };
function Location() { return <output aria-label="Location">{useLocation().pathname}</output>; }
async function mount() {
  vi.spyOn(libraryStore.library, "restore").mockResolvedValue(undefined);
  vi.spyOn(api, "plugins").mockResolvedValue([]); vi.spyOn(api, "titles").mockResolvedValue([]); vi.spyOn(api, "workspaces").mockResolvedValue([]);
  vi.spyOn(libraryStore.library, "importedWorkspace").mockReturnValue("remembered");
  vi.spyOn(libraryStore, "useLibrary").mockReturnValue({ status: "ready", folderName: "Folder", persistent: false, skipped: 0, error: null, icons: {},
    games: ["First game", "Second game"].map(key => ({ key, titleId: null, fallbackName: key, kind: "zip", sources: [], saveDirs: 1, totalBytes: 1, icon: null })) });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } }); clients.push(client);
  await act(async () => { render(<MemoryRouter><QueryClientProvider client={client}><Link to="/away">Leave</Link><Location /><Routes>
    <Route path="/" element={<HomePage />} /><Route path="/away" element={<h1>Away</h1>} /><Route path="/w/:workspaceId" element={<h1>Workspace</h1>} />
  </Routes></QueryClientProvider></MemoryRouter>); });
}
function chooseZip() { fireEvent.change(document.querySelector('input[accept=".zip"]')!, { target: { files: [new File(["fixture"], "save.zip")] } }); }

it("accepts a ZIP import once while its request is pending", async () => {
  const pending = deferred<Workspace>(); const upload = vi.spyOn(api, "upload").mockReturnValue(pending.promise);
  await mount();
  act(() => { chooseZip(); chooseZip(); });
  expect(upload).toHaveBeenCalledOnce();
  await act(async () => { pending.resolve(ws); await pending.promise; });
  expect(screen.getByLabelText("Location").textContent).toBe("/w/new");
});

it("guards the remembered-workspace lookup and excludes a simultaneous ZIP/tile import", async () => {
  const pending = deferred<Workspace>(); const check = vi.spyOn(api, "workspace").mockReturnValue(pending.promise);
  const upload = vi.spyOn(api, "upload");
  await mount();
  act(() => {
    const game = screen.getByRole("button", { name: /First game/ });
    fireEvent.click(game); fireEvent.click(game);
    fireEvent.click(screen.getByRole("button", { name: /Second game/ }));
    chooseZip();
  });
  expect(check).toHaveBeenCalledOnce();
  expect(upload).not.toHaveBeenCalled();
  await act(async () => { pending.resolve(ws); await pending.promise; });
  expect(screen.getByLabelText("Location").textContent).toBe("/w/remembered");
});

it.each(["zip", "remembered"])("ignores a completed %s import after leaving home", async kind => {
  const pending = deferred<Workspace>();
  vi.spyOn(api, "upload").mockReturnValue(pending.promise); vi.spyOn(api, "workspace").mockReturnValue(pending.promise);
  await mount();
  act(() => { if (kind === "zip") chooseZip(); else fireEvent.click(screen.getByRole("button", { name: /First game/ })); });
  fireEvent.click(screen.getByRole("link", { name: "Leave" }));
  await act(async () => { pending.resolve(ws); await pending.promise; });
  expect(screen.getByLabelText("Location").textContent).toBe("/away");
});
