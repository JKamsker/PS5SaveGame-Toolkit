// @vitest-environment node
import { describe, expect, it } from "vitest";
import { makeFlushAtomic, type IdbfsReconcile, type ReconcileSet } from "./idbfsTree";

/**
 * A model of one IndexedDB object store with the transaction rules that matter here: writes become
 * visible only when the transaction commits (after its last request succeeded), a failed request aborts
 * the transaction unless its error event is cancelled (`preventDefault`), and an abort discards every
 * write of the transaction.
 */
class FakeDb {
  data = new Map<string, unknown>();
  /** Paths whose put fails with a request error ("refuse") or throws synchronously ("throw"). */
  failures = new Map<string, "refuse" | "throw">();

  transaction() {
    const staged = new Map<string, unknown>();
    let pending = 0;
    let state: "active" | "committed" | "aborted" = "active";
    const transaction = {
      error: null as unknown,
      oncomplete: null as (() => void) | null,
      onabort: null as (() => void) | null,
      onerror: null as ((event: FakeEvent) => void) | null,
      abort: () => {
        if (state !== "active") throw new Error("InvalidStateError");
        state = "aborted";
        queueMicrotask(() => transaction.onabort?.());
      },
      objectStore: () => ({ put: (value: unknown, key: string) => request(key, value), delete: (key: string) => request(key, undefined) }),
    };
    const settle = () => {
      if (state !== "active" || pending > 0) return;
      state = "committed";
      this.commit(staged);
      transaction.oncomplete?.();
    };
    const request = (key: string, value: unknown) => {
      if (state !== "active") throw new Error("TransactionInactiveError");
      if (this.failures.get(key) === "throw") throw new Error(`DataCloneError: ${key}`);
      pending++;
      const handle = { error: null as unknown, onerror: null as ((event: FakeEvent) => void) | null };
      setTimeout(() => {
        pending--;
        if (state !== "active") return;
        if (this.failures.get(key) === "refuse") {
          handle.error = new Error(`UnknownError: ${key}`);
          const event = new FakeEvent(handle);
          handle.onerror?.(event);
          transaction.onerror?.(event);
          if (!event.defaultPrevented && state === "active") {
            transaction.error = handle.error;
            transaction.abort();
            return;
          }
        } else staged.set(key, value);
        setTimeout(settle);
      });
      return handle;
    };
    return transaction;
  }

  private commit(staged: Map<string, unknown>) {
    for (const [key, value] of staged) {
      if (value === undefined) this.data.delete(key);
      else this.data.set(key, value);
    }
  }
}

class FakeEvent {
  defaultPrevented = false;
  constructor(readonly target: { error: unknown }) {}
  preventDefault() { this.defaultPrevented = true; }
}

/** What IDBFS' syncfs(false) passes: the in-memory tree as the source, IndexedDB as the destination. */
function flush(idbfs: IdbfsReconcile, db: FakeDb, local: Record<string, number>) {
  const remote = Object.fromEntries([...db.data.keys()].map(path => [path, { timestamp: new Date((db.data.get(path) as { at: number }).at) }]));
  const src: ReconcileSet = { type: "local", entries: Object.fromEntries(Object.entries(local).map(([path, at]) => [path, { timestamp: new Date(at) }])) };
  const dst: ReconcileSet = { type: "remote", db: db as unknown as IDBDatabase, entries: remote };
  return new Promise<void>((resolve, reject) => idbfs.reconcile(src, dst, error => error ? reject(error) : resolve()));
}

/**
 * The error handling of Emscripten 3.1.56's IDBFS.reconcile for a flush (as shipped in dotnet.native.js):
 * every request error is reported at once and cancelled, so the transaction goes on and commits.
 */
function emscriptenIdbfs(local: Record<string, number>): IdbfsReconcile {
  return {
    DB_STORE_NAME: "FILE_DATA",
    loadLocalEntry: (path, callback) => local[path] === undefined ? callback(new Error(`ENOENT ${path}`)) : callback(null, { at: local[path] }),
    reconcile: (src, dst, callback) => {
      const transaction = (dst.db as unknown as FakeDb).transaction();
      let errored = false;
      const done = (error?: unknown) => { if (error && !errored) { errored = true; callback(error); } };
      transaction.onerror = event => { done(event.target.error); event.preventDefault(); };
      transaction.oncomplete = () => { if (!errored) callback(null); };
      const store = transaction.objectStore();
      for (const path of Object.keys(src.entries).sort()) {
        try {
          const request = store.put({ at: local[path] }, path);
          request.onerror = event => { done(event.target.error); event.preventDefault(); };
        } catch (error) { done(error); }
      }
    },
  };
}

const stored = (db: FakeDb) => Object.fromEntries([...db.data].map(([path, value]) => [path, (value as { at: number }).at]));

const before = { "/p/.generation": 1, "/p/presets/presets.json": 1 };
const change = { "/p/.generation": 2, "/p/presets/presets.json": 2, "/p/reference-packs/new.zip": 2 };

function site() {
  const db = new FakeDb();
  for (const [path, at] of Object.entries(before)) db.data.set(path, { at });
  return db;
}

describe("a flush to IndexedDB that fails part way", () => {
  it("as shipped, commits the rest of a change (with its new generation) after a write failed", async () => {
    const db = site();
    db.failures.set("/p/reference-packs/new.zip", "refuse");
    await expect(flush(emscriptenIdbfs(change), db, change)).rejects.toThrow("UnknownError");
    await new Promise(resolve => setTimeout(resolve, 20));
    expect(stored(db)).toEqual({ "/p/.generation": 2, "/p/presets/presets.json": 2 });
  });

  it.each(["refuse", "throw"] as const)("keeps what IndexedDB held when a write fails (%s)", async failure => {
    const db = site();
    const idbfs = emscriptenIdbfs(change);
    makeFlushAtomic(idbfs);
    db.failures.set("/p/reference-packs/new.zip", failure);
    await expect(flush(idbfs, db, change)).rejects.toThrow(failure === "refuse" ? "UnknownError" : "DataCloneError");
    // Reported only once the transaction ended: a reload right after it sees the old tree.
    expect(stored(db)).toEqual(before);
  });

  it("keeps what IndexedDB held when an in-memory entry cannot be read", async () => {
    const db = site();
    const idbfs = emscriptenIdbfs({ "/p/.generation": 2 });
    makeFlushAtomic(idbfs);
    await expect(flush(idbfs, db, change)).rejects.toThrow("ENOENT");
    expect(stored(db)).toEqual(before);
  });

});

describe("a flush to IndexedDB that succeeds", () => {
  it("writes changed entries and removes deleted ones in one commit", async () => {
    const db = site();
    db.data.set("/p/reference-packs/old.zip", { at: 1 });
    const idbfs = emscriptenIdbfs(change);
    makeFlushAtomic(idbfs);
    await flush(idbfs, db, change);
    expect(stored(db)).toEqual(change);
  });

  it("does not open a transaction when nothing changed", async () => {
    const db = site();
    const idbfs = emscriptenIdbfs(before);
    makeFlushAtomic(idbfs);
    db.transaction = () => { throw new Error("no transaction expected"); };
    await flush(idbfs, db, before);
  });

  it("leaves loading (IndexedDB to memory) to IDBFS", async () => {
    const calls: string[] = [];
    const idbfs: IdbfsReconcile = { DB_STORE_NAME: "FILE_DATA", loadLocalEntry: () => undefined,
      reconcile: (_src, dst, callback) => { calls.push(dst.type); callback(null); } };
    makeFlushAtomic(idbfs);
    await new Promise<void>(resolve => idbfs.reconcile({ type: "remote", entries: {} }, { type: "local", entries: {} }, () => resolve()));
    expect(calls).toEqual(["local"]);
  });
});
