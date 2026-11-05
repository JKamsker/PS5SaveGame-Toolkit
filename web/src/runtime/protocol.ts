/** Messages between the page (runtime/client.ts) and the .NET runtime worker (runtime/worker.ts). */

export type HeaderPairs = [string, string][];

export interface BootInfo {
  version: string;
  plugins: string[];
  /** Plugin assemblies that failed to load and were skipped. */
  failedAssemblies: string[];
  /** False when IndexedDB is unavailable and presets/reference packs only live for this page. */
  persistent: boolean;
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
  | { type: "progress"; loaded: number; total: number; starting?: boolean }
  | { type: "ready"; boot: BootInfo }
  | { type: "failed"; message: string }
  /** `frame` is the whole framed response (transferred); the body starts at `bodyOffset`. */
  | { type: "response"; id: number; status: number; headers: HeaderPairs; frame: ArrayBuffer; bodyOffset: number }
  | { type: "error"; id: number; message: string };
