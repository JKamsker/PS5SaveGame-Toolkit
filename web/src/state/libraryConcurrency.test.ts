import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { LibraryGame } from "../lib/library";
import { deferred } from "../test/deferred";

vi.mock("../lib/library", async original => ({ ...await original<typeof import("../lib/library")>(), groupIntoGames: vi.fn(), listDirectory: vi.fn() }));
let library: typeof import("./libraryStore").library;
let group: typeof import("../lib/library").groupIntoGames;
let list: typeof import("../lib/library").listDirectory;
const game = (key: string): LibraryGame => ({ key, titleId: null, fallbackName: key, kind: "zip", sources: [], saveDirs: 1, totalBytes: 1, icon: null });
beforeEach(async () => {
  vi.resetModules(); vi.stubGlobal("indexedDB", undefined);
  ({ library } = await import("./libraryStore"));
  ({ groupIntoGames: group, listDirectory: list } = await import("../lib/library"));
  vi.mocked(group).mockReset(); vi.mocked(list).mockReset();
  Object.defineProperty(URL, "createObjectURL", { configurable: true, value: vi.fn(() => "blob:icon") });
  Object.defineProperty(URL, "revokeObjectURL", { configurable: true, value: vi.fn() });
});
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); Reflect.deleteProperty(URL, "createObjectURL"); Reflect.deleteProperty(URL, "revokeObjectURL"); });

it.each(["resolve", "reject"] as const)("ignores an older scan %s after a newer folder finishes", async outcome => {
  const old = deferred<Awaited<ReturnType<typeof group>>>();
  vi.mocked(group).mockReturnValueOnce(old.promise).mockResolvedValueOnce({ games: [game("new")], skipped: 0 });
  const first = library.scanFiles("Old folder", []);
  await library.scanFiles("New folder", []);
  if (outcome === "resolve") old.resolve({ games: [{ ...game("old"), icon: new Blob() }], skipped: 8 });
  else old.reject(new Error("Old scan failed"));
  await first;
  expect(library.get()).toMatchObject({ status: "ready", folderName: "New folder", games: [game("new")], skipped: 0, error: null });
  expect(URL.createObjectURL).not.toHaveBeenCalled();
});

it("does not repopulate a forgotten folder or allocate icons for the retired scan", async () => {
  const pending = deferred<Awaited<ReturnType<typeof group>>>();
  vi.mocked(group).mockReturnValue(pending.promise);
  const scan = library.scanFiles("Forgotten", []);
  await library.forget();
  pending.resolve({ games: [{ ...game("forgotten"), icon: new Blob() }], skipped: 0 });
  await scan;
  expect(library.get()).toMatchObject({ status: "empty", folderName: null, games: [], icons: {} });
  expect(URL.createObjectURL).not.toHaveBeenCalled();
});

it("does not let delayed directory enumeration replace a newer file selection", async () => {
  const pending = deferred<Awaited<ReturnType<typeof list>>>();
  const enumerating = deferred<void>();
  vi.mocked(list).mockImplementation(() => { enumerating.resolve(); return pending.promise; });
  vi.mocked(group).mockResolvedValue({ games: [game("new")], skipped: 0 });
  const old = library.scanHandle({ name: "Old handle" } as FileSystemDirectoryHandle);
  await enumerating.promise;
  await library.scanFiles("New files", []);
  pending.resolve([]); await old;
  expect(library.get()).toMatchObject({ folderName: "New files", games: [game("new")], persistent: false });
  expect(group).toHaveBeenCalledOnce();
});

it("does not publish restored permissions after the user chooses another folder", async () => {
  const permission = deferred<PermissionState>();
  const requested = deferred<void>();
  const handle = { name: "Remembered", queryPermission: () => { requested.resolve(); return permission.promise; } };
  const success = <T>(result: T) => {
    const request = { result, onsuccess: null as null | (() => void) };
    queueMicrotask(() => request.onsuccess?.()); return request;
  };
  vi.stubGlobal("indexedDB", { open: () => success({ transaction: () => ({ objectStore: () => ({ get: () => success(handle) }) }) }) });
  const restore = library.restore();
  await requested.promise;
  vi.mocked(group).mockResolvedValue({ games: [game("new")], skipped: 0 });
  await library.scanFiles("New files", []);
  permission.resolve("prompt"); await restore;
  expect(library.get()).toMatchObject({ status: "ready", folderName: "New files", persistent: false, games: [game("new")] });
});
