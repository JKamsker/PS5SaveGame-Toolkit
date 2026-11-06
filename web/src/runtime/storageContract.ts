/** What the persistence coordinator (runtime/persistence.ts) needs from the worker and the browser. */
import type { PersistentData } from "./persistentChanges";

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
