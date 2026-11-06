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
/** Must match LOCK_NAME in web/src/runtime/browserTabs.ts. */
const storageLock = "savetoolkit-persistent-storage";

type Change = { kind: "write"; path: string; text: string } | { kind: "rename"; path: string; to: string } | { kind: "unlink"; path: string }
  | { kind: "mkdir"; path: string } | { kind: "rmdir"; path: string };

/** The page's booted .NET runtime worker. */
export async function runtimeWorker(page: Page) {
  const workers = page.workers();
  const hosts = await Promise.all(workers.map(worker => worker.evaluate(() => "getDotnetRuntime" in globalThis).catch(() => false)));
  const worker = workers[hosts.indexOf(true)];
  if (!worker) throw new Error("The page has not booted its .NET runtime worker.");
  return worker;
}

/**
 * Stops the page's booted .NET runtime the way a fatal runtime failure does: the runtime exits, its exit
 * hook makes the worker post its genuine `failed` message (web/src/runtime/worker.ts `stop`), and the
 * page's runtime client fails every pending request and reports the runtime as crashed.
 */
export async function stopRuntime(page: Page, reason: string) {
  const worker = await runtimeWorker(page);
  await worker.evaluate(cause => {
    const runtime = (globalThis as unknown as { getDotnetRuntime(id: number): { exit(code: number, reason?: unknown): void } }).getDotnetRuntime(0);
    // Emscripten signals the exit by throwing ExitStatus after it ran the exit hooks.
    try { runtime.exit(1, new Error(cause)); } catch { /* the runtime is stopped either way */ }
  }, reason);
}

/**
 * Makes the next request the page's .NET runtime dispatches hit a genuine WebAssembly trap
 * (`WebAssembly.RuntimeError`), as K4os' LZ4 HC encoder once did on a Witcher 3 download ("memory access
 * out of bounds"). The trap comes from a one-instruction module (`unreachable`), raised inside the
 * runtime's own dispatch call. Afterwards the runtime never answers again, like one whose managed locks
 * the trap left held: the app must fail every pending and later request instead of waiting for it.
 */
export async function trapRuntime(page: Page) {
  const worker = await runtimeWorker(page);
  await worker.evaluate(async () => {
    type Interop = { DispatchAsync(...args: unknown[]): Promise<number> };
    const runtime = (globalThis as unknown as { getDotnetRuntime(id: number): { getAssemblyExports(name: string): Promise<unknown> } })
      .getDotnetRuntime(0);
    // The runtime caches each assembly's exports: this is the object the worker dispatches through.
    const exports = await runtime.getAssemblyExports("SaveToolkit.Browser") as { SaveToolkit: { Browser: { Interop: Interop } } };
    // (module (func (export "trap") unreachable))
    const trapModule = new WebAssembly.Module(new Uint8Array([0, 97, 115, 109, 1, 0, 0, 0, 1, 4, 1, 96, 0, 0, 3, 2, 1, 0,
      7, 8, 1, 4, 116, 114, 97, 112, 0, 0, 10, 5, 1, 3, 0, 0, 11]));
    const trap = new WebAssembly.Instance(trapModule).exports.trap as () => void;
    let trapped = false;
    exports.SaveToolkit.Browser.Interop.DispatchAsync = () => {
      if (!trapped) {
        trapped = true;
        trap();
      }
      return new Promise<number>(() => undefined);
    };
  });
}

export async function runtimeFiles(page: Page) {
  const worker = await runtimeWorker(page);
  type Fs = { readFile(path: string, options: { encoding: "utf8" }): string; writeFile(path: string, data: string): void;
    rename(from: string, to: string): void; unlink(path: string): void; mkdir(path: string): void; rmdir(path: string): void; syncfs(populate: boolean, done: (error: unknown) => void): void };
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
            else if (change.kind === "mkdir") fs.mkdir(change.path);
            else if (change.kind === "rmdir") fs.rmdir(change.path);
            else fs.unlink(change.path);
          }
          await new Promise<void>((resolve, reject) => fs.syncfs(false, error => error ? reject(error as Error) : resolve()));
        };
        await (navigator.locks ? navigator.locks.request(lock, { mode: "exclusive" }, apply) : apply());
      }, [storageLock, changes] as const);
    },
  };
}
