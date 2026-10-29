import { defaultScheduler, notifyManager, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { Link, MemoryRouter, Route, Routes, useLocation } from "react-router";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { api, ApiError } from "../../api/client";
import type { SaveDocument, SaveFileInfo, Workspace } from "../../api/protocol";
import { deferred } from "../../test/deferred";
import { WorkspacePage } from "./WorkspacePage";

const files = ["first", "second", "cached"].map(id => ({ id, path: id, length: 1, matches: [],
  openDocumentId: id === "cached" ? "cached" : undefined } satisfies SaveFileInfo));
vi.mock("./SlotRail", () => ({ SlotRail: ({ onOpen }: { onOpen: (file: SaveFileInfo) => void }) =>
  <nav>{files.map(file => <button key={file.id} onClick={() => onOpen(file)}>{file.id}</button>)}</nav> }));
vi.mock("../document/DocumentEditor", () => ({ DocumentEditor: ({ documentId, onSwitchPlugin }: { documentId: string; onSwitchPlugin: (id: string) => void }) =>
  <div>Editor {documentId}<button onClick={() => onSwitchPlugin("plugin-a")}>Plugin A</button><button onClick={() => onSwitchPlugin("plugin-b")}>Plugin B</button></div> }));

const clients: QueryClient[] = [];
beforeEach(() => { notifyManager.setScheduler(queueMicrotask); Object.defineProperty(HTMLDialogElement.prototype, "close", { configurable: true, value() {} }); });
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); vi.restoreAllMocks(); notifyManager.setScheduler(defaultScheduler); Reflect.deleteProperty(HTMLDialogElement.prototype, "close"); });
const doc = (id: string) => ({ id, fileId: id, state: { isDirty: false } }) as SaveDocument;
const ws: Workspace = { id: "w", name: "Dump", createdAt: "2026-10-29", titles: [{ titleId: "PPSA99999", directories: [
  { id: "dir", name: "slot", rawName: "slot", isBackup: false, generation: 0, twinIdentical: false, hasIcon: false, files },
] }], documents: files.map(file => ({ id: file.id, fileId: file.id, filePath: file.path, title: file.path, pluginId: "test", isDirty: false })) };
function Location() { return <output aria-label="Location">{useLocation().pathname}{useLocation().search}</output>; }
async function mount() {
  vi.spyOn(api, "workspace").mockResolvedValue(ws);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  clients.push(client);
  await act(async () => { render(<MemoryRouter initialEntries={["/w/w?doc=first"]}><QueryClientProvider client={client}>
    <Link to="/">Leave</Link><Location /><Routes><Route path="/w/:workspaceId" element={<WorkspacePage />} /><Route path="/" element={<h1>Home</h1>} /></Routes>
  </QueryClientProvider></MemoryRouter>); });
}

it("ignores an older file-open success after the newer selection has opened", async () => {
  const first = deferred<SaveDocument>(); const second = deferred<SaveDocument>();
  vi.spyOn(api, "open").mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);
  await mount();
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: "first" })); });
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: "second" })); });
  await act(async () => { second.resolve(doc("second")); await second.promise; });
  expect(screen.getByLabelText("Location").textContent).toBe("/w/w?doc=second");
  await act(async () => { first.resolve(doc("first")); await first.promise; });
  expect(screen.getByLabelText("Location").textContent).toBe("/w/w?doc=second");
});

it("coalesces repeated opens and retires the pending response when selecting a cached document", async () => {
  const pending = deferred<SaveDocument>();
  const open = vi.spyOn(api, "open").mockReturnValue(pending.promise);
  await mount();
  await act(async () => { const button = screen.getByRole("button", { name: "second" }); fireEvent.click(button); fireEvent.click(button); });
  expect(open).toHaveBeenCalledOnce();
  fireEvent.click(screen.getByRole("button", { name: "cached" }));
  await act(async () => { pending.resolve(doc("second")); await pending.promise; });
  expect(screen.getByLabelText("Location").textContent).toBe("/w/w?doc=cached");
});

it("serializes replacement-plugin opens for the same file", async () => {
  const first = deferred<SaveDocument>(); const second = deferred<SaveDocument>();
  const open = vi.spyOn(api, "open").mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);
  await mount();
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: "Plugin A" })); });
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: "Plugin B" })); });
  expect(open).toHaveBeenCalledTimes(1);
  await act(async () => { first.resolve(doc("first")); await first.promise; });
  expect(open).toHaveBeenCalledTimes(2);
  expect(open.mock.calls.map(call => call[2])).toEqual(["plugin-a", "plugin-b"]);
  await act(async () => { second.resolve(doc("first")); await second.promise; });
});

it.each(["resolve", "reject"] as const)("suppresses an open %s after leaving the workspace", async outcome => {
  const pending = deferred<SaveDocument>();
  vi.spyOn(api, "open").mockReturnValue(pending.promise);
  await mount();
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: "second" })); });
  fireEvent.click(screen.getByRole("link", { name: "Leave" }));
  await act(async () => { if (outcome === "resolve") pending.resolve(doc("second")); else pending.reject(new ApiError("Old failure", 409)); await pending.promise.catch(() => {}); });
  expect(screen.getByLabelText("Location").textContent).toBe("/");
  expect(screen.getByRole("heading", { name: "Home" })).toBeTruthy();
});
