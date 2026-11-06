import { createCompressedResourceLoader, type BootResourceLoader } from "./compressedResources";
import { browserTabs, memoryOnly, PersistenceCoordinator, type Persistence, type PersistentStorage } from "./persistence";
import type { BootInfo } from "./protocol";

/** The subset of the .NET WebAssembly host API (`_framework/dotnet.js`) used here. */
interface DotnetHostBuilder {
  withResourceLoader(loader: BootResourceLoader): DotnetHostBuilder;
  /** Present at runtime though omitted from dotnet.d.ts since .NET 9. */
  withModuleConfig(config: { onDownloadResourceProgress?: (loaded: number, total: number) => void }): DotnetHostBuilder;
  create(): Promise<DotnetRuntime>;
}

interface EmscriptenFs {
  mkdirTree(path: string): void;
  mount(type: unknown, options: object, mountpoint: string): void;
  syncfs(populate: boolean, callback: (error: unknown) => void): void;
  readdir(path: string): string[];
  /** `mtime` is a Date in .NET 10's Emscripten; newer Emscripten versions report milliseconds. */
  stat(path: string): { mode: number; size: number; mtime: Date | number };
  isDir(mode: number): boolean;
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
}

export async function bootDotnet(frameworkUrl: string, progress: BootProgress): Promise<BootedRuntime> {
  if (typeof WebAssembly !== "object") throw new Error("This browser does not support WebAssembly.");
  const started = performance.now();
  // dotnet.js treats any worker as a pthread "deputy" that waits for assets from a main thread and
  // never boots. Declaring a sidecar marks this worker as the runtime's own (single-threaded) host.
  (globalThis as { dotnetSidecar?: boolean }).dotnetSidecar = true;
  const { dotnet } = await import(/* @vite-ignore */ `${frameworkUrl}dotnet.js`) as { dotnet: DotnetHostBuilder };
  const runtime = await dotnet
    .withModuleConfig({ onDownloadResourceProgress: (loaded, total) => progress.onDownload(loaded, total) })
    .withResourceLoader(createCompressedResourceLoader())
    .create();
  const fs = runtime.Module.FS;
  const assembly = await runtime.getAssemblyExports("SaveToolkit.Browser") as { SaveToolkit: { Browser: { Interop: InteropExports } } };
  const interop = assembly.SaveToolkit.Browser.Interop;
  const coordinator = new PersistenceCoordinator(persistentStorage(fs, interop), browserTabs());
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
  try {
    fs.mount(idbfs, {}, PERSISTENT_ROOT);
    await coordinator.initialLoad();
    return true;
  } catch (error) {
    // Private browsing, blocked storage or a lock held too long: keep the plain in-memory directory.
    // eslint-disable-next-line no-console -- the UI shows `persistent: false`; the console keeps the cause.
    console.warn("Presets and reference packs will not be saved in this tab:", error);
    return false;
  }
}

function persistentStorage(fs: EmscriptenFs, interop: InteropExports): PersistentStorage {
  return {
    load: () => syncfs(fs, true),
    save: () => syncfs(fs, false),
    fingerprint: () => treeFingerprint(fs, PERSISTENT_ROOT),
    readGeneration: () => fs.analyzePath(GENERATION_FILE).exists ? fs.readFile(GENERATION_FILE, { encoding: "utf8" }) : "",
    writeGeneration: generation => fs.writeFile(GENERATION_FILE, generation),
    reloadStores: () => interop.ReloadPersistent(),
  };
}

function treeFingerprint(fs: EmscriptenFs, root: string): string {
  const entries: string[] = [];
  const walk = (directory: string) => {
    for (const name of fs.readdir(directory).sort()) {
      if (name === "." || name === "..") continue;
      const path = `${directory}/${name}`;
      const { mode, size, mtime } = fs.stat(path);
      if (fs.isDir(mode)) {
        entries.push(`${path}/`);
        walk(path);
      } else {
        entries.push(`${path} ${size} ${mtime instanceof Date ? mtime.getTime() : Number(mtime)}`);
      }
    }
  };
  walk(root);
  return entries.join("\n");
}

function syncfs(fs: EmscriptenFs, populate: boolean): Promise<void> {
  return new Promise((resolve, reject) => fs.syncfs(populate, error => error ? reject(error instanceof Error ? error : new Error(String(error))) : resolve()));
}
