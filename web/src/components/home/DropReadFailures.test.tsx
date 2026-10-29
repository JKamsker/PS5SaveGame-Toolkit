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
const original = new File(["AAA"], "save.dat");
const replacement = new File(["BBB"], "save.dat");
const path = "PPSA99999/sdimg_SLOT/save.dat";
const failure = () => new DOMException("Synthetic read failure", "NotReadableError");
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
  vi.spyOn(library, "restore").mockResolvedValue(); vi.spyOn(library, "supportsHandles").mockReturnValue(false);
  vi.spyOn(api, "plugins").mockResolvedValue([]); vi.spyOn(api, "titles").mockResolvedValue([]); vi.spyOn(api, "workspaces").mockResolvedValue([]);
  const upload = vi.spyOn(api, "upload"); const scan = vi.spyOn(library, "scanFiles");
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } }); clients.push(client);
  await act(async () => { render(<MemoryRouter><QueryClientProvider client={client}><Location /><HomePage /></QueryClientProvider></MemoryRouter>); });
  return { upload, scan };
}
function drop(entry: object, handle?: () => Promise<FileSystemHandle | null>) {
  fireEvent.drop(screen.getByText("Drop a folder or zip here").parentElement!, {
    dataTransfer: { items: [{ webkitGetAsEntry: () => entry, getAsFileSystemHandle: handle }] },
  });
}
function fileEntry(name = "save.dat", rejectRead = false) {
  return { isFile: true, isDirectory: false, name,
    file: (resolve: (file: File) => void, reject: (error: DOMException) => void) => rejectRead ? reject(failure()) : resolve(replacement) };
}
function directory(rejectBatch = false, rejectFile = false) {
  return { isDirectory: true, isFile: false, name: "Dropped",
    createReader: () => {
      let batch = 0;
      return { readEntries: (resolve: (entries: object[]) => void, reject: (error: DOMException) => void) => {
        if (batch++ === 0) resolve([fileEntry("first.dat"), fileEntry("unreadable.dat", rejectFile)]);
        else if (rejectBatch) reject(failure()); else resolve([]);
      } };
    } };
}
function readableDumpDirectory() {
  const dir = (name: string, children: object[]) => ({ isDirectory: true, isFile: false, name, createReader: () => {
    let read = false;
    return { readEntries: (resolve: (entries: object[]) => void) => { resolve(read ? [] : children); read = true; } };
  } });
  return dir("Recovery", [dir("PPSA99999", [dir("sdimg_SLOT", [fileEntry()])])]);
}

it.each(["handle", "handle-read", "file", "batch"])("reports a dropped-folder %s failure, preserves the library and offers folder selection", async kind => {
  const { upload, scan } = await mount();
  const snapshot = library.get().snapshotId;
  await act(async () => {
    const handle = kind === "handle" ? () => Promise.reject(failure()) : kind === "handle-read" ? async () => ({
      kind: "directory", name: "Unreadable", values: () => ({
        [Symbol.asyncIterator]() { return this; }, next: async () => { throw failure(); },
      }),
    } as unknown as FileSystemDirectoryHandle) : undefined;
    drop(directory(kind === "batch", kind === "file"), handle);
  });
  expect(screen.getByRole("alert").textContent).toMatch(/Could not read.*Choose folder.*drop.*again/);
  expect(screen.queryByRole("button", { name: "Retry import" })).toBeNull();
  expect(upload).not.toHaveBeenCalled(); expect(scan).not.toHaveBeenCalled();
  expect(library.get().snapshotId).toBe(snapshot);
  expect(library.get().games[0]!.sources[0]!.file).toBe(original);
  const choose = screen.getByRole("button", { name: "Choose folder" }); choose.focus();
  await act(async () => { fireEvent.click(choose); });
  expect(screen.queryByRole("alert")).toBeNull();
  Object.defineProperty(replacement, "webkitRelativePath", { configurable: true, value: `Recovery/${path}` });
  await act(async () => { fireEvent.change(document.querySelector('input[webkitdirectory]')!, { target: { files: [replacement] } }); });
  expect(library.get().folderName).toBe("Recovery");
  expect(library.get().games[0]!.sources[0]!.file).toBe(replacement);
  expect(document.activeElement).toBe(choose);
});

it("catches synchronous drop access errors and permits dropping a readable folder again", async () => {
  const { upload, scan } = await mount();
  await act(async () => { fireEvent.drop(screen.getByText("Drop a folder or zip here").parentElement!, {
    dataTransfer: { items: [{ webkitGetAsEntry: () => { throw failure(); } }] },
  }); });
  expect(screen.getByRole("alert").textContent).toMatch(/Could not read/);
  await act(async () => { drop(directory()); });
  expect(screen.queryByRole("alert")).toBeNull();
  expect(scan).toHaveBeenCalledOnce(); expect(upload).not.toHaveBeenCalled();
  expect(library.get().folderName).toBe("Dropped");
});

it("announces pending folder reads and ignores a late failure after choosing another folder", async () => {
  const pending = deferred<FileSystemHandle | null>();
  const { upload, scan } = await mount();
  await act(async () => { drop(directory(), () => pending.promise); });
  expect(screen.getByRole("status", { name: "Dropped files status" }).textContent).toBe("Reading dropped folder…");
  await act(async () => { fireEvent.change(document.querySelector('input[webkitdirectory]')!, { target: { files: [replacement] } }); });
  await act(async () => { pending.reject(failure()); await pending.promise.catch(() => {}); });
  expect(screen.queryByRole("alert")).toBeNull();
  expect(screen.queryByRole("status", { name: "Dropped files status" })).toBeNull();
  expect(scan).toHaveBeenCalledOnce(); expect(upload).not.toHaveBeenCalled();
});

it("clears an old upload retry when reading a new dropped folder fails", async () => {
  const { upload } = await mount(); upload.mockRejectedValue(new Error("Upload failure"));
  await act(async () => { fireEvent.change(document.querySelector('input[accept=".zip"]')!, { target: { files: [new File(["synthetic"], "save.zip")] } }); });
  expect(screen.getByRole("button", { name: "Retry import" })).toBeTruthy();
  await act(async () => { drop(directory(true)); });
  expect(screen.getByRole("alert").textContent).toMatch(/Could not read/);
  expect(screen.queryByRole("button", { name: "Retry import" })).toBeNull();
});

it.each(["rescan", "zip"])("ignores a late dropped-folder failure after a newer %s selection", async next => {
  const pending = deferred<FileSystemHandle | null>();
  const { upload } = await mount();
  upload.mockResolvedValue({ id: "new", name: "Synthetic dump", createdAt: "2026-10-29", documents: [], titles: [] });
  await act(async () => { drop(directory(), () => pending.promise); });
  await act(async () => {
    if (next === "rescan") await library.scanFiles("New scan", [{ path, file: replacement }]);
    else fireEvent.change(document.querySelector('input[accept=".zip"]')!, { target: { files: [new File(["synthetic"], "new.zip")] } });
  });
  await act(async () => { pending.reject(failure()); await pending.promise.catch(() => {}); });
  expect(screen.queryByRole("alert")).toBeNull();
  expect(screen.queryByText("Reading dropped folder…")).toBeNull();
  expect(upload).toHaveBeenCalledTimes(next === "zip" ? 1 : 0);
});

it.each([
  { source: "zip", outcome: "success" }, { source: "zip", outcome: "failure" },
  { source: "tile", outcome: "success" }, { source: "tile", outcome: "failure" },
])("preserves a newer failed drop after an older $source upload $outcome, then recovers", async ({ source, outcome }) => {
  const { upload } = await mount();
  const snapshotId = library.get().snapshotId;
  const pending = deferred<Workspace>(); upload.mockReturnValue(pending.promise);
  const older: Workspace = { id: "older", name: "Older synthetic dump", createdAt: "2026-10-29", titles: [], documents: [] };
  const summaries: WorkspaceSummary[] = [];
  vi.mocked(api.workspaces).mockImplementation(async () => summaries);
  await act(async () => {
    if (source === "zip") fireEvent.change(document.querySelector('input[accept=".zip"]')!, { target: { files: [new File(["synthetic"], "older.zip")] } });
    else fireEvent.click(screen.getByRole("button", { name: /PPSA99999.*save folder/ }));
  });
  expect(upload).toHaveBeenCalledOnce();
  await act(async () => { drop(directory(true)); });
  const recovery = screen.getByRole("alert").textContent;
  expect(recovery).toMatch(/Could not read.*Choose folder/);
  expect(library.get().snapshotId).toBe(snapshotId);
  expect(library.get().games[0]!.sources[0]!.file).toBe(original);
  // A newer source intent retires observers, but cannot release the active submit barrier.
  expect(screen.getByRole("button", { name: "Open zip" })).toHaveProperty("disabled", true);
  await act(async () => {
    upload.mock.calls[0]![2]?.(0.9);
    fireEvent.change(document.querySelector('input[accept=".zip"]')!, { target: { files: [new File(["synthetic"], "ignored.zip")] } });
  });
  expect(upload).toHaveBeenCalledOnce();
  expect(screen.getByRole("alert").textContent).toBe(recovery);
  await act(async () => {
    if (outcome === "success") {
      summaries.push({ id: older.id, name: older.name, createdAt: older.createdAt, titleIds: [], directoryCount: 0, fileCount: 0 });
      pending.resolve(older);
    } else pending.reject(new ApiError("Older ZIP upload failed.", 503));
    await pending.promise.catch(() => {});
  });
  expect(screen.getByLabelText("Location").textContent).toBe("/");
  expect(screen.getByRole("alert").textContent).toBe(recovery);
  expect(screen.queryByRole("button", { name: "Retry import" })).toBeNull();
  expect(screen.queryByRole("progressbar")).toBeNull();
  expect(screen.getByRole("button", { name: "Choose folder" })).toHaveProperty("disabled", false);
  if (outcome === "success") {
    expect(await screen.findByRole("link", { name: /Older synthetic dump/ })).toHaveProperty("pathname", "/w/older");
    expect(api.workspaces).toHaveBeenCalledTimes(2); // Initial list plus completed retired upload.
    if (source === "tile") expect(library.importedWorkspace(library.get().games[0]!.key)).toBe(older.id);
  }
  await act(async () => { drop(readableDumpDirectory()); });
  expect(screen.queryByRole("alert")).toBeNull();
  expect(library.get().folderName).toBe("Recovery");
  expect(library.get().games[0]!.sources[0]!.file).toBe(replacement);
  expect(upload).toHaveBeenCalledOnce();
});
