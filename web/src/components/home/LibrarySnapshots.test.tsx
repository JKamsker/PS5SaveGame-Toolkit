import { File as NodeFile } from "node:buffer";
import { zipSync, strToU8 } from "fflate";
import { defaultScheduler, notifyManager, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, useLocation } from "react-router";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { api } from "../../api/client";
import type { Workspace } from "../../api/protocol";
import { library } from "../../state/libraryStore";
import { deferred } from "../../test/deferred";
import { HomePage } from "./HomePage";

const clients: QueryClient[] = [];
const path = "PPSA99999/sdimg_SLOT/save.dat";
const workspace = (id: string): Workspace => ({ id, name: "Synthetic dump", createdAt: "2026-10-29", documents: [], titles: [] });
const folder = (bytes: string) => [{ path, file: new File([bytes], "save.dat") }];
function zip(bytes: string) {
  const data = zipSync({ [path]: strToU8(bytes) }, { level: 0 });
  // Node's File supports sliced arrayBuffer reads used by the real ZIP indexer in jsdom.
  return [{ path: "same.zip", file: new NodeFile([data], "same.zip") as unknown as File }];
}
function Location() { return <output aria-label="Location">{useLocation().pathname}</output>; }
beforeEach(async () => {
  notifyManager.setScheduler(queueMicrotask);
  vi.stubGlobal("indexedDB", undefined);
  await library.forget();
});
afterEach(() => {
  cleanup(); clients.splice(0).forEach(client => client.clear()); vi.restoreAllMocks(); vi.unstubAllGlobals();
  notifyManager.setScheduler(defaultScheduler);
});
async function mount() {
  vi.spyOn(library, "restore").mockResolvedValue();
  vi.spyOn(api, "plugins").mockResolvedValue([]); vi.spyOn(api, "titles").mockResolvedValue([]);
  vi.spyOn(api, "workspaces").mockResolvedValue([]);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } }); clients.push(client);
  await act(async () => { render(<MemoryRouter><QueryClientProvider client={client}><Location /><HomePage /></QueryClientProvider></MemoryRouter>); });
}
const open = () => fireEvent.click(screen.getByRole("button", { name: /PPSA99999.*save folder/ }));

it.each(["folder", "zip"])("uploads changed %s bytes at equal title/path, preserving the older dirty workspace", async kind => {
  const sources = kind === "folder" ? folder : zip;
  const a = sources("AAA"), b = sources("BBB");
  const older = workspace("a");
  older.documents = [{ id: "dirty", fileId: "f", filePath: path, title: "Older edits", pluginId: "test", isDirty: true }];
  const upload = vi.spyOn(api, "upload").mockResolvedValueOnce(older).mockResolvedValueOnce(workspace("b"));
  const lookup = vi.spyOn(api, "workspace").mockResolvedValue(older);
  const remove = vi.spyOn(api, "deleteWorkspace");
  await library.scanFiles("Same folder", a); await mount();
  await act(async () => { open(); });
  await act(async () => { open(); });
  expect(upload).toHaveBeenCalledOnce(); // Reuse within the exact snapshot.
  expect(lookup).toHaveBeenCalledExactlyOnceWith("a");
  await act(async () => { await library.scanFiles("Same folder", b); });
  await act(async () => { open(); });
  expect(upload).toHaveBeenCalledTimes(2);
  expect(upload.mock.calls.map(call => call[0])).toEqual([librarySources(a, kind), librarySources(b, kind)]);
  expect(upload.mock.calls[0]![0][0]!.file).toBe(a[0]!.file);
  expect(upload.mock.calls[1]![0][0]!.file).toBe(b[0]!.file);
  expect(screen.getByLabelText("Location").textContent).toBe("/w/b");
  expect(remove).not.toHaveBeenCalled();
  expect(older.documents[0]?.isDirty).toBe(true);
});

it("retires a direct ZIP upload and its progress before React adopts a replacement library", async () => {
  const pending = deferred<Workspace>();
  vi.spyOn(api, "upload").mockReturnValue(pending.promise);
  await mount();
  await act(async () => {
    fireEvent.change(document.querySelector('input[accept=".zip"]')!, { target: { files: [new File(["AAA"], "save.zip")] } });
  });
  await act(async () => {
    await library.scanFiles("B", folder("BBB"));
    pending.resolve(workspace("old")); await pending.promise;
  });
  expect(screen.getByLabelText("Location").textContent).toBe("/");
  expect(screen.queryByText(/Importing save.zip/)).toBeNull();
});
function librarySources(files: ReturnType<typeof folder>, kind: string) {
  return files.map(source => ({ ...source, path: kind === "zip" ? source.file.name : path }));
}

it.each(["scan", "forget"])("retires a remembered lookup immediately on %s, before React rerenders", async action => {
  await library.scanFiles("A", folder("AAA"));
  const key = library.get().games[0]!.key;
  library.rememberImport(key, "old");
  const lookup = deferred<Workspace>(); vi.spyOn(api, "workspace").mockReturnValue(lookup.promise);
  const upload = vi.spyOn(api, "upload").mockResolvedValue(workspace("new"));
  await mount();
  await act(async () => {
    open();
    if (action === "forget") await library.forget();
    else await library.scanFiles("B", folder("BBB"));
    lookup.resolve(workspace("old")); await lookup.promise;
  });
  expect(screen.getByLabelText("Location").textContent).toBe("/");
  expect(upload).not.toHaveBeenCalled();
  await act(async () => { await library.scanFiles("B", folder("BBB")); });
  await act(async () => { open(); });
  expect(screen.getByLabelText("Location").textContent).toBe("/w/new");
});

it.each(["resolve", "reject"] as const)("retires an upload %s and releases the new snapshot's import controls", async outcome => {
  await library.scanFiles("A", folder("AAA"));
  const oldSnapshot = library.get().snapshotId;
  const pending = deferred<Workspace>();
  const upload = vi.spyOn(api, "upload").mockReturnValueOnce(pending.promise).mockResolvedValueOnce(workspace("new"));
  await mount();
  await act(async () => { open(); });
  await act(async () => { await library.scanFiles("B", folder("BBB")); });
  expect(screen.getByRole("button", { name: /PPSA99999.*save folder/ })).toHaveProperty("disabled", false);
  await act(async () => { open(); });
  await act(async () => {
    if (outcome === "resolve") pending.resolve(workspace("old")); else pending.reject(new Error("Old failure"));
    await pending.promise.catch(() => {});
  });
  expect(upload).toHaveBeenCalledTimes(2);
  expect(screen.getByLabelText("Location").textContent).toBe("/w/new");
  expect(screen.queryByRole("alert")).toBeNull();
  expect(library.importedWorkspace(library.get().games[0]!.key)).toBe("new");
  library.rememberImport(library.get().games[0]!.key, "late-old", oldSnapshot);
  expect(library.importedWorkspace(library.get().games[0]!.key)).toBe("new");
});
