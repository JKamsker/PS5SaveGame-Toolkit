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
 * 4. announce what changed on a BroadcastChannel, so other tabs reload before their next request.
 *
 * After such an announcement (or when the browser closed the IndexedDB connection, for example "Clear
 * site data", or IndexedDB's generation marker differs from the loaded one because an announcement was
 * lost), reads of presets and reference packs refresh under a shared lock, waiting only briefly:
 * while another tab holds the lock (a long import, a frozen tab) they answer from the cached view and
 * retry later, and reads of anything else never wait. A failed flush is reported as an error response
 * (never a silent success), and the tab reloads what IndexedDB really holds. When the stores cannot
 * re-read another tab's files (for example a library saved by a newer version of the site), the tab
 * keeps its last good view for reads but refuses mutations, which would write that stale view back.
 *
 * A mutation whose .NET dispatch never settles would hold the exclusive lock until the tab closes (Web
 * Locks are released with the page and its workers). After MUTATION_WATCHDOG_MS the lock is released and
 * this tab's persistence stops for good (`RuntimeHungError`): the worker reports the runtime as crashed,
 * and the page offers a reload. A synchronous hang blocks the worker's timers too; only closing or
 * reloading that tab releases the lock then.
 */
import { readsDurableData, writesDurableData } from "./durableRoutes";
import { describeError, isRuntimeStopped } from "./failures";
import { decodeFrame, problemFrame } from "./framing";
import { changedData, type PersistentData } from "./persistentChanges";
import { StoredGenerationCheck, type GenerationCheckTiming } from "./storedGeneration";

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
  /** The generation marker of the in-memory tree ("" when there is none). */
  readGeneration(): string;
  /** The marker IndexedDB holds now ("" when none), without loading the tree; absent without IDBFS' internals. */
  readStoredGeneration?(): Promise<string>;
  /** Writes the marker; like every tracked change, IDBFS always sees it as changed. */
  writeGeneration(generation: string): void;
  /** Makes the .NET stores re-read their files; returns an error message, or "" on success. */
  reloadStores(): string;
}

export type LockMode = "shared" | "exclusive";

export interface TabCoordination {
  /**
   * Runs `task` holding the site-wide lock; null when the browser has no Web Locks. Waits at most
   * `waitMs` (default LOCK_WAIT_MS; 0: only if available now); rejects without running `task` when not granted.
   */
  lock: (<T>(mode: LockMode, task: () => Promise<T>, waitMs?: number) => Promise<T>) | null;
  /** Tells the other tabs that persistent data changed, and what. */
  announce(changed: readonly PersistentData[]): void;
  /** Registers the handler for the other tabs' announcements. */
  listen(onChange: () => void): void;
}

export interface Persistence {
  /** True when Web Locks keep tabs from overwriting each other's presets and packs. */
  readonly coordinated: boolean;
  /** Runs one request; mutations of durable data are serialized across tabs and flushed. */
  run(method: string, url: string, dispatch: () => Promise<Uint8Array>): Promise<Uint8Array>;
  /**
   * The runtime stopped: a stopped .NET runtime never settles the dispatch in flight, so stop waiting for
   * it (releasing the cross-tab lock now rather than after the watchdog) and never touch storage again.
   */
  stop(reason: string): void;
}

/** Longest wait for another tab's write before giving up with an error (never a lost write). */
export const LOCK_WAIT_MS = 30_000;
/** Longest wait of a preset or pack read for fresh data; it answers from the cached view after that. */
export const READ_LOCK_WAIT_MS = 1_000;
/** A mutation still unanswered after this long (twice the UI's longest deadline) is treated as hung. */
export const MUTATION_WATCHDOG_MS = 10 * 60_000;
/** After IndexedDB's generation marker could not be read, reads do not try again for this long. */
export const MARKER_RETRY_MS = 30_000;

export interface CoordinatorTiming extends GenerationCheckTiming {
  readLockWaitMs: number;
  mutationWatchdogMs: number;
}

export { readsDurableData, writesDurableData } from "./durableRoutes";

/** Without persistence (no IndexedDB) every request simply runs. */
export const memoryOnly: Persistence = { coordinated: false, run: (_method, _url, dispatch) => dispatch(), stop: () => {} };

class LockUnavailableError extends Error {}

/**
 * A mutation's dispatch never settled, or the runtime stopped (aborted, exited, trapped): this tab's
 * runtime is unusable and must not touch storage again.
 */
export class RuntimeHungError extends Error {
  override name = "RuntimeHungError";
}

/** The .NET stores could not re-read the files another tab saved; they still hold their last good view. */
class StoreReloadError extends Error {}

export class PersistenceCoordinator implements Persistence {
  /**
   * The `.generation` the .NET stores reflect. Null when unknown (not loaded yet, or a failed flush could
   * not be undone): the next refresh then always makes the stores re-read their files.
   */
  private generation: string | null = null;
  private stale = false;
  /** A read found the lock held: later reads do not wait for it until a lock is granted again. */
  private lockBusy = false;
  /** A read's reload of the tree failed: later failures are not reported again until one works. */
  private loadFailing = false;
  private hung: RuntimeHungError | null = null;
  /** Rejects the dispatch the running mutation waits for (it holds the exclusive lock until then). */
  private readonly inFlight = new Set<(error: RuntimeHungError) => void>();
  private readonly timing: CoordinatorTiming;
  /** Notices a change another tab saved but could not announce. */
  private readonly storedGeneration: StoredGenerationCheck;

  constructor(private readonly storage: PersistentStorage, private readonly tabs: TabCoordination,
    private readonly log: Pick<Console, "error" | "warn"> = console, timing: Partial<CoordinatorTiming> = {}) {
    this.timing = { readLockWaitMs: READ_LOCK_WAIT_MS, mutationWatchdogMs: MUTATION_WATCHDOG_MS, markerReadMs: READ_LOCK_WAIT_MS,
      markerRetryMs: MARKER_RETRY_MS, ...timing };
    this.storedGeneration = new StoredGenerationCheck(storage, this.timing);
    tabs.listen(() => { this.stale = true; });
  }

  get coordinated() { return this.tabs.lock !== null; }

  /**
   * IndexedDB may no longer hold what this tab loaded (its connection was closed, for example because
   * the site's data was cleared): the next preset or pack read reloads, as after another tab's change.
   */
  invalidate() { this.stale = true; }

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
    try {
      return await this.request(method, url, dispatch);
    } catch (error) {
      // A trap anywhere in the request (a store reload while reverting, a read's dispatch) stops this
      // tab's storage here, whether or not the worker gets to call stop().
      this.stopped(error);
      throw error;
    }
  }

  private async request(method: string, url: string, dispatch: () => Promise<Uint8Array>): Promise<Uint8Array> {
    if (this.hung) throw this.hung;
    if (!writesDurableData(method, url)) {
      if (readsDurableData(url)) {
        // A change another tab saved but could not announce gives no other sign (runtime/storedGeneration.ts).
        if (!this.stale && await this.storedGeneration.changed()) this.stale = true;
        if (this.stale) await this.refreshForRead();
      }
      // The runtime stopped while the read refreshed: its dispatch would never settle (or fail).
      if (this.hung) throw this.hung;
      return dispatch();
    }
    try {
      return await this.locked("exclusive", () => this.mutate(dispatch, changedData(url)));
    } catch (error) {
      if (!(error instanceof LockUnavailableError)) throw error;
      this.log.warn("Waiting for another tab's storage lock failed:", error.cause);
      return problemFrame(503, "Service Unavailable", "Another tab of this site is still saving presets or reference "
        + "packs, so this change was not made. Try again in a moment.");
    }
  }

  /**
   * Another tab saved: serve this read from fresh data if the lock comes quickly, else from the cached
   * view (still stale, so a later read tries again). Before the first load, or while the lock is known
   * to be held, do not wait at all: the tab that holds it may hold it for long.
   */
  private async refreshForRead() {
    const wait = this.generation === null || this.lockBusy ? 0 : this.timing.readLockWaitMs;
    await this.locked("shared", () => this.refresh(), wait).catch((error: unknown) => {
      // Stopped meanwhile, or the stores' reload trapped: the read must not dispatch on that runtime.
      if (error instanceof RuntimeHungError || this.stopped(error)) throw error;
      if (error instanceof StoreReloadError) {
        // Retried by the next mutation, which is refused while it fails; reads keep the last good view.
        this.log.error(`Reading presets and reference packs saved by another tab failed: ${error.message}`);
        return;
      }
      this.stale = true;
      if (error instanceof LockUnavailableError) {
        if (!this.lockBusy) this.log.warn("Another tab holds the storage lock; showing presets and reference packs as last loaded.");
        this.lockBusy = true;
        return;
      }
      // Retried by every later preset or pack read (and mutation), but reported once until a reload works.
      if (!this.loadFailing) this.log.warn("Reloading presets and reference packs from browser storage failed:", error);
      this.loadFailing = true;
    });
  }

  private async mutate(dispatch: () => Promise<Uint8Array>, changed: readonly PersistentData[]): Promise<Uint8Array> {
    // The runtime stopped while this mutation waited for the lock: touch neither the tree nor the stores.
    if (this.hung) throw this.hung;
    try {
      await this.refresh();
    } catch (error) {
      // Stopped meanwhile, or the stores' reload trapped: a crash, not a storage failure.
      if (error instanceof RuntimeHungError || this.stopped(error)) throw error;
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
      frame = await this.watch(dispatch());
    } catch (error) {
      // A hung runtime may still change the tree, and a trapped one must not re-read its stores: neither
      // is flushed or reverted again. Latched before the lock is released, not when the worker stops.
      if (error instanceof RuntimeHungError || this.stopped(error)) throw error;
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
    this.announce(changed);
    return frame;
  }

  /** Best effort: the change is saved, and other tabs see its new generation before their next mutation or durable read. */
  private announce(changed: readonly PersistentData[]) {
    try {
      this.tabs.announce(changed);
    } catch (error) {
      this.log.warn("Telling other tabs about saved presets or reference packs failed:", error);
    }
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
      throw this.hung ?? error;
    }
    this.loadFailing = false;
    this.storedGeneration.reloaded();
    // The runtime stopped while the tree was loading (its dispatch would never settle): re-read nothing.
    if (this.hung) throw this.hung;
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
    if (this.hung) throw this.hung;
    const generation = this.storage.readGeneration();
    const error = this.storage.reloadStores();
    if (error) {
      this.log.error(`Restoring presets and reference packs from browser storage failed: ${error}`);
      return false;
    }
    this.generation = generation;
    return true;
  }

  stop(reason: string) {
    this.hung ??= new RuntimeHungError(reason);
    for (const abandon of [...this.inFlight]) abandon(this.hung);
  }

  /**
   * Whether the runtime stopped; a trap or an exit (`isRuntimeStopped`) stops this tab's storage now. The
   * coordinator's own errors never do: a failed store reload quotes .NET text, even preset file content
   * (a trap cannot reach .NET's catch, so it never arrives as one).
   */
  private stopped(error: unknown): boolean {
    const own = error instanceof StoreReloadError || error instanceof LockUnavailableError;
    if (!this.hung && !own && isRuntimeStopped(error)) this.stop(describeError(error));
    return this.hung !== null;
  }

  /** Rejects with RuntimeHungError (releasing the lock) once `dispatch` outlives the watchdog or the runtime stops. */
  private watch(dispatch: Promise<Uint8Array>): Promise<Uint8Array> {
    let timer: ReturnType<typeof setTimeout> | undefined;
    let abandon: ((error: RuntimeHungError) => void) | undefined;
    const watchdog = new Promise<never>((_, reject) => {
      abandon = reject;
      this.inFlight.add(reject);
      timer = setTimeout(() => {
        this.hung = new RuntimeHungError(`The editor runtime did not finish a change to presets or reference packs within `
          + `${Math.round(this.timing.mutationWatchdogMs / 60_000)} minutes and was stopped, so other tabs can save again. `
          + "Reload the page.");
        this.log.error(this.hung.message);
        reject(this.hung);
      }, this.timing.mutationWatchdogMs);
    });
    return Promise.race([dispatch, watchdog]).finally(() => {
      clearTimeout(timer);
      if (abandon) this.inFlight.delete(abandon);
    });
  }

  private async locked<T>(mode: LockMode, task: () => Promise<T>, waitMs?: number): Promise<T> {
    if (!this.tabs.lock) return task();
    let entered = false;
    try {
      return await this.tabs.lock(mode, () => { entered = true; this.lockBusy = false; return task(); }, waitMs);
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

function newGeneration(): string {
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}
