import type * as LibraryModule from "../lib/library";
import { act, cleanup, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { groupIntoGames, type LibraryGame, type ScannedFile } from "../lib/library";
import { library, useLibrary } from "./libraryStore";

vi.mock("../lib/library", async importOriginal => ({
  ...await importOriginal<typeof LibraryModule>(), groupIntoGames: vi.fn(),
}));

const game: LibraryGame = { key: "fixture", titleId: "PPSA99999", fallbackName: "Fixture", kind: "zip",
  sources: [], saveDirs: 1, totalBytes: 1, icon: null };
const files: ScannedFile[] = [{ file: new File(["invented fixture"], "save.zip"), path: "Folder/save.zip" }];

beforeEach(async () => {
  // No persistent browser database is needed for these fallback-store tests.
  vi.stubGlobal("indexedDB", undefined);
  await library.forget();
  sessionStorage.clear();
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.resetAllMocks(); vi.unstubAllGlobals(); });

it("publishes scanning before a pending empty scan completes and reports skipped files", async () => {
  let finish!: (value: Awaited<ReturnType<typeof groupIntoGames>>) => void;
  vi.mocked(groupIntoGames).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
  const { result } = renderHook(useLibrary);
  expect(result.current.status).toBe("empty");
  let scan!: Promise<void>;
  act(() => { scan = library.scanFiles("Folder", files); });
  expect(result.current).toMatchObject({ status: "scanning", folderName: "Folder", persistent: false, error: null });
  await act(async () => { finish({ games: [], skipped: 2 }); await scan; });
  expect(result.current).toMatchObject({ status: "ready", games: [], icons: {}, skipped: 2, error: null });
  expect(groupIntoGames).toHaveBeenCalledExactlyOnceWith(files);
});

it.each([
  { failure: new Error("Directory unavailable"), message: "Directory unavailable" },
  { failure: "unavailable", message: "The folder could not be read." },
])("reports a rejected scan ($message) and clears the error on retry", async ({ failure, message }) => {
  vi.mocked(groupIntoGames).mockRejectedValueOnce(failure).mockResolvedValueOnce({ games: [game], skipped: 0 });
  const { result } = renderHook(useLibrary);
  await act(async () => { await library.scanFiles("Folder", files); });
  expect(result.current).toMatchObject({ status: "error", folderName: "Folder", games: [], error: message });
  await act(async () => { await library.scanFiles("Retry", files); });
  expect(result.current).toMatchObject({ status: "ready", folderName: "Retry", games: [game], skipped: 0, error: null });
  expect(vi.mocked(groupIntoGames).mock.calls).toEqual([[files], [files]]);
});

it("unsubscribes the hook when it unmounts", () => {
  const unsubscribe = vi.fn();
  const subscribe = vi.spyOn(library, "subscribe").mockReturnValue(unsubscribe);
  const { unmount } = renderHook(useLibrary);
  expect(subscribe).toHaveBeenCalledTimes(1);
  unmount();
  expect(unsubscribe).toHaveBeenCalledExactlyOnceWith();
});

it("notifies subscribers for both scan phases and stops after unsubscribe", async () => {
  vi.mocked(groupIntoGames).mockResolvedValue({ games: [], skipped: 0 });
  const statuses: string[] = [];
  const unsubscribe = library.subscribe(() => statuses.push(library.get().status));
  try {
    await library.scanFiles("Folder", files);
    expect(statuses).toEqual(["scanning", "ready"]);
  } finally { unsubscribe(); }
  await library.scanFiles("Another folder", files);
  expect(statuses).toEqual(["scanning", "ready"]);
});

it("keeps imported workspace IDs separate and tolerates malformed stored mappings", () => {
  library.rememberImport("first", "w1");
  library.rememberImport("second", "w2");
  expect(library.importedWorkspace("first")).toBe("w1");
  expect(library.importedWorkspace("second")).toBe("w2");
  expect(library.importedWorkspace("missing")).toBeUndefined();
  sessionStorage.setItem("savebench.imported", "not JSON");
  expect(library.importedWorkspace("first")).toBeUndefined();
  expect(() => library.rememberImport("third", "w3")).not.toThrow();
});

it("tolerates unavailable storage and a missing folder database during restore", async () => {
  vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => { throw new DOMException("Storage unavailable"); });
  expect(library.importedWorkspace("first")).toBeUndefined();
  expect(() => library.rememberImport("first", "w1")).not.toThrow();
  await expect(library.restore()).resolves.toBeUndefined();
  expect(library.get()).toMatchObject({ status: "empty", folderName: null, games: [], error: null });
});
