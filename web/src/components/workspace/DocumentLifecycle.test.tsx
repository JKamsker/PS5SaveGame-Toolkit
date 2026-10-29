import { defaultScheduler, notifyManager, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { api, ApiError } from "../../api/client";
import { groupApi } from "../../api/groups";
import type { DocumentSource, EditResult, SaveDocument, Workspace } from "../../api/protocol";
import { deferred } from "../../test/deferred";
import { WorkspacePage } from "./WorkspacePage";
import { documentClosures } from "../document/documentClosures";

const clients: QueryClient[] = [];
beforeEach(() => { notifyManager.setScheduler(queueMicrotask); });
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); vi.restoreAllMocks(); notifyManager.setScheduler(defaultScheduler); });
const clean = { revision: 0, isDirty: false, canUndo: false, canRedo: false };
const summary = (id: string, fileId: string, isDirty = false) => ({ id, fileId, filePath: `${fileId}.dat`, title: id, pluginId: "test", isDirty });
const source = (version: string): DocumentSource => ({ packId: "synthetic", packTitle: `Synthetic pack ${version}`, packVersion: version,
  origin: "User", datasetId: "items", kind: "Records", itemCount: 1, used: true,
  attribution: { author: "Test fixture", license: "CC0", licenseUrl: "https://example.org/license", sourceUrl: "https://example.org/source",
    retrieved: "2026-10-29", modifications: "Synthetic fixture." } });
function Location() { return <output aria-label="Selection">{useLocation().search}</output>; }

async function mount(dirty = false) {
  let pack = "1";
  const pinned = new Map([["old", "1"], ["other", "1"]]);
  const ws: Workspace = { id: "w", name: "Synthetic dump", createdAt: "2026-10-29", documents: [summary("old", "save", dirty), summary("other", "other", true)],
    titles: [{ titleId: "PPSA99999", directories: [{ id: "slot", name: "Slot", rawName: "sdimg_SLOT", isBackup: false,
      generation: 0, twinIdentical: false, hasIcon: false, files: ["save", "other"].map(id => ({ id, path: `${id}.dat`, length: 3,
        openDocumentId: id === "save" ? "old" : "other", matches: [{ pluginId: "test", pluginName: "Synthetic", score: 10, maturity: "Experimental" }] })) }] }] };
  const doc = (id: string): SaveDocument => ({ id, fileId: id === "other" ? "other" : "save", filePath: "save.dat", title: `Document ${id}`, canEdit: true,
    plugin: { id: "test", name: "Synthetic", version: "1", maturity: "Experimental", games: [], references: [] },
    state: { ...clean, isDirty: id === "other" || dirty },
    view: { type: "field", label: "Flag", valueId: "flag", value: false, spec: { kind: "bool" } } });
  vi.spyOn(api, "workspace").mockImplementation(async () => structuredClone(ws));
  vi.spyOn(api, "document").mockImplementation(async (_w, id) => doc(id));
  vi.spyOn(api, "sources").mockImplementation(async (_w, id) => ({ datasets: [source(pinned.get(id)!)] }));
  vi.spyOn(api, "importReferencePack").mockImplementation(async () => {
    pack = "2";
    return { id: "synthetic", version: "2", title: "Synthetic pack 2", origin: "User", pluginIds: ["test"], titleIds: [], gameBuilds: [], datasets: [] };
  });
  const close = vi.spyOn(api, "closeDocument").mockImplementation(async (_w, id) => {
    ws.documents = ws.documents.filter(document => document.id !== id);
    for (const file of ws.titles[0]!.directories[0]!.files) if (file.openDocumentId === id) delete file.openDocumentId;
  });
  const open = vi.spyOn(api, "open").mockImplementation(async () => {
    pinned.set("new", pack);
    ws.documents.push(summary("new", "save"));
    ws.titles[0]!.directories[0]!.files[0]!.openDocumentId = "new";
    return doc("new");
  });
  const propose = vi.spyOn(groupApi, "propose");
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } }); clients.push(client);
  client.setQueryData(["document", "w", "other"], doc("other"));
  await act(async () => { render(<MemoryRouter initialEntries={["/w/w?doc=old"]}><QueryClientProvider client={client}><Location /><Routes>
    <Route path="/w/:workspaceId" element={<WorkspacePage />} />
  </Routes></QueryClientProvider></MemoryRouter>); });
  await screen.findByRole("heading", { name: "Document old" });
  return { ws, client, close, open, propose };
}

it("closes and reopens a non-group document to use a new pack without replacing live pins", async () => {
  const { client, ws, close, open, propose } = await mount();
  expect(screen.queryByRole("button", { name: "Choose related files" })).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: /Sources and attribution/ }));
  await screen.findByText(/Synthetic pack 1/);
  await act(async () => { fireEvent.change(screen.getByLabelText(/Import reference pack/), { target: { files: [new File(["synthetic"], "pack.zip")] } }); });
  expect(screen.getByText(/Imported.*Synthetic pack 2.*Re-open/)).toBeTruthy();
  expect(screen.getByText(/Synthetic pack 1/)).toBeTruthy();
  expect(open).not.toHaveBeenCalled();
  for (const kind of ["nodes", "sources", "blob", "collection", "changes"]) {
    client.setQueryData([kind, "w", "old", "fixture"], { synthetic: true });
    client.setQueryData([kind, "w", "other", "fixture"], { keep: true });
  }
  const button = screen.getByRole("button", { name: "Close document" }); button.focus();
  await act(async () => { fireEvent.click(button); });
  expect(close).toHaveBeenCalledExactlyOnceWith("w", "old");
  const picker = await screen.findByRole("heading", { name: "Pick a save file" });
  expect(document.activeElement).toBe(picker);
  expect(screen.getByLabelText("Selection").textContent).toBe("");
  expect(ws.documents).toEqual([summary("other", "other", true)]);
  expect(client.getQueryCache().findAll().filter(query => query.queryKey[1] === "w" && query.queryKey[2] === "old")).toHaveLength(0);
  expect(client.getQueryData(["nodes", "w", "other", "fixture"])).toEqual({ keep: true });
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: /save.dat/ })); });
  await screen.findByRole("heading", { name: "Document new" });
  fireEvent.click(screen.getByRole("button", { name: /Sources and attribution/ }));
  expect(await screen.findByText(/^Synthetic pack 2 2/)).toBeTruthy();
  expect(screen.getByLabelText("Selection").textContent).toBe("?doc=new");
  expect(propose).not.toHaveBeenCalled();
});

it("keeps dirty ordinary documents open and explains how to recover", async () => {
  const { close } = await mount(true);
  const button = screen.getByRole("button", { name: "Close document" });
  expect(button).toHaveProperty("disabled", true);
  expect(document.getElementById(button.getAttribute("aria-describedby")!)?.textContent).toMatch(/Undo.*export/);
  fireEvent.click(button);
  expect(close).not.toHaveBeenCalled();
});

it("keeps a dirty live document's pack pinned after importing a newer pack", async () => {
  const { close, open } = await mount(true);
  fireEvent.click(screen.getByRole("button", { name: /Sources and attribution/ }));
  await screen.findByText(/Synthetic pack 1/);
  await act(async () => { fireEvent.change(screen.getByLabelText(/Import reference pack/), { target: { files: [new File(["synthetic"], "pack.zip")] } }); });
  expect(screen.getByText(/Synthetic pack 1/)).toBeTruthy();
  expect(screen.getByRole("button", { name: "Close document" })).toHaveProperty("disabled", true);
  expect(close).not.toHaveBeenCalled(); expect(open).not.toHaveBeenCalled();
});

it("blocks close while an edit is pending, then keeps the acknowledged dirty document", async () => {
  const edit = deferred<EditResult>(); vi.spyOn(api, "edit").mockReturnValue(edit.promise);
  const { close } = await mount();
  await act(async () => { fireEvent.click(screen.getByRole("switch", { name: "Flag" })); fireEvent.click(screen.getByRole("button", { name: "Close document" })); });
  expect(screen.getByRole("alert").textContent).toMatch(/Wait for pending edits/);
  expect(close).not.toHaveBeenCalled();
  await act(async () => { edit.resolve({ ok: true, changes: [{ valueId: "flag", oldValue: false, newValue: true }], warnings: [], errors: [],
    structureChanged: false, state: { ...clean, revision: 1, isDirty: true, canUndo: true } }); await edit.promise; });
  expect(screen.getByRole("button", { name: "Close document" })).toHaveProperty("disabled", true);
  expect(screen.getByRole("switch", { name: /Flag/ }).getAttribute("aria-checked")).toBe("true");
});

it("retains selection and caches after close failure and permits one explicit retry", async () => {
  const { close, client } = await mount();
  const redo = vi.spyOn(api, "redo");
  await act(async () => { client.setQueryData<SaveDocument>(["document", "w", "old"], previous => previous && ({ ...previous, state: { ...previous.state, canRedo: true } })); });
  const pending = deferred<void>(); close.mockReturnValueOnce(pending.promise);
  const button = screen.getByRole("button", { name: "Close document" }); button.focus();
  await act(async () => { fireEvent.click(button); fireEvent.click(button); });
  expect(close).toHaveBeenCalledOnce();
  expect(screen.getByRole("status", { name: "Document close status" }).textContent).toMatch(/Closing document/);
  expect(screen.queryByRole("switch")).toBeNull();
  fireEvent.keyDown(window, { key: "z", ctrlKey: true, shiftKey: true });
  expect(redo).not.toHaveBeenCalled();
  await act(async () => { pending.reject(new ApiError("Could not close. Try again.", 503)); await pending.promise.catch(() => {}); });
  expect((await screen.findByRole("alert")).textContent).toContain("Could not close");
  expect(screen.getByLabelText("Selection").textContent).toBe("?doc=old");
  expect(client.getQueryData(["document", "w", "old"])).toBeDefined();
  const retry = await screen.findByRole("button", { name: "Close document" });
  expect(document.activeElement).toBe(retry);
  await act(async () => { fireEvent.click(retry); });
  expect(close).toHaveBeenCalledTimes(2);
  expect(await screen.findByRole("heading", { name: "Pick a save file" })).toBeTruthy();
});

it("refreshes ownership without replacing a newer selection when close finishes late", async () => {
  const { close, client } = await mount();
  const pending = deferred<void>(); close.mockReturnValueOnce(pending.promise);
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: "Close document" })); });
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: /other.dat/ })); });
  await screen.findByRole("heading", { name: "Document other" });
  await act(async () => { pending.resolve(); await pending.promise; });
  expect(screen.getByLabelText("Selection").textContent).toBe("?doc=other");
  expect(client.getQueryData(["document", "w", "other"])).toBeDefined();
});

it("releases the interaction guard when the same document is reselected during a failed close", async () => {
  const { close } = await mount();
  const pending = deferred<void>(); close.mockReturnValueOnce(pending.promise);
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: "Close document" })); });
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: /save.dat/ })); });
  await act(async () => { pending.reject(new ApiError("Retired close failure", 503)); await pending.promise.catch(() => {}); });
  expect(await screen.findByRole("heading", { name: "Document old" })).toBeTruthy();
  expect(screen.getByRole("article").hasAttribute("inert")).toBe(false);
  expect(screen.getByRole("button", { name: "Close document" })).toHaveProperty("disabled", false);
  expect(screen.getByRole("alert").textContent).toBe("Retired close failure");
});

it.each(["success", "failure"])("keeps A protected across A → B → A until deferred close %s settles, preserving B", async outcome => {
  const { close, client, ws } = await mount();
  const executeClose = close.getMockImplementation()!;
  const gate = deferred<void>();
  close.mockImplementationOnce(async (...args) => { await gate.promise; await executeClose(...args); });
  const read = vi.mocked(api.document).getMockImplementation()!;
  let bEdited = false;
  vi.mocked(api.document).mockImplementation(async (...args) => {
    const doc = await read(...args);
    if (doc.id === "old") doc.state.canRedo = true;
    if (doc.id === "other" && doc.view.type === "field") doc.view.value = bEdited;
    return doc;
  });
  const edit = vi.spyOn(api, "edit").mockImplementation(async (_workspace, id) => {
    if (id !== "other") throw new Error("Closing A must not submit edits.");
    bEdited = true;
    return { ok: true, errors: [], warnings: [], changes: [{ valueId: "flag", oldValue: false, newValue: true }],
      structureChanged: false, state: { ...clean, revision: 1, isDirty: true, canUndo: true } };
  });
  const redo = vi.spyOn(api, "redo");
  await act(async () => { await client.refetchQueries({ queryKey: ["document", "w", "old"] }); });
  const oldClose = screen.getByRole("button", { name: "Close document" });
  await act(async () => { fireEvent.click(oldClose); });
  expect(documentClosures(client).get("w", "old").pending).toBe(true);
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: /other.dat/ })); });
  await screen.findByRole("heading", { name: "Document other" });
  await act(async () => { fireEvent.click(screen.getByRole("switch", { name: "Flag" })); });
  expect(edit).toHaveBeenCalledOnce();
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: /save.dat/ })); });
  // A has actually unmounted for B. A fresh successful read must not reactivate it.
  await act(async () => { await client.fetchQuery({ queryKey: ["document", "w", "old"], queryFn: () => api.document("w", "old") }); });
  expect(screen.getByRole("status", { name: "Document close status" })).toBeTruthy();
  expect(screen.queryByRole("switch")).toBeNull();
  expect(screen.queryByRole("button", { name: "Redo" })).toBeNull();
  expect(screen.queryByRole("button", { name: "Close document" })).toBeNull();
  fireEvent.keyDown(window, { key: "z", ctrlKey: true, shiftKey: true });
  fireEvent.click(oldClose); fireEvent.click(oldClose);
  expect(redo).not.toHaveBeenCalled(); expect(close).toHaveBeenCalledOnce();
  expect(edit.mock.calls.map(call => call[1])).toEqual(["other"]);
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: /other.dat/ })); });
  await screen.findByRole("heading", { name: "Document other" });
  await act(async () => {
    if (outcome === "success") gate.resolve(); else gate.reject(new ApiError("Close rejected; A is still open.", 409));
    await gate.promise.catch(() => {});
  });
  expect(documentClosures(client).get("w", "old").pending).toBe(false);
  expect(screen.getByLabelText("Selection").textContent).toBe("?doc=other");
  expect(screen.getByRole("switch", { name: /Flag/ }).getAttribute("aria-checked")).toBe("true");
  expect(ws.documents.find(document => document.id === "other")?.isDirty).toBe(true);
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: /save.dat/ })); });
  if (outcome === "failure") {
    await screen.findByRole("heading", { name: "Document old" });
    expect(screen.getByRole("alert").textContent).toMatch(/Close rejected/);
    expect(screen.getByRole("button", { name: "Close document" })).toHaveProperty("disabled", false);
    await act(async () => { fireEvent.click(screen.getByRole("button", { name: "Close document" })); });
    expect(close).toHaveBeenCalledTimes(2);
  } else {
    await screen.findByRole("heading", { name: "Document new" });
    expect(screen.getByRole("button", { name: "Close document" })).toHaveProperty("disabled", false);
  }
});
