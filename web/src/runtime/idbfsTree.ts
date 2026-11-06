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

export function asError(error: unknown): Error {
  return error instanceof Error ? error : new Error(String(error));
}
