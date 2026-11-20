/**
 * How a preset or pack read notices a change another tab saved but could not announce, and how such
 * reads leave a broken IndexedDB alone. The read compares IndexedDB's `.generation` marker with the one
 * the mounted tree holds (one small read, no lock). That check must never cost more than the change it
 * looks for, so it converges in every case:
 *
 * - **A failing IndexedDB.** A marker read that fails or does not settle within `markerReadMs` is
 *   "unknown": it reloads the tree (which reconnects to IndexedDB), and so does nothing else for a while:
 *   after it, or after a read's reload that failed, reads do not touch storage for `retryMs` (measured on
 *   the monotonic clock). So while IndexedDB keeps failing a read reloads at most once per period, and
 *   another tab's unannounced save is still seen within about one period. An announcement or a new
 *   connection ends the period early: storage then likely works again. Only this marker read is bounded:
 *   the read waits for the reload itself (runtime/persistence.ts), however long IndexedDB takes.
 * - **A stored marker a reload did not bring into the tree.** `FS.syncfs(true)` replaces an entry only
 *   when its modification time differs from the stored one, so a marker with another content but the same
 *   time stays as it is however often the tree is reloaded. That stored marker is remembered and does
 *   not cause another reload until IndexedDB holds a different one.
 *
 * Neither hides a change for good: every mutation reloads the tree regardless.
 */
import type { PersistentStorage } from "./storageContract";

export interface GenerationCheckTiming {
  /** Longest wait for IndexedDB's marker; the answer is "unknown" after that. */
  markerReadMs: number;
  /** After storage failed a read (an unknown marker, a failed reload), reads leave it alone this long. */
  retryMs: number;
  /** A monotonic clock in milliseconds (a wall-clock step must not stretch the retry period). */
  now: () => number;
}

export class StoredGenerationCheck {
  /** Reads leave storage alone before this time (`now()`). */
  private retryAt = -Infinity;
  /** The stored marker the pending reload is for. */
  private reloadingFor: string | null = null;
  /** A stored marker a reload could not bring into the tree: it does not cause another reload. */
  private unconverged: string | null = null;

  constructor(private readonly storage: Pick<PersistentStorage, "readGeneration" | "readStoredGeneration">,
    private readonly timing: GenerationCheckTiming) {}

  /** Whether a read may touch storage now: false within the retry period after a failure. */
  get ready() { return this.timing.now() >= this.retryAt; }

  /** Storage failed a read: reads leave it alone for the retry period. */
  backOff() { this.retryAt = this.timing.now() + this.timing.retryMs; }

  /** Storage likely works again (another tab saved, a new connection): reads may try it now. */
  retry() { this.retryAt = -Infinity; }

  /** Whether the tree must be reloaded before this durable read; never throws. */
  async changed(): Promise<boolean> {
    if (!this.storage.readStoredGeneration) return false;
    let stored: string;
    try {
      stored = await bounded(this.storage.readStoredGeneration(), this.timing.markerReadMs);
    } catch {
      this.backOff();
      return true;
    }
    if (stored === this.storage.readGeneration()) {
      this.unconverged = null;
      return false;
    }
    if (stored === this.unconverged) return false;
    this.reloadingFor = stored;
    return true;
  }

  /** The tree was just reloaded from IndexedDB (for a read or a mutation). */
  reloaded() {
    if (this.reloadingFor !== null && this.reloadingFor !== this.storage.readGeneration()) this.unconverged = this.reloadingFor;
    this.reloadingFor = null;
  }
}

/** `promise`, or a rejection once it took longer than `ms`. */
function bounded<T>(promise: Promise<T>, ms: number): Promise<T> {
  let timer: ReturnType<typeof setTimeout> | undefined;
  const timeout = new Promise<never>((_, reject) => {
    timer = setTimeout(() => reject(new Error(`Browser storage did not answer within ${ms} ms.`)), ms);
  });
  return Promise.race([promise, timeout]).finally(() => clearTimeout(timer));
}
