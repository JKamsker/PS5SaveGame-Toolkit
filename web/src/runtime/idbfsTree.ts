/**
 * Change tracking for the IDBFS mount that does not depend on the clock.
 *
 * IDBFS decides what `FS.syncfs(false)` writes by comparing each entry's modification time with the
 * copy in IndexedDB, for equality. MEMFS stamps writes with `Date.now()`, which some browsers round
 * (Firefox `privacy.resistFingerprinting` and Tor Browser: 100 ms), so a file rewritten within one tick
 * of its stored copy looks unchanged and is silently not saved. Instead, every entry is stamped with a
 * sentinel time before a request runs: an entry whose time is no longer the sentinel (or whose node,
 * `ino`, changed) was written. Unchanged entries get their stored time back; changed ones get a time
 * strictly after it, so IDBFS always writes them, and a later `syncfs(true)` always replaces them.
 */

/** The Emscripten FS calls used here. `mtime` is a Date in .NET 10's Emscripten; newer ones use ms. */
export interface TreeFs {
  readdir(path: string): string[];
  stat(path: string): { mode: number; ino: number; mtime: Date | number };
  isDir(mode: number): boolean;
  utime(path: string, atime: number, mtime: number): void;
}

/** IDBFS internals that list what each side holds (path to modification time), for verification. */
export interface IdbfsSets {
  getLocalSet(mount: { mountpoint: string }, callback: (error: unknown, set?: EntrySet) => void): void;
  getRemoteSet(mount: { mountpoint: string }, callback: (error: unknown, set?: EntrySet) => void): void;
}

interface EntrySet { entries: Record<string, { timestamp: Date | number }> }

/** No write produces this time: MEMFS stamps with `Date.now()`, IDBFS restores stored (real) times. */
const UNTOUCHED = -1;

const millis = (time: Date | number) => time instanceof Date ? time.getTime() : Number(time);

function walk(fs: TreeFs, root: string, visit: (path: string, stat: ReturnType<TreeFs["stat"]>) => void) {
  for (const name of fs.readdir(root)) {
    if (name === "." || name === "..") continue;
    const path = `${root}/${name}`;
    const stat = fs.stat(path);
    visit(path, stat);
    if (fs.isDir(stat.mode)) walk(fs, path, visit);
  }
}

/**
 * Starts recording changes under `root`. The returned function stops recording (idempotent) and
 * reports whether anything was created, written, replaced or removed; changed entries then carry a
 * modification time after the one they had, whatever `now` returns.
 */
export function trackTreeChanges(fs: TreeFs, root: string, now: () => number = Date.now): () => boolean {
  const before = new Map<string, { ino: number; mtime: number }>();
  walk(fs, root, (path, stat) => before.set(path, { ino: stat.ino, mtime: millis(stat.mtime) }));
  for (const path of before.keys()) fs.utime(path, UNTOUCHED, UNTOUCHED);
  let result: boolean | null = null;
  return () => {
    if (result !== null) return result;
    let changed = false;
    const seen = new Set<string>();
    walk(fs, root, (path, stat) => {
      seen.add(path);
      const old = before.get(path);
      if (old && old.ino === stat.ino && millis(stat.mtime) === UNTOUCHED) {
        fs.utime(path, old.mtime, old.mtime);
        return;
      }
      changed = true;
      const time = Math.max(now(), old ? old.mtime + 1 : 0);
      fs.utime(path, time, time);
    });
    result = changed || [...before.keys()].some(path => !seen.has(path));
    return result;
  };
}

/** Writes `path` so that its modification time is after the previous one, whatever `now` returns. */
export function writeAfter(fs: TreeFs & { analyzePath(path: string): { exists: boolean }; writeFile(path: string, data: string): void },
  path: string, data: string, now: () => number = Date.now) {
  const previous = fs.analyzePath(path).exists ? millis(fs.stat(path).mtime) : 0;
  fs.writeFile(path, data);
  const time = Math.max(now(), previous + 1);
  fs.utime(path, time, time);
}

/** Resolves when IndexedDB holds exactly the in-memory entries (same paths and times); rejects otherwise. */
export async function verifyMirrored(idbfs: IdbfsSets, mountpoint: string): Promise<void> {
  const read = (get: IdbfsSets["getLocalSet"]) => new Promise<EntrySet["entries"]>((resolve, reject) =>
    get({ mountpoint }, (error, set) => error || !set ? reject(asError(error ?? "No entries.")) : resolve(set.entries)));
  const local = await read(idbfs.getLocalSet.bind(idbfs));
  const remote = await read(idbfs.getRemoteSet.bind(idbfs));
  const differing = [...new Set([...Object.keys(local), ...Object.keys(remote)])].filter(path => {
    const [mine, stored] = [local[path], remote[path]];
    return !mine || !stored || millis(mine.timestamp) !== millis(stored.timestamp);
  });
  if (differing.length) throw new Error(`IndexedDB did not store ${differing.length} changed entr${differing.length === 1 ? "y" : "ies"} `
    + `(${differing.slice(0, 3).join(", ")}${differing.length > 3 ? ", ..." : ""}).`);
}

/** A side of an IDBFS sync, as `getLocalSet`/`getRemoteSet` list it. */
export interface ReconcileSet extends EntrySet {
  type: "local" | "remote";
  db?: IDBDatabase;
}

/** The IDBFS internals `FS.syncfs` uses to apply a difference. */
export interface IdbfsReconcile {
  DB_STORE_NAME: string;
  reconcile(src: ReconcileSet, dst: ReconcileSet, callback: (error: unknown) => void): void;
  loadLocalEntry(path: string, callback: (error: unknown, entry?: unknown) => void): void;
}

/**
 * Makes `syncfs(false)` all or nothing. Emscripten's IDBFS (3.1.56, shipped with .NET 10) calls
 * `preventDefault()` on every failed put or delete, which keeps the transaction from aborting: it
 * reports the error at once but still commits the other writes, possibly including a new `.generation`.
 * The worker would then tell the user the change was not saved while IndexedDB holds part of it, which
 * its revert (and other tabs) load. This flush aborts the transaction on any failure and reports only
 * once the transaction has ended, so IndexedDB holds either the whole tree or what it held before.
 * Loading (`syncfs(true)`, which writes the in-memory tree) keeps IDBFS' own reconcile.
 */
export function makeFlushAtomic(idbfs: IdbfsReconcile): void {
  const reconcile = idbfs.reconcile.bind(idbfs);
  idbfs.reconcile = (src, dst, callback) => {
    if (dst.type !== "remote" || !dst.db) return reconcile(src, dst, callback);
    flushAtomically(idbfs, src, dst.db, dst.entries, callback);
  };
}

function flushAtomically(idbfs: IdbfsReconcile, src: ReconcileSet, db: IDBDatabase, stored: EntrySet["entries"],
  callback: (error: unknown) => void) {
  const create = Object.keys(src.entries).filter(path => {
    const entry = stored[path];
    return !entry || millis(entry.timestamp) !== millis(src.entries[path]!.timestamp);
  }).sort();
  // Files before their directories.
  const remove = Object.keys(stored).filter(path => !src.entries[path]).sort().reverse();
  if (!create.length && !remove.length) return callback(null);
  let transaction: IDBTransaction;
  try {
    transaction = db.transaction([idbfs.DB_STORE_NAME], "readwrite");
  } catch (error) {
    return callback(error);
  }
  let failure: unknown;
  let ended = false;
  const end = (error: unknown) => {
    if (ended) return;
    ended = true;
    callback(error);
  };
  const fail = (error: unknown) => {
    failure ??= error ?? new Error("IndexedDB refused a write.");
    try { transaction.abort(); } catch { /* already aborting */ }
  };
  // A failed request aborts the transaction by default (its error event is not cancelled here).
  transaction.oncomplete = () => end(failure ?? null);
  transaction.onabort = () => end(failure ?? transaction.error ?? new Error("The IndexedDB transaction was aborted."));
  const store = transaction.objectStore(idbfs.DB_STORE_NAME);
  const send = (request: () => IDBRequest) => {
    if (failure !== undefined) return;
    try {
      const pending = request();
      pending.onerror = () => { failure ??= pending.error ?? new Error("IndexedDB refused a write."); };
    } catch (error) {
      fail(error);
    }
  };
  for (const path of create) idbfs.loadLocalEntry(path, (error, entry) => error ? fail(error) : send(() => store.put(entry, path)));
  for (const path of remove) send(() => store.delete(path));
}

/** One cached IndexedDB connection of IDBFS (an `IDBDatabase`). */
export interface DatabaseConnection {
  close(): void;
  addEventListener(type: "close" | "versionchange", listener: () => void): void;
}

/** IDBFS' cache of open connections, by mount point. */
export interface IdbfsConnections { dbs: Record<string, DatabaseConnection | undefined> }

/**
 * IDBFS opens its IndexedDB connection once and caches it for good. When the browser closes it ("Clear
 * site data", the database deleted or upgraded elsewhere), every later `syncfs` would fail until a
 * reload, even after another tab recreated the database. The returned function runs `sync`; when that
 * fails on a cached connection, it drops the connection and tries once more on a new one. It also
 * watches each connection, so `onLost` hears when the browser closes it (or another tab needs it closed
 * to delete or upgrade the database): IndexedDB may then hold something else than this tab loaded.
 */
export function reconnectingSync(idbfs: IdbfsConnections, mountpoint: string, sync: (populate: boolean) => Promise<void>,
  onLost: () => void): (populate: boolean) => Promise<void> {
  const watched = new WeakSet<DatabaseConnection>();
  const drop = (db: DatabaseConnection) => {
    if (idbfs.dbs[mountpoint] === db) delete idbfs.dbs[mountpoint];
  };
  const watch = () => {
    const db = idbfs.dbs[mountpoint];
    if (!db || watched.has(db)) return;
    watched.add(db);
    db.addEventListener("close", () => { drop(db); onLost(); });
    db.addEventListener("versionchange", () => { db.close(); drop(db); onLost(); });
  };
  return async populate => {
    const cached = idbfs.dbs[mountpoint];
    try {
      await sync(populate);
    } catch (error) {
      // Opening failed (storage blocked, a newer database version): a new connection would fail too.
      if (!cached) throw error;
      drop(cached);
      try { cached.close(); } catch { /* already closed */ }
      await sync(populate);
    } finally {
      watch();
    }
  };
}

/** The IDBFS internals that open (and cache) a mount's database. */
export interface IdbfsDatabase {
  DB_STORE_NAME: string;
  getDB(name: string, callback: (error: unknown, db?: IDBDatabase) => void): void;
}

/**
 * The text IndexedDB holds for the file at `path` of the mount (IDBFS stores each entry under its full
 * path, a file's bytes as `contents`), "" when it holds none: one `get`, without loading the tree.
 */
export function readStoredText(idbfs: IdbfsDatabase, mountpoint: string, path: string): Promise<string> {
  return new Promise((resolve, reject) => idbfs.getDB(mountpoint, (error, db) => {
    if (error || !db) return reject(asError(error ?? "No IndexedDB connection."));
    try {
      const request = db.transaction([idbfs.DB_STORE_NAME], "readonly").objectStore(idbfs.DB_STORE_NAME).get(path);
      request.onsuccess = () => {
        const contents = (request.result as { contents?: unknown } | undefined)?.contents;
        resolve(contents instanceof Uint8Array ? new TextDecoder().decode(contents) : "");
      };
      request.onerror = () => reject(asError(request.error ?? "IndexedDB could not read the entry."));
    } catch (failure) {
      reject(asError(failure));
    }
  }));
}

export function asError(error: unknown): Error {
  return error instanceof Error ? error : new Error(String(error));
}
