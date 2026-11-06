/**
 * The check a preset or pack read makes for a change another tab saved but could not announce: compare
 * IndexedDB's `.generation` marker with the one the mounted tree holds (one small read, no lock).
 *
 * The check must never cost more than the change it looks for, so it converges in every case:
 *
 * - **A marker read that fails or does not settle** within `markerReadMs` is "unknown": the first one
 *   reloads the tree (which reconnects to IndexedDB, and logs if that fails too). Later reads skip the
 *   check for `markerRetryMs`, and while it keeps failing they no longer reload: a broken or hung
 *   IndexedDB must not turn every read into a reload, a warning and a wait.
 * - **A stored marker a reload did not bring into the tree.** `FS.syncfs(true)` replaces an entry only
 *   when its modification time differs from the stored one, so a marker with another content but the same
 *   time stays as it is however often the tree is reloaded. That stored marker is remembered and does
 *   not cause another reload until IndexedDB holds a different one.
 *
 * Neither hides a change for good: an announcement and every mutation reload the tree regardless.
 */
import type { PersistentStorage } from "./persistence";

export interface GenerationCheckTiming {
  /** Longest wait for IndexedDB's marker; the answer is "unknown" after that. */
  markerReadMs: number;
  /** After an unknown answer, reads skip the check for this long. */
  markerRetryMs: number;
}

export class StoredGenerationCheck {
  /** No marker read before this time (`now()`), after one that failed or did not settle. */
  private retryAt = 0;
  /** The last marker read failed: another failure does not reload again. */
  private failing = false;
  /** The stored marker the pending reload is for. */
  private reloadingFor: string | null = null;
  /** A stored marker a reload could not bring into the tree: it does not cause another reload. */
  private unconverged: string | null = null;

  constructor(private readonly storage: Pick<PersistentStorage, "readGeneration" | "readStoredGeneration">,
    private readonly timing: GenerationCheckTiming, private readonly now: () => number = Date.now) {}

  /** Whether the tree must be reloaded before this durable read; never throws. */
  async changed(): Promise<boolean> {
    if (!this.storage.readStoredGeneration || this.now() < this.retryAt) return false;
    let stored: string;
    try {
      stored = await bounded(this.storage.readStoredGeneration(), this.timing.markerReadMs);
    } catch {
      this.retryAt = this.now() + this.timing.markerRetryMs;
      if (this.failing) return false;
      this.failing = true;
      return true;
    }
    this.failing = false;
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
    timer = setTimeout(() => reject(new Error(`No answer within ${ms} ms.`)), ms);
  });
  return Promise.race([promise, timeout]).finally(() => clearTimeout(timer));
}
