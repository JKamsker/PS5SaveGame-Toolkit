import { createCompressedResourceLoader, type BootResourceLoader } from "./compressedResources";
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
}

export interface BootedRuntime {
  interop: InteropExports;
  boot: BootInfo;
  /** Flushes the persistent directory to IndexedDB; a no-op without persistence. */
  persist(): Promise<void>;
}

/** Workspaces are session-scoped (memory only), exactly like the server's temporary workspace folder. */
const SESSION_ROOT = "/savetoolkit/session";
/** Presets and imported reference packs survive reloads through Emscripten's IDBFS. */
const PERSISTENT_ROOT = "/savetoolkit/persist";

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
  const persistent = await mountPersistent(fs);
  const assembly = await runtime.getAssemblyExports("SaveToolkit.Browser") as { SaveToolkit: { Browser: { Interop: InteropExports } } };
  const interop = assembly.SaveToolkit.Browser.Interop;
  progress.onStarting();
  const initializing = performance.now();
  const info = JSON.parse(interop.Initialize(JSON.stringify({ dataRoot: SESSION_ROOT, persistentRoot: PERSISTENT_ROOT }))) as
    Omit<BootInfo, "persistent" | "elapsedMs" | "initializeMs">;
  return {
    interop,
    boot: { ...info, persistent, elapsedMs: Math.round(performance.now() - started), initializeMs: Math.round(performance.now() - initializing) },
    persist: persistent ? () => syncfs(fs, false) : () => Promise.resolve(),
  };
}

async function mountPersistent(fs: EmscriptenFs): Promise<boolean> {
  fs.mkdirTree(PERSISTENT_ROOT);
  const idbfs = fs.filesystems.IDBFS;
  if (!idbfs || typeof indexedDB === "undefined") return false;
  try {
    fs.mount(idbfs, {}, PERSISTENT_ROOT);
    await syncfs(fs, true);
    return true;
  } catch {
    // Private browsing or blocked storage: keep the plain in-memory directory.
    return false;
  }
}

function syncfs(fs: EmscriptenFs, populate: boolean): Promise<void> {
  return new Promise((resolve, reject) => fs.syncfs(populate, error => error ? reject(error instanceof Error ? error : new Error(String(error))) : resolve()));
}
