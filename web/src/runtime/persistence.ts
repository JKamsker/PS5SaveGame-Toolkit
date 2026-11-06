/**
 * Presets and reference packs live on an Emscripten IDBFS mount, which every tab of the site shares
 * through one IndexedDB database. `FS.syncfs(false)` mirrors a tab's whole in-memory tree onto that
 * database, so a tab flushing a stale tree would delete or revert what another tab saved. This module
 * makes every write a short critical section across tabs:
 *
 * 1. take an exclusive Web Lock (all tabs of the origin),
 * 2. reload the tree from IndexedDB and, when another tab changed it (a generation marker differs),
 *    make the .NET stores re-read their files,
 * 3. run the request, and flush only if the tree changed,
 * 4. announce the change on a BroadcastChannel, so other tabs reload before their next request.
 *
 * Reads refresh under a shared lock after such an announcement. A failed flush is reported as an error
 * response (never a silent success), and the tab reloads what IndexedDB really holds.
 */
import { decodeFrame, problemFrame } from "./framing";

/** The worker's view of the persistent mount and the .NET stores on top of it. */
export interface PersistentStorage {
  /** Replaces the in-memory tree with what IndexedDB holds (`FS.syncfs(true)`). */
  load(): Promise<void>;
  /** Writes the in-memory tree to IndexedDB (`FS.syncfs(false)`). */
  save(): Promise<void>;
  /**
   * Paths, sizes and modification times of the in-memory tree. IDBFS itself detects changes by
   * modification time, so this sees exactly the changes a flush would write.
   */
  fingerprint(): string;
  readGeneration(): string;
  writeGeneration(generation: string): void;
  /** Makes the .NET stores re-read their files; returns an error message, or "" on success. */
  reloadStores(): string;
}

export type LockMode = "shared" | "exclusive";

export interface TabCoordination {
  /** Runs `task` holding the site-wide lock; null when the browser has no Web Locks. */
  lock: (<T>(mode: LockMode, task: () => Promise<T>) => Promise<T>) | null;
  /** Tells the other tabs that persistent data changed. */
  announce(): void;
  /** Registers the handler for the other tabs' announcements. */
  listen(onChange: () => void): void;
}

export interface Persistence {
  /** True when Web Locks keep tabs from overwriting each other's presets and packs. */
  readonly coordinated: boolean;
  /** Runs one request; mutations of durable data are serialized across tabs and flushed. */
  run(method: string, url: string, dispatch: () => Promise<Uint8Array>): Promise<Uint8Array>;
}

const LOCK_NAME = "savetoolkit-persistent-storage";
const CHANNEL_NAME = "savetoolkit-persistent-storage";
/** Longest wait for another tab's write before giving up with an error (never a lost write). */
export const LOCK_WAIT_MS = 30_000;

/** Without persistence (no IndexedDB) every request simply runs. */
export const memoryOnly: Persistence = { coordinated: false, run: (_method, _url, dispatch) => dispatch() };

/**
 * Whether a request may write presets or reference packs. Workspaces live in the session root, so only
 * their preset routes (capture saves a preset) take part; every other mutation is treated as durable.
 */
export function writesDurableData(method: string, url: string): boolean {
  const verb = method.toUpperCase();
  if (verb === "GET" || verb === "HEAD") return false;
  const path = (url.split(/[?#]/, 1)[0] ?? "").toLowerCase();
  if (path === "/api/workspaces" || path.startsWith("/api/workspaces/")) return /\/presets(\/|$)/.test(path);
  return true;
}

class LockUnavailableError extends Error {}

export class PersistenceCoordinator implements Persistence {
  private generation = "";
  private stale = false;

  constructor(private readonly storage: PersistentStorage, private readonly tabs: TabCoordination,
    private readonly log: Pick<Console, "error" | "warn"> = console) {
    tabs.listen(() => { this.stale = true; });
  }

  get coordinated() { return this.tabs.lock !== null; }

  /** Boot: the first load from IndexedDB, before the .NET host reads the files. Throws when unavailable. */
  async initialLoad(): Promise<void> {
    await this.locked("shared", async () => {
      this.stale = false;
      await this.storage.load();
      this.generation = this.storage.readGeneration();
    });
  }

  async run(method: string, url: string, dispatch: () => Promise<Uint8Array>): Promise<Uint8Array> {
    if (!writesDurableData(method, url)) {
      if (this.stale) {
        // Another tab saved: serve this read from fresh data. On failure keep the cached view.
        await this.locked("shared", () => this.refresh()).catch((error: unknown) => {
          this.stale = true;
          this.log.warn("Reloading presets and reference packs from browser storage failed:", error);
        });
      }
      return dispatch();
    }
    try {
      return await this.locked("exclusive", () => this.mutate(dispatch));
    } catch (error) {
      if (!(error instanceof LockUnavailableError)) throw error;
      this.log.warn("Waiting for another tab's storage lock failed:", error.cause);
      return problemFrame(503, "Service Unavailable", "Another tab of this site is still saving presets or reference "
        + "packs, so this change was not made. Try again in a moment.");
    }
  }

  private async mutate(dispatch: () => Promise<Uint8Array>): Promise<Uint8Array> {
    try {
      await this.refresh();
    } catch (error) {
      this.log.error("Reading presets and reference packs from browser storage failed:", error);
      return problemFrame(503, "Service Unavailable", "Browser storage could not be read, so presets and reference "
        + "packs were not changed. Reload the page and try again.");
    }
    const before = this.storage.fingerprint();
    let frame: Uint8Array;
    try {
      frame = await dispatch();
    } catch (error) {
      // No answer reports this as saved: undo whatever the failed request left behind.
      if (this.storage.fingerprint() !== before) await this.revert();
      throw error;
    }
    if (this.storage.fingerprint() === before) return frame;
    const generation = newGeneration();
    this.storage.writeGeneration(generation);
    try {
      await this.storage.save();
    } catch (error) {
      this.log.error("Saving presets and reference packs to browser storage failed:", error);
      const reverted = await this.revert();
      return decodeFrame(frame).status >= 400 ? frame : storageFailure(error, reverted);
    }
    this.generation = generation;
    this.tabs.announce();
    return frame;
  }

  /** Reloads the tree; the .NET stores re-read their files only when another tab changed them. */
  private async refresh() {
    this.stale = false;
    try {
      await this.storage.load();
    } catch (error) {
      this.stale = true;
      throw error;
    }
    const generation = this.storage.readGeneration();
    if (generation === this.generation) return;
    this.generation = generation;
    this.reloadStores();
  }

  /** After a failed flush: show what IndexedDB really holds. Returns false when even that failed. */
  private async revert(): Promise<boolean> {
    try {
      await this.storage.load();
    } catch (error) {
      this.stale = true;
      this.log.error("Restoring presets and reference packs from browser storage failed:", error);
      return false;
    }
    this.generation = this.storage.readGeneration();
    this.reloadStores();
    return true;
  }

  private reloadStores() {
    const error = this.storage.reloadStores();
    if (error) this.log.error(`Reloading presets and reference packs failed: ${error}`);
  }

  private async locked<T>(mode: LockMode, task: () => Promise<T>): Promise<T> {
    if (!this.tabs.lock) return task();
    let entered = false;
    try {
      return await this.tabs.lock(mode, () => { entered = true; return task(); });
    } catch (error) {
      if (entered) throw error;
      throw new LockUnavailableError("The storage lock is unavailable.", { cause: error });
    }
  }
}

/** The answer for a mutation whose result could not be stored: an error, never a plain success. */
export function storageFailure(error: unknown, reverted: boolean): Uint8Array {
  const quota = error instanceof Error && (error.name === "QuotaExceededError" || /quota/i.test(error.message));
  const reason = error instanceof Error ? `${error.name}: ${error.message}` : String(error);
  let detail = quota
    ? "The browser has no storage space left for this site, so the change was not saved. Free up storage "
      + "(for example remove reference packs or presets you no longer need) and try again."
    : `The browser could not store the change (${reason}), so it was not saved. Try again.`;
  if (!reverted) detail += " This tab may still show the change until the page is reloaded.";
  return quota ? problemFrame(507, "Insufficient Storage", detail) : problemFrame(503, "Service Unavailable", detail);
}

/** Web Locks and a BroadcastChannel, where the browser has them. */
export function browserTabs(): TabCoordination {
  const locks = typeof navigator === "object" ? navigator.locks : undefined;
  const channel = typeof BroadcastChannel === "function" ? new BroadcastChannel(CHANNEL_NAME) : null;
  return {
    lock: locks
      ? (mode, task) => locks.request(LOCK_NAME, { mode, signal: AbortSignal.timeout(LOCK_WAIT_MS) }, task)
      : null,
    announce: () => channel?.postMessage("changed"),
    listen: onChange => { if (channel) channel.onmessage = () => onChange(); },
  };
}

function newGeneration(): string {
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}
