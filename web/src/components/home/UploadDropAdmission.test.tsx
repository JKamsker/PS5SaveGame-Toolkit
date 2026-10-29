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
const olderZip = new File(["synthetic A"], "older.zip");
const duplicateZip = new File(["synthetic B"], "duplicate.zip");
const older: Workspace = { id: "older", name: "Older synthetic dump", createdAt: "2026-10-29", titles: [], documents: [] };
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
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } }); clients.push(client);
  await act(async () => { render(<MemoryRouter><QueryClientProvider client={client}><Location /><HomePage /></QueryClientProvider></MemoryRouter>); });
  return { pending, upload, summaries };
}
const chooseZip = () => fireEvent.change(document.querySelector('input[accept=".zip"]')!, { target: { files: [olderZip] } });
const openTile = () => fireEvent.click(screen.getByRole("button", { name: /PPSA99999.*save folder/ }));
function drop(dataTransfer: object) { fireEvent.drop(screen.getByText("Drop a folder or zip here").parentElement!, { dataTransfer }); }
function fileEntry(file: File, read = vi.fn((resolve: (file: File) => void) => resolve(file))) {
  return { isDirectory: false, isFile: true, name: file.name, file: read };
}
function dropFile(transport: string) {
  const read = vi.fn((resolve: (file: File) => void) => resolve(duplicateZip));
  if (transport === "entry") drop({ items: [{ webkitGetAsEntry: () => fileEntry(duplicateZip, read) }] });
  else drop({ items: [{ kind: "file", getAsFile: () => duplicateZip }], files: [duplicateZip] });
  return read;
}
function folder(name: string, children: object[], rejectLater = false) {
  return { isDirectory: true, isFile: false, name, createReader: () => {
    let read = false;
    return { readEntries: (resolve: (entries: object[]) => void, reject: (error: DOMException) => void) => {
      if (!read) { read = true; resolve(children); }
      else if (rejectLater) reject(new DOMException("Synthetic partial-folder failure", "NotReadableError"));
      else resolve([]);
    } };
  } };
}
const dropFailedFolder = () => drop({ items: [{ webkitGetAsEntry: () => folder("Unreadable", [fileEntry(original)], true) }] });
const dropReadableFolder = () => drop({ items: [{ webkitGetAsEntry: () => folder("Recovery", [folder("PPSA99999", [folder("sdimg_SLOT", [fileEntry(original)])])]) }] });

for (const transport of ["entry", "file-list"]) {
  it.each([
    { source: "zip", outcome: "success", failedFolder: false }, { source: "zip", outcome: "failure", failedFolder: false },
    { source: "tile", outcome: "success", failedFolder: false }, { source: "tile", outcome: "failure", failedFolder: false },
    { source: "zip", outcome: "success", failedFolder: true }, { source: "zip", outcome: "failure", failedFolder: true },
    { source: "tile", outcome: "success", failedFolder: true }, { source: "tile", outcome: "failure", failedFolder: true },
  ])(`rejects a duplicate ${transport} ZIP drop during $source/$outcome (folder recovery=$failedFolder) without retiring its owner`, async ({ source, outcome, failedFolder }) => {
    const { pending, upload, summaries } = await mount();
    const snapshotId = library.get().snapshotId;
    let duplicateRead!: ReturnType<typeof dropFile>;
    await act(async () => {
      if (source === "zip") chooseZip(); else openTile();
      // Same event batch: admission cannot rely on a rendered disabled/busy flag.
      if (!failedFolder) duplicateRead = dropFile(transport);
    });
    let recovery: string | null = null;
    if (failedFolder) {
      await act(async () => { dropFailedFolder(); });
      recovery = screen.getByRole("alert").textContent;
      expect(recovery).toMatch(/Could not read.*Choose folder/);
      await act(async () => { duplicateRead = dropFile(transport); });
      expect(screen.getByRole("alert").textContent).toBe(recovery);
    }
    expect(duplicateRead).not.toHaveBeenCalled();
    expect(upload).toHaveBeenCalledOnce();
    expect(library.get().snapshotId).toBe(snapshotId);
    expect(library.get().games[0]!.sources[0]!.file).toBe(original);
    await act(async () => { upload.mock.calls[0]![2]?.(0.75); });
    if (failedFolder) expect(screen.getByRole("alert").textContent).toBe(recovery);
    else if (source === "zip") expect(screen.getByRole("status", { name: "Dropped files status" }).textContent).toContain("older.zip: 75 %");
    else expect(screen.getByRole("progressbar").getAttribute("aria-valuenow")).toBe("75");
    await act(async () => {
      if (outcome === "success") {
        summaries.push({ id: older.id, name: older.name, createdAt: older.createdAt, titleIds: [], directoryCount: 0, fileCount: 0 });
        pending.resolve(older);
      } else pending.reject(new ApiError("Original import refused.", 503));
      await pending.promise.catch(() => {});
    });
    expect(upload).toHaveBeenCalledOnce();
    expect(screen.getByLabelText("Location").textContent).toBe(outcome === "success" && !failedFolder ? "/w/older" : "/");
    if (failedFolder) {
      expect(screen.getByRole("alert").textContent).toBe(recovery);
      expect(screen.queryByRole("button", { name: "Retry import" })).toBeNull();
    } else if (outcome === "failure") {
      expect(screen.getByRole("alert").textContent).toBe("Original import refused.");
      if (source === "zip") expect(screen.getByRole("button", { name: "Retry import" })).toBeTruthy();
      else expect(screen.getByRole("button", { name: /PPSA99999.*save folder/ })).toHaveProperty("disabled", false);
    }
    if (outcome === "success") {
      expect(await screen.findByRole("link", { name: /Older synthetic dump/ })).toHaveProperty("pathname", "/w/older");
      if (source === "tile") expect(library.importedWorkspace(library.get().games[0]!.key)).toBe(older.id);
    }
    await act(async () => { dropReadableFolder(); });
    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.queryByRole("button", { name: "Retry import" })).toBeNull();
    expect(library.get().folderName).toBe("Recovery");
    expect(library.get().status).toBe("ready");
    expect(library.get().games[0]!.sources[0]!.file).toBe(original);
    expect(upload).toHaveBeenCalledOnce();
  });
}

it.each([false, true])("reserves a delayed file-drop read synchronously and respects newer folder intent (%s)", async newerFolder => {
  const { pending, upload } = await mount();
  const fileRead = deferred<File>();
  const read = vi.fn((resolve: (file: File) => void) => { void fileRead.promise.then(resolve); });
  let duplicateRead!: ReturnType<typeof dropFile>;
  await act(async () => {
    drop({ items: [{ webkitGetAsEntry: () => fileEntry(olderZip, read) }] });
    duplicateRead = dropFile("entry");
    openTile(); chooseZip();
  });
  expect(read).toHaveBeenCalledOnce(); expect(duplicateRead).not.toHaveBeenCalled();
  expect(upload).not.toHaveBeenCalled();
  expect(screen.getByRole("status", { name: "Dropped files status" }).textContent).toBe("Reading dropped files…");
  if (newerFolder) await act(async () => { dropFailedFolder(); });
  await act(async () => { fileRead.resolve(olderZip); await fileRead.promise; });
  if (newerFolder) {
    expect(upload).not.toHaveBeenCalled();
    expect(screen.getByRole("alert").textContent).toMatch(/Could not read/);
    await act(async () => { dropReadableFolder(); });
    expect(screen.queryByRole("alert")).toBeNull();
    expect(library.get().folderName).toBe("Recovery");
  } else {
    expect(upload).toHaveBeenCalledOnce();
    expect(upload.mock.calls[0]![0]).toEqual([{ file: olderZip, path: olderZip.name }]);
    await act(async () => { pending.resolve(older); await pending.promise; });
    expect(screen.getByLabelText("Location").textContent).toBe("/w/older");
  }
  expect(screen.getByRole("button", { name: "Choose folder" })).toHaveProperty("disabled", false);
});

it("imports a FileList-only drop when the entry API is unavailable", async () => {
  const { pending, upload } = await mount();
  await act(async () => { drop({ files: [duplicateZip] }); });
  expect(upload).toHaveBeenCalledExactlyOnceWith([{ file: duplicateZip, path: duplicateZip.name }], undefined, expect.any(Function));
  await act(async () => { pending.resolve(older); await pending.promise; });
  expect(screen.getByLabelText("Location").textContent).toBe("/w/older");
});
