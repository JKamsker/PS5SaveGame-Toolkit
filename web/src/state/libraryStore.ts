import { useSyncExternalStore } from "react";
import { groupIntoGames, listDirectory, type LibraryGame, type ScannedFile } from "../lib/library";

export type LibraryStatus = "empty" | "scanning" | "ready" | "reconnect" | "error";

export interface LibraryState {
  status: LibraryStatus;
  folderName: string | null;
  games: LibraryGame[];
  icons: Record<string, string>;
  skipped: number;
  error: string | null;
  /** True when the folder survives a reload (File System Access handle stored for this tab). */
  persistent: boolean;
}

const TAB_KEY = "savebench.tab";
const IMPORTED_KEY = "savebench.imported";
const DB_NAME = "savebench";
const STORE = "folders";

const initial: LibraryState = { status: "empty", folderName: null, games: [], icons: {}, skipped: 0, error: null, persistent: false };

let state: LibraryState = initial;
let restoreStarted = false;
const listeners = new Set<() => void>();

function set(next: Partial<LibraryState>): void {
  state = { ...state, ...next };
  listeners.forEach((l) => l());
}

function safeSession<T>(fn: () => T, fallback: T): T {
  try {
    return fn();
  } catch {
    return fallback;
  }
}

/** Per-tab id: sessionStorage is scoped to the tab, so each tab remembers its own folder. */
function tabId(): string {
  return safeSession(() => {
    let id = sessionStorage.getItem(TAB_KEY);
    if (!id) {
      id = crypto.randomUUID();
      sessionStorage.setItem(TAB_KEY, id);
    }
    return id;
  }, "no-session");
}

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, 1);
    request.onupgradeneeded = () => request.result.createObjectStore(STORE);
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

async function idb<T>(mode: IDBTransactionMode, run: (store: IDBObjectStore) => IDBRequest<T>): Promise<T | undefined> {
  try {
    const db = await openDb();
    return await new Promise<T>((resolve, reject) => {
      const request = run(db.transaction(STORE, mode).objectStore(STORE));
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  } catch {
    return undefined;
  }
}

async function applyScan(folderName: string, files: ScannedFile[], persistent: boolean): Promise<void> {
  set({ status: "scanning", folderName, error: null, persistent });
  try {
    const { games, skipped } = await groupIntoGames(files);
    Object.values(state.icons).forEach((url) => URL.revokeObjectURL(url));
    const icons = Object.fromEntries(games.filter((g) => g.icon).map((g) => [g.key, URL.createObjectURL(g.icon!)]));
    set({ status: "ready", games, icons, skipped });
  } catch (error) {
    set({ status: "error", error: error instanceof Error ? error.message : "The folder could not be read." });
  }
}

async function scanHandle(handle: FileSystemDirectoryHandle): Promise<void> {
  await idb("readwrite", (s) => s.put(handle, tabId()));
  set({ status: "scanning", folderName: handle.name, persistent: true });
  try {
    await applyScan(handle.name, await listDirectory(handle), true);
  } catch (error) {
    set({ status: "error", error: error instanceof Error ? error.message : "The folder could not be read." });
  }
}

export const library = {
  get: (): LibraryState => state,

  subscribe(listener: () => void): () => void {
    listeners.add(listener);
    return () => listeners.delete(listener);
  },

  supportsHandles: (): boolean => typeof window !== "undefined" && typeof window.showDirectoryPicker === "function",

  /** Opens the folder picker (File System Access API). Returns false if unsupported or cancelled. */
  async pickFolder(): Promise<boolean> {
    if (!window.showDirectoryPicker) return false;
    try {
      const handle = await window.showDirectoryPicker({ id: "savebench-dumps", mode: "read" });
      await scanHandle(handle);
      return true;
    } catch (error) {
      if (error instanceof DOMException && error.name === "AbortError") return true;
      return false;
    }
  },

  scanHandle,

  /** Fallback for browsers without handles: the files live only as long as this page. */
  scanFiles: (folderName: string, files: ScannedFile[]) => applyScan(folderName, files, false),

  /** Re-attaches this tab's folder after a reload. Permission prompts need a user gesture, see reconnect(). */
  async restore(): Promise<void> {
    if (restoreStarted || state.status !== "empty") return;
    restoreStarted = true;
    const handle = await idb<FileSystemDirectoryHandle>("readonly", (s) => s.get(tabId()) as IDBRequest<FileSystemDirectoryHandle>);
    if (!handle) return;
    const permission = (await handle.queryPermission?.({ mode: "read" })) ?? "prompt";
    if (permission === "granted") await scanHandle(handle);
    else set({ status: "reconnect", folderName: handle.name, persistent: true });
  },

  async reconnect(): Promise<void> {
    const handle = await idb<FileSystemDirectoryHandle>("readonly", (s) => s.get(tabId()) as IDBRequest<FileSystemDirectoryHandle>);
    if (!handle) return void set(initial);
    const permission = (await handle.requestPermission?.({ mode: "read" })) ?? "denied";
    if (permission === "granted") await scanHandle(handle);
  },

  async forget(): Promise<void> {
    await idb("readwrite", (s) => s.delete(tabId()));
    Object.values(state.icons).forEach((url) => URL.revokeObjectURL(url));
    set(initial);
  },

  importedWorkspace: (gameKey: string): string | undefined =>
    safeSession(() => (JSON.parse(sessionStorage.getItem(IMPORTED_KEY) ?? "{}") as Record<string, string>)[gameKey], undefined),

  rememberImport(gameKey: string, workspaceId: string): void {
    safeSession(() => {
      const map = JSON.parse(sessionStorage.getItem(IMPORTED_KEY) ?? "{}") as Record<string, string>;
      sessionStorage.setItem(IMPORTED_KEY, JSON.stringify({ ...map, [gameKey]: workspaceId }));
    }, undefined);
  },
};

export function useLibrary(): LibraryState {
  return useSyncExternalStore(library.subscribe, library.get);
}
