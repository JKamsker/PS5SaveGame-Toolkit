import { createCompressedResourceLoader, type BootResourceLoader } from "./compressedResources";
import { asError, makeFlushAtomic, readStoredText, reconnectingSync, trackTreeChanges, verifyMirrored, writeAfter,
  type IdbfsConnections, type IdbfsDatabase, type IdbfsReconcile, type IdbfsSets, type TreeFs } from "./idbfsTree";
import { browserTabs } from "./browserTabs";
import { memoryOnly, PersistenceCoordinator, type Persistence, type PersistentStorage } from "./persistence";
import type { BootInfo } from "./protocol";

/** The subset of the .NET WebAssembly host API (`_framework/dotnet.js`) used here. */
interface DotnetHostBuilder {
  withResourceLoader(loader: BootResourceLoader): DotnetHostBuilder;
  /** Present at runtime though omitted from dotnet.d.ts since .NET 9. */
  withModuleConfig(config: {
    onDownloadResourceProgress?: (loaded: number, total: number) => void;
    /** Emscripten's hooks; dotnet.js calls them before it shuts the runtime down. */
    onAbort?: (reason: unknown) => void;
    onExit?: (code: unknown) => void;
  }): DotnetHostBuilder;
  create(): Promise<DotnetRuntime>;
}

interface EmscriptenFs extends TreeFs {
  mkdirTree(path: string): void;
  mount(type: unknown, options: object, mountpoint: string): void;
  syncfs(populate: boolean, callback: (error: unknown) => void): void;
  analyzePath(path: string): { exists: boolean };
  readFile(path: string, options: { encoding: "utf8" }): string;
  writeFile(path: string, data: string): void;
  filesystems: Record<string, unknown>;
}

interface DotnetRuntime {
  Module: { FS: EmscriptenFs };
  getAssemblyExports(assemblyName: string): Promise<unknown>;
}

export interface InteropExports {
  Initialize(optionsJson: string): string;
  DispatchAsync(method: string, url: string, headersJson: string, body: Uint8Array): Promise<number>;
  TakeResponse(handle: number): Uint8Array;
  /** Makes the preset and reference-pack stores re-read their files; returns an error message or "". */
  ReloadPersistent(): string;
}

export interface BootedRuntime {
  interop: InteropExports;
  boot: BootInfo;
  /** Runs requests against the persistent mount: cross-tab locking, reloads and IndexedDB flushes. */
  persistence: Persistence;
}

/** Workspaces are session-scoped (memory only), exactly like the server's temporary workspace folder. */
const SESSION_ROOT = "/savetoolkit/session";
/** Presets and imported reference packs survive reloads through Emscripten's IDBFS. */
const PERSISTENT_ROOT = "/savetoolkit/persist";
/** Changes whenever a tab flushes a change; another value means the .NET stores must re-read their files. */
const GENERATION_FILE = `${PERSISTENT_ROOT}/.generation`;

export interface BootProgress {
  /** Runtime files downloaded so far, out of `total`. */
  onDownload(loaded: number, total: number): void;
  /** Downloads are done; plugins and embedded reference data are being prepared. */
  onStarting(): void;
  /** Bytes of runtime files received, reported per chunk while a file downloads. */
  onBytes?(bytes: number): void;
  /** The runtime aborted (for example out of memory) or exited; it cannot answer another request. */
  onStopped?(reason: string): void;
}

export async function bootDotnet(frameworkUrl: string, progress: BootProgress): Promise<BootedRuntime> {
  if (typeof WebAssembly !== "object") throw new Error("This browser does not support WebAssembly.");
  const started = performance.now();
  // dotnet.js treats any worker as a pthread "deputy" that waits for assets from a main thread and
  // never boots. Declaring a sidecar marks this worker as the runtime's own (single-threaded) host.
  (globalThis as { dotnetSidecar?: boolean }).dotnetSidecar = true;
  const { dotnet } = await import(/* @vite-ignore */ `${frameworkUrl}dotnet.js`) as { dotnet: DotnetHostBuilder };
  const runtime = await dotnet
    .withModuleConfig({
      onDownloadResourceProgress: (loaded, total) => progress.onDownload(loaded, total),
      onAbort: reason => progress.onStopped?.(`Aborted(${reason instanceof Error ? reason.message : String(reason ?? "")})`),
      onExit: code => progress.onStopped?.(`The .NET runtime exited with code ${String(code)}.`),
    })
    .withResourceLoader(createCompressedResourceLoader(fetch, progress.onBytes))
    .create();
  const fs = runtime.Module.FS;
  const assembly = await runtime.getAssemblyExports("SaveToolkit.Browser") as { SaveToolkit: { Browser: { Interop: InteropExports } } };
  const interop = assembly.SaveToolkit.Browser.Interop;
  // A closed IndexedDB connection (site data cleared) makes the next preset or pack read reload.
  const coordinator: PersistenceCoordinator = new PersistenceCoordinator(
    persistentStorage(fs, interop, () => coordinator.invalidate()), browserTabs());
  const persistent = await mountPersistent(fs, coordinator);
  progress.onStarting();
  const initializing = performance.now();
  const info = JSON.parse(interop.Initialize(JSON.stringify({ dataRoot: SESSION_ROOT, persistentRoot: PERSISTENT_ROOT }))) as
    Omit<BootInfo, "persistent" | "elapsedMs" | "initializeMs">;
  return {
    interop,
    boot: {
      ...info, persistent, tabCoordination: persistent && coordinator.coordinated,
      elapsedMs: Math.round(performance.now() - started), initializeMs: Math.round(performance.now() - initializing),
    },
    persistence: persistent ? coordinator : memoryOnly,
  };
}

async function mountPersistent(fs: EmscriptenFs, coordinator: PersistenceCoordinator): Promise<boolean> {
  fs.mkdirTree(PERSISTENT_ROOT);
  const idbfs = fs.filesystems.IDBFS;
  if (!idbfs || typeof indexedDB === "undefined") return false;
  // A failed flush must leave IndexedDB as it was, never holding part of the change (idbfsTree.ts).
  const internals = idbfs as Partial<IdbfsReconcile>;
  if (typeof internals.reconcile === "function" && typeof internals.loadLocalEntry === "function"
    && typeof internals.DB_STORE_NAME === "string") makeFlushAtomic(internals as IdbfsReconcile);
  try {
    fs.mount(idbfs, {}, PERSISTENT_ROOT);
    if (!await coordinator.initialLoad()) {
      // eslint-disable-next-line no-console -- a delay, not a failure: the next request loads the data.
      console.warn("Another tab held the storage lock during startup; presets and reference packs load on the next request.");
    }
    return true;
  } catch (error) {
    // Private browsing or blocked storage: keep the plain in-memory directory.
    // eslint-disable-next-line no-console -- the UI shows `persistent: false`; the console keeps the cause.
    console.warn("Presets and reference packs will not be saved in this tab:", error);
    return false;
  }
}

function persistentStorage(fs: EmscriptenFs, interop: InteropExports, onConnectionLost: () => void): PersistentStorage {
  const idbfs = fs.filesystems.IDBFS as Partial<IdbfsSets & IdbfsConnections & IdbfsDatabase>;
  const sync = (populate: boolean) => syncfs(fs, populate);
  // IDBFS caches its connection forever; without its cache (another Emscripten), sync as it is.
  const synced = typeof idbfs.dbs === "object" && idbfs.dbs !== null
    ? reconnectingSync(idbfs as IdbfsConnections, PERSISTENT_ROOT, sync, onConnectionLost) : sync;
  return {
    load: () => synced(true),
    save: () => synced(false),
    trackChanges: () => trackTreeChanges(fs, PERSISTENT_ROOT),
    // Reads back what IndexedDB holds after a flush (IDBFS' own listings; skipped if a runtime lacks them).
    verifySaved: () => typeof idbfs.getLocalSet === "function" && typeof idbfs.getRemoteSet === "function"
      ? verifyMirrored(idbfs as IdbfsSets, PERSISTENT_ROOT) : Promise.resolve(),
    readGeneration: () => fs.analyzePath(GENERATION_FILE).exists ? fs.readFile(GENERATION_FILE, { encoding: "utf8" }) : "",
    // Notices another tab's save whose announcement was lost (skipped if a runtime lacks these internals).
    readStoredGeneration: typeof idbfs.getDB === "function" && typeof idbfs.DB_STORE_NAME === "string"
      ? () => readStoredText(idbfs as IdbfsDatabase, PERSISTENT_ROOT, GENERATION_FILE) : undefined,
    writeGeneration: generation => writeAfter(fs, GENERATION_FILE, generation),
    reloadStores: () => interop.ReloadPersistent(),
  };
}

function syncfs(fs: EmscriptenFs, populate: boolean): Promise<void> {
  return new Promise((resolve, reject) => fs.syncfs(populate, error => error ? reject(asError(error)) : resolve()));
}
