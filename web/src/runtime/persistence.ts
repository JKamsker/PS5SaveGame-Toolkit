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
 * response (never a silent success), and the tab reloads what IndexedDB really holds. When the stores
 * cannot re-read another tab's files (for example a library saved by a newer version of the site), the
 * tab keeps its last good view for reads but refuses mutations, which would write that stale view back.
 */
import { decodeFrame, problemFrame } from "./framing";

/** The worker's view of the persistent mount and the .NET stores on top of it. */
export interface PersistentStorage {
  /** Replaces the in-memory tree with what IndexedDB holds (`FS.syncfs(true)`). */
  load(): Promise<void>;
  /** Writes the in-memory tree to IndexedDB (`FS.syncfs(false)`). */
  save(): Promise<void>;
  /**
   * Starts recording changes to the in-memory tree. The returned function stops recording (calling it
   * again returns the same answer) and tells whether anything changed; changed entries then carry
   * modification times IDBFS cannot mistake for the stored ones, whatever the clock's resolution.
   */
  trackChanges(): () => boolean;
  /** After `save()`: rejects unless IndexedDB now holds exactly the in-memory tree. */
  verifySaved(): Promise<void>;
  readGeneration(): string;
  /** Writes the marker; like every tracked change, IDBFS always sees it as changed. */
  writeGeneration(generation: string): void;
  /** Makes the .NET stores re-read their files; returns an error message, or "" on success. */
  reloadStores(): string;
}

export type LockMode = "shared" | "exclusive";

export interface TabCoordination {
  /**
   * Runs `task` holding the site-wide lock; null when the browser has no Web Locks. Waits at most
   * LOCK_WAIT_MS, or not at all with `ifAvailable`; rejects without running `task` when not granted.
   */
  lock: (<T>(mode: LockMode, task: () => Promise<T>, ifAvailable?: boolean) => Promise<T>) | null;
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
 * their preset routes take part (capture saves a preset), except those known to only read the library:
 * preview, apply (edits the document) and transfer compatibility. Every other mutation is durable.
 */
export function writesDurableData(method: string, url: string): boolean {
  const verb = method.toUpperCase();
  if (verb === "GET" || verb === "HEAD") return false;
  const path = (url.split(/[?#]/, 1)[0] ?? "").toLowerCase();
  if (path === "/api/workspaces" || path.startsWith("/api/workspaces/"))
    return /\/presets(\/|$)/.test(path) && !/\/presets\/(?:[^/]+\/(?:preview|apply)|transfer\/compatibility)\/?$/.test(path);
  return true;
}

class LockUnavailableError extends Error {}

/** The .NET stores could not re-read the files another tab saved; they still hold their last good view. */
class StoreReloadError extends Error {}

export class PersistenceCoordinator implements Persistence {
  /**
   * The `.generation` the .NET stores reflect. Null when unknown (not loaded yet, or a failed flush could
   * not be undone): the next refresh then always makes the stores re-read their files.
   */
  private generation: string | null = null;
  private stale = false;

  constructor(private readonly storage: PersistentStorage, private readonly tabs: TabCoordination,
    private readonly log: Pick<Console, "error" | "warn"> = console) {
    tabs.listen(() => { this.stale = true; });
  }

  get coordinated() { return this.tabs.lock !== null; }

  /**
   * Boot: the first load from IndexedDB, before the .NET host reads the files. Throws when storage is
   * unavailable. Returns false when another tab held the lock too long: the mount then starts empty and
   * is loaded by the first request that gets the lock (a mutation always waits for it and loads first).
   */
  async initialLoad(): Promise<boolean> {
    try {
      await this.locked("shared", async () => {
        await this.storage.load();
        this.generation = this.storage.readGeneration();
      });
      return true;
    } catch (error) {
      if (!(error instanceof LockUnavailableError)) throw error;
      this.generation = null;
      this.stale = true;
      return false;
    }
  }

  async run(method: string, url: string, dispatch: () => Promise<Uint8Array>): Promise<Uint8Array> {
    if (!writesDurableData(method, url)) {
      if (this.stale) {
        // Another tab saved: serve this read from fresh data. On failure keep the cached view. Before the
        // first load, do not wait: the tab that holds the lock may hold it for long.
        await this.locked("shared", () => this.refresh(), this.generation === null).catch((error: unknown) => {
          if (error instanceof StoreReloadError) {
            // Retried by the next mutation, which is refused while it fails; reads keep the last good view.
            this.log.error(`Reading presets and reference packs saved by another tab failed: ${error.message}`);
            return;
          }
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
      if (error instanceof StoreReloadError) {
        this.log.error(`Reading presets and reference packs saved by another tab failed: ${error.message}`);
        // Running the request on the last good view could write it back over the other tab's saves.
        return problemFrame(503, "Service Unavailable", "Presets or reference packs saved in another tab could not "
          + `be read here (${error.message}), so this change was not made: it could have overwritten them. Reload `
          + "the page; if another tab runs a newer version of this site, use that version.");
      }
      this.log.error("Reading presets and reference packs from browser storage failed:", error);
      return problemFrame(503, "Service Unavailable", "Browser storage could not be read, so presets and reference "
        + "packs were not changed. Reload the page and try again.");
    }
    const changes = this.storage.trackChanges();
    let frame: Uint8Array;
    try {
      frame = await dispatch();
    } catch (error) {
      // No answer reports this as saved: undo whatever the failed request left behind.
      if (changes()) await this.revert();
      throw error;
    }
    if (!changes()) return frame;
    const generation = newGeneration();
    try {
      this.storage.writeGeneration(generation);
      await this.storage.save();
      await this.storage.verifySaved();
    } catch (error) {
      this.log.error("Saving presets and reference packs to browser storage failed:", error);
      const reverted = await this.revert();
      return decodeFrame(frame).status >= 400 ? frame : storageFailure(error, reverted);
    }
    this.generation = generation;
    this.tabs.announce();
    return frame;
  }

  /**
   * Reloads the tree; the .NET stores re-read their files when another tab changed them. Adopts the new
   * generation only once they did, so a failed reload is retried (and mutations refused) until it works.
   */
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
    const error = this.storage.reloadStores();
    if (error) throw new StoreReloadError(error);
    this.generation = generation;
  }

  /**
   * After a failed flush: show what IndexedDB really holds. Returns false when even that failed; the
   * generation is then unknown, so the next request reloads the tree and the stores before any write.
   */
  private async revert(): Promise<boolean> {
    this.generation = null;
    try {
      await this.storage.load();
    } catch (error) {
      this.stale = true;
      this.log.error("Restoring presets and reference packs from browser storage failed:", error);
      return false;
    }
    const generation = this.storage.readGeneration();
    const error = this.storage.reloadStores();
    if (error) {
      this.log.error(`Restoring presets and reference packs from browser storage failed: ${error}`);
      return false;
    }
    this.generation = generation;
    return true;
  }

  private async locked<T>(mode: LockMode, task: () => Promise<T>, ifAvailable = false): Promise<T> {
    if (!this.tabs.lock) return task();
    let entered = false;
    try {
      return await this.tabs.lock(mode, () => { entered = true; return task(); }, ifAvailable);
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
      ? (mode, task, ifAvailable) => ifAvailable
        ? locks.request(LOCK_NAME, { mode, ifAvailable: true }, lock => lock ? task() : Promise.reject(new DOMException("The storage lock is held by another tab.", "AbortError")))
        : locks.request(LOCK_NAME, { mode, signal: AbortSignal.timeout(LOCK_WAIT_MS) }, task)
      : null,
    announce: () => channel?.postMessage("changed"),
    listen: onChange => { if (channel) channel.onmessage = () => onChange(); },
  };
}

function newGeneration(): string {
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}
