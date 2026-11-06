import type { Page } from "@playwright/test";

/**
 * The .NET runtime's file system inside the page's runtime worker: the in-browser equivalent of the old
 * server's data directory. Presets and reference packs live under `persistentRoot`, an Emscripten IDBFS
 * mount that every tab of the site shares through IndexedDB (see web/src/runtime/persistence.ts). Changes
 * made here are written through to IndexedDB under the same cross-tab lock, like an external writer such as
 * another tab: the runtime reloads that storage before each durable write, so memory-only edits would be
 * discarded. The change is deliberately not announced, so the .NET stores must detect it themselves.
 */
export const persistentRoot = "/savetoolkit/persist";
export const presetLibraryPath = `${persistentRoot}/presets/presets.json`;
/** Must match LOCK_NAME in web/src/runtime/persistence.ts. */
const storageLock = "savetoolkit-persistent-storage";

type Change = { kind: "write"; path: string; text: string } | { kind: "rename"; path: string; to: string } | { kind: "unlink"; path: string };

export async function runtimeFiles(page: Page) {
  const workers = page.workers();
  const hosts = await Promise.all(workers.map(worker => worker.evaluate(() => "getDotnetRuntime" in globalThis).catch(() => false)));
  const worker = workers[hosts.indexOf(true)];
  if (!worker) throw new Error("The page has not booted its .NET runtime worker.");
  type Fs = { readFile(path: string, options: { encoding: "utf8" }): string; writeFile(path: string, data: string): void;
    rename(from: string, to: string): void; unlink(path: string): void; syncfs(populate: boolean, done: (error: unknown) => void): void };
  return {
    read: (path: string) => worker.evaluate(file =>
      (globalThis as unknown as { getDotnetRuntime(id: number): { Module: { FS: Fs } } }).getDotnetRuntime(0).Module.FS
        .readFile(file, { encoding: "utf8" }), path),
    /** Applies the changes in one step and writes them through to IndexedDB, holding the storage lock. */
    change: async (...changes: Change[]) => {
      await worker.evaluate(async ([lock, changes]) => {
        const fs = (globalThis as unknown as { getDotnetRuntime(id: number): { Module: { FS: Fs } } }).getDotnetRuntime(0).Module.FS;
        const apply = async () => {
          for (const change of changes) {
            if (change.kind === "write") fs.writeFile(change.path, change.text);
            else if (change.kind === "rename") fs.rename(change.path, change.to);
            else fs.unlink(change.path);
          }
          await new Promise<void>((resolve, reject) => fs.syncfs(false, error => error ? reject(error as Error) : resolve()));
        };
        await (navigator.locks ? navigator.locks.request(lock, { mode: "exclusive" }, apply) : apply());
      }, [storageLock, changes] as const);
    },
  };
}
