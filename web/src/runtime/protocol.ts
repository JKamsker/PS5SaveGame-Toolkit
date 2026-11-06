/** Messages between the page (runtime/client.ts) and the .NET runtime worker (runtime/worker.ts). */

export type HeaderPairs = [string, string][];

export interface BootInfo {
  version: string;
  plugins: string[];
  /** Plugin assemblies that failed to load and were skipped. */
  failedAssemblies: string[];
  /** False when IndexedDB is unavailable and presets/reference packs only live for this page. */
  persistent: boolean;
  /**
   * True when Web Locks serialize preset and reference-pack writes across tabs. Without them (an
   * insecure origin or an old browser) tabs of the same site could overwrite each other's saves.
   */
  tabCoordination: boolean;
  /** Total boot time in the worker, from importing dotnet.js to a ready API host. */
  elapsedMs: number;
  /** Part of `elapsedMs` spent creating the API host (plugin discovery, embedded reference data). */
  initializeMs: number;
}

export interface WireRequest {
  id: number;
  method: string;
  /** Path and query, e.g. `/api/plugins`, percent-encoded as a browser would send it. */
  url: string;
  headers: HeaderPairs;
  /** Raw body bytes, transferred to the worker. */
  body: ArrayBuffer | null;
}

export type ToWorker =
  | { type: "boot"; frameworkUrl: string }
  | { type: "request"; request: WireRequest }
  /** Drops a request the worker has not started yet. */
  | { type: "cancel"; id: number };

export type FromWorker =
  /** `received`: bytes of runtime files downloaded so far (also posted while one large file is still arriving). */
  | { type: "progress"; loaded: number; total: number; received?: number; starting?: boolean }
  /** The worker began executing the request (it left the queue); request deadlines start here. */
  | { type: "started"; id: number }
  | { type: "ready"; boot: BootInfo }
  | { type: "failed"; message: string }
  /** `frame` is the whole framed response (transferred); the body starts at `bodyOffset`. */
  | { type: "response"; id: number; status: number; headers: HeaderPairs; frame: ArrayBuffer; bodyOffset: number }
  /**
   * `outOfMemory` marks failures caused by an exhausted WebAssembly heap or buffer allocation. `stopped`:
   * the worker found its runtime stopped (aborted, exited, trapped); the page then fails every request.
   * Only the worker decides that: it sees the thrown value, the page only its message.
   */
  | { type: "error"; id: number; message: string; outOfMemory?: boolean; stopped: boolean };
