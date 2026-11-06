import type { Page } from "@playwright/test";

/**
 * The .NET runtime's file system inside the page's runtime worker: the in-browser equivalent of the old
 * server's data directory. Presets and reference packs live under `persistentRoot` (an IndexedDB-backed
 * Emscripten mount, see web/src/runtime/dotnetBoot.ts), so tests can model external changes and storage
 * failures there instead of on the host's disk.
 */
export const persistentRoot = "/savetoolkit/persist";
export const presetLibraryPath = `${persistentRoot}/presets/presets.json`;

type Operation = { kind: "read"; path: string } | { kind: "write"; path: string; text: string }
  | { kind: "rename"; path: string; to: string } | { kind: "unlink"; path: string };

export async function runtimeFiles(page: Page) {
  const workers = page.workers();
  const hosts = await Promise.all(workers.map(worker => worker.evaluate(() => "getDotnetRuntime" in globalThis).catch(() => false)));
  const worker = workers[hosts.indexOf(true)];
  if (!worker) throw new Error("The page has not booted its .NET runtime worker.");
  const run = (operation: Operation) => worker.evaluate(operation => {
    type Fs = { readFile(path: string, options: { encoding: "utf8" }): string; writeFile(path: string, data: string): void;
      rename(from: string, to: string): void; unlink(path: string): void };
    const fs = (globalThis as unknown as { getDotnetRuntime(id: number): { Module: { FS: Fs } } }).getDotnetRuntime(0).Module.FS;
    if (operation.kind === "read") return fs.readFile(operation.path, { encoding: "utf8" });
    if (operation.kind === "write") fs.writeFile(operation.path, operation.text);
    else if (operation.kind === "rename") fs.rename(operation.path, operation.to);
    else fs.unlink(operation.path);
    return "";
  }, operation);
  return {
    read: (path: string) => run({ kind: "read", path }),
    write: async (path: string, text: string) => { await run({ kind: "write", path, text }); },
    rename: async (path: string, to: string) => { await run({ kind: "rename", path, to }); },
    unlink: async (path: string) => { await run({ kind: "unlink", path }); },
  };
}
