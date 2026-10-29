import { defaultScheduler, notifyManager, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, useLocation } from "react-router";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { api, ApiError } from "../../api/client";
import type { Workspace, WorkspaceSummary } from "../../api/protocol";
import { library } from "../../state/libraryStore";
import { deferred } from "../../test/deferred";
import { HomePage } from "./HomePage";

const clients: QueryClient[] = [];
const path = "PPSA99999/sdimg_SLOT/save.dat";
const original = new File(["AAA"], "save.dat");
const zip = new File(["synthetic ZIP"], "older.zip");
const older: Workspace = { id: "older", name: "Older synthetic dump", createdAt: "2026-10-29", titles: [], documents: [] };
const failure = () => new DOMException("Synthetic metadata/read failure", "NotReadableError");
function Location() { return <output aria-label="Location">{useLocation().pathname}</output>; }
beforeEach(async () => {
  notifyManager.setScheduler(queueMicrotask); vi.stubGlobal("indexedDB", undefined);
  await library.forget(); await library.scanFiles("Original", [{ path, file: original }]);
});
afterEach(() => {
  cleanup(); clients.splice(0).forEach(client => client.clear()); vi.restoreAllMocks(); vi.unstubAllGlobals();
  notifyManager.setScheduler(defaultScheduler);
});
async function mount() {
  vi.spyOn(library, "restore").mockResolvedValue();
  vi.spyOn(api, "plugins").mockResolvedValue([]); vi.spyOn(api, "titles").mockResolvedValue([]);
  const summaries: WorkspaceSummary[] = [];
  vi.spyOn(api, "workspaces").mockImplementation(async () => [...summaries]);
  const pending = deferred<Workspace>(); const upload = vi.spyOn(api, "upload").mockReturnValue(pending.promise);
  const scan = vi.spyOn(library, "scanFiles"); const scanHandle = vi.spyOn(library, "scanHandle");
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } }); clients.push(client);
  await act(async () => { render(<MemoryRouter><QueryClientProvider client={client}><Location /><HomePage /></QueryClientProvider></MemoryRouter>); });
  return { pending, upload, summaries, scan, scanHandle };
}
function start(source: string) {
  if (source === "zip") fireEvent.change(document.querySelector('input[accept=".zip"]')!, { target: { files: [zip] } });
  else if (source === "tile") fireEvent.click(screen.getByRole("button", { name: /PPSA99999.*save folder/ }));
}
function drop(dataTransfer: object) { fireEvent.drop(screen.getByText("Drop a folder or zip here").parentElement!, { dataTransfer }); }
function fileEntry(file: File, read: (resolve: (file: File) => void, reject: (error: DOMException) => void) => void = resolve => resolve(file)) {
  return { isDirectory: false, isFile: true, name: file.name, file: read };
}
function folder(name: string, children: object[]) {
  return { isDirectory: true, isFile: false, name, createReader: vi.fn(() => {
    let read = false;
    return { readEntries: (resolve: (entries: object[]) => void) => { const batch = read ? [] : children; read = true; resolve(batch); } };
  }) };
}
function recover() {
  drop({ items: [{ webkitGetAsEntry: () => folder("Recovery", [folder("PPSA99999", [folder("sdimg_SLOT", [fileEntry(original)])])]) }] });
}
function brokenMetadata(kind: string, entry: object) {
  const first = { webkitGetAsEntry: () => entry };
  if (kind === "iterator") return { items: { *[Symbol.iterator]() { yield first; throw failure(); } } };
  const second = kind === "entry" ? { webkitGetAsEntry: () => { throw failure(); } }
    : { webkitGetAsEntry: () => ({ get isDirectory() { throw failure(); } }) };
  return { items: [first, second] };
}
async function settle(state: Awaited<ReturnType<typeof mount>>, outcome: string) {
  await act(async () => {
    if (outcome === "success") {
      state.summaries.push({ id: older.id, name: older.name, createdAt: older.createdAt, titleIds: [], directoryCount: 0, fileCount: 0 });
      state.pending.resolve(older);
    } else state.pending.reject(new ApiError("Older upload refused.", 503));
    await state.pending.promise.catch(() => {});
  });
}
function expectRecovery(snapshotId: number) {
  expect(screen.getByRole("alert").textContent).toMatch(/Could not read.*Choose folder.*drop.*again/);
  expect(screen.queryByRole("button", { name: "Retry import" })).toBeNull();
  expect(screen.getByLabelText("Location").textContent).toBe("/");
  expect(library.get().snapshotId).toBe(snapshotId);
  expect(library.get().games[0]!.sources[0]!.file).toBe(original);
}

for (const metadata of ["entry", "iterator", "flag"]) {
  it.each([
    { source: "zip", outcome: "success" }, { source: "zip", outcome: "failure" },
    { source: "tile", outcome: "success" }, { source: "tile", outcome: "failure" },
  ])(`retains a known directory after a later ${metadata} failure during $source/$outcome`, async ({ source, outcome }) => {
    const state = await mount(); const snapshotId = library.get().snapshotId;
    const directory = folder("Unreadable", [fileEntry(original)]);
    await act(async () => { start(source); });
    await act(async () => { drop(brokenMetadata(metadata, directory)); });
    const recovery = screen.getByRole("alert").textContent;
    expectRecovery(snapshotId);
    expect(directory.createReader).not.toHaveBeenCalled();
    expect(state.scan).not.toHaveBeenCalled(); expect(state.scanHandle).not.toHaveBeenCalled();
    expect(state.upload).toHaveBeenCalledOnce();
    expect(screen.getByRole("button", { name: "Open zip" })).toHaveProperty("disabled", true);
    await act(async () => {
      state.upload.mock.calls[0]![2]?.(0.9);
      drop({ items: [{ webkitGetAsEntry: () => fileEntry(zip) }] });
    });
    expect(screen.getByRole("alert").textContent).toBe(recovery);
    if (source === "tile") expect(screen.getByRole("progressbar").getAttribute("aria-valuenow")).toBe("0");
    expect(state.upload).toHaveBeenCalledOnce();
    await settle(state, outcome);
    expectRecovery(snapshotId);
    expect(screen.getByRole("alert").textContent).toBe(recovery);
    expect(screen.queryByRole("progressbar")).toBeNull();
    expect(screen.getByRole("button", { name: "Open zip" })).toHaveProperty("disabled", false);
    if (outcome === "success") {
      expect(await screen.findByRole("link", { name: /Older synthetic dump/ })).toHaveProperty("pathname", "/w/older");
      expect(api.workspaces).toHaveBeenCalledTimes(2);
      if (source === "tile") expect(library.importedWorkspace(library.get().games[0]!.key)).toBe(older.id);
    }
    await act(async () => { recover(); });
    expect(screen.queryByRole("alert")).toBeNull();
    expect(library.get().folderName).toBe("Recovery"); expect(library.get().status).toBe("ready");
    expect(library.get().games[0]!.sources[0]!.file).toBe(original);
    expect(state.upload).toHaveBeenCalledOnce();
  });
}

it.each([
  { kind: "entry", source: "free", outcome: "none" }, { kind: "files", source: "free", outcome: "none" },
  { kind: "items", source: "zip", outcome: "success" }, { kind: "entry", source: "tile", outcome: "failure" },
  { kind: "after-file", source: "zip", outcome: "failure" }, { kind: "after-file", source: "tile", outcome: "success" },
])("owns unclassified $kind recovery independently of $source admission ($outcome)", async ({ kind, source, outcome }) => {
  const state = await mount(); const snapshotId = library.get().snapshotId;
  const read = vi.fn((resolve: (file: File) => void) => resolve(zip));
  const transfer = kind === "items" ? { get items() { throw failure(); } }
    : kind === "files" ? { items: [], get files() { throw failure(); } }
    : kind === "after-file" ? brokenMetadata("entry", fileEntry(zip, read))
    : { items: [{ webkitGetAsEntry: () => { throw failure(); } }] };
  await act(async () => { start(source); drop(transfer); });
  expectRecovery(snapshotId);
  expect(read).not.toHaveBeenCalled();
  expect(state.scan).not.toHaveBeenCalled(); expect(state.scanHandle).not.toHaveBeenCalled();
  expect(state.upload).toHaveBeenCalledTimes(source === "free" ? 0 : 1);
  expect(screen.getByRole("button", { name: "Open zip" })).toHaveProperty("disabled", source !== "free");
  if (source !== "free") {
    const recovery = screen.getByRole("alert").textContent;
    await act(async () => { state.upload.mock.calls[0]![2]?.(0.9); });
    await settle(state, outcome);
    expectRecovery(snapshotId); expect(screen.getByRole("alert").textContent).toBe(recovery);
    expect(screen.queryByRole("progressbar")).toBeNull();
    if (outcome === "success") expect(await screen.findByRole("link", { name: /Older synthetic dump/ })).toBeTruthy();
  }
  expect(screen.getByRole("button", { name: "Open zip" })).toHaveProperty("disabled", false);
  await act(async () => { recover(); });
  expect(screen.queryByRole("alert")).toBeNull(); expect(library.get().folderName).toBe("Recovery");
  expect(state.upload).toHaveBeenCalledTimes(source === "free" ? 0 : 1);
});

it.each(["known-folder", "unclassified"])("retires an earlier pending native enumeration after a newer %s metadata failure", async kind => {
  const state = await mount(); const snapshotId = library.get().snapshotId;
  const enumeration = deferred<File>(); const getFile = vi.fn(() => enumeration.promise);
  const handle = { kind: "directory", name: "Earlier", values: async function* () {
    yield { kind: "file", name: original.name, getFile };
  } } as unknown as FileSystemDirectoryHandle;
  await act(async () => { drop({ items: [{ webkitGetAsEntry: () => folder("Earlier", []), getAsFileSystemHandle: async () => handle }] }); });
  expect(screen.getByRole("status", { name: "Dropped files status" }).textContent).toBe("Reading dropped folder…");
  expect(getFile).toHaveBeenCalledOnce();
  await act(async () => {
    drop(kind === "known-folder" ? brokenMetadata("entry", folder("Newer", []))
      : { items: [{ webkitGetAsEntry: () => { throw failure(); } }] });
  });
  const recovery = screen.getByRole("alert").textContent;
  await act(async () => {
    enumeration.resolve(original);
    await enumeration.promise;
  });
  expectRecovery(snapshotId); expect(screen.getByRole("alert").textContent).toBe(recovery);
  expect(state.scan).not.toHaveBeenCalled(); expect(state.scanHandle).not.toHaveBeenCalled();
  expect(state.upload).not.toHaveBeenCalled();
  await act(async () => { recover(); });
  expect(screen.queryByRole("alert")).toBeNull(); expect(library.get().folderName).toBe("Recovery");
});

it.each(["synchronous", "asynchronous"])("releases admitted file-read failure (%s) without uploading partial files, then accepts a retry", async timing => {
  const state = await mount(); const snapshotId = library.get().snapshotId;
  const readFailure = deferred<File>();
  const read = vi.fn((resolve: (file: File) => void, reject: (error: DOMException) => void) => {
    if (timing === "synchronous") throw failure();
    void readFailure.promise.then(resolve, () => reject(failure()));
  });
  await act(async () => { drop({ items: [fileEntry(zip), fileEntry(zip, read)].map(entry => ({ webkitGetAsEntry: () => entry })) }); });
  if (timing === "asynchronous") {
    expect(screen.getByRole("status", { name: "Dropped files status" }).textContent).toBe("Reading dropped files…");
    const duplicate = vi.fn((resolve: (file: File) => void) => resolve(zip));
    await act(async () => { drop({ items: [{ webkitGetAsEntry: () => fileEntry(zip, duplicate) }] }); });
    expect(duplicate).not.toHaveBeenCalled();
    await act(async () => { readFailure.reject(failure()); await readFailure.promise.catch(() => {}); });
  }
  expect(read).toHaveBeenCalledOnce(); expectRecovery(snapshotId);
  expect(state.upload).not.toHaveBeenCalled(); expect(state.scan).not.toHaveBeenCalled();
  expect(screen.getByRole("button", { name: "Open zip" })).toHaveProperty("disabled", false);
  await act(async () => { drop({ items: [{ webkitGetAsEntry: () => fileEntry(zip) }] }); });
  expect(state.upload).toHaveBeenCalledExactlyOnceWith([{ path: zip.name, file: zip }], undefined, expect.any(Function));
  expect(screen.queryByRole("alert")).toBeNull();
  await settle(state, "success");
  expect(screen.getByLabelText("Location").textContent).toBe("/w/older");
});

it.each(["free", "zip", "tile"])("adopts a complete mixed folder/file drop independently of %s admission", async source => {
  const state = await mount();
  const directory = folder("Recovery", [folder("PPSA99999", [folder("sdimg_SLOT", [fileEntry(original)])])]);
  await act(async () => {
    start(source);
    drop({ items: [fileEntry(zip), directory].map(entry => ({ webkitGetAsEntry: () => entry })) });
  });
  expect(state.scan).toHaveBeenCalledExactlyOnceWith("Dropped folders", [
    { path: zip.name, file: zip }, { path: `Recovery/${path}`, file: original },
  ]);
  expect(library.get().folderName).toBe("Dropped folders"); expect(library.get().status).toBe("ready");
  expect(state.upload).toHaveBeenCalledTimes(source === "free" ? 0 : 1);
  if (source !== "free") {
    await settle(state, "success");
    expect(await screen.findByRole("link", { name: /Older synthetic dump/ })).toBeTruthy();
  }
  expect(screen.getByLabelText("Location").textContent).toBe("/");
  expect(screen.queryByRole("alert")).toBeNull();
});
