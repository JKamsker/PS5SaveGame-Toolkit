import { vi } from "vitest";
import { decodeFrame, encodeFrame } from "../runtime/framing";
import { LOCK_WAIT_MS, PersistenceCoordinator, type CoordinatorTiming, type LockMode, type PersistentStorage,
  type TabCoordination } from "../runtime/persistence";

/**
 * A model of the browser for runtime/persistence tests: one IndexedDB shared by tabs, each tab with its
 * own in-memory copy of the mount (IDBFS mirrors whole trees, exactly like FS.syncfs) and a .NET preset
 * store that caches the library and writes its whole cached view back (like PresetStore.Persist).
 */
type Tree = Map<string, { content: string; mtime: number }>;
let clock = 1;
/** A modification time after every earlier one. */
export const tick = () => clock++;
const copy = (tree: Tree): Tree => new Map([...tree].map(([path, entry]) => [path, { ...entry }]));
const timedOut = () => new DOMException("Timed out", "TimeoutError");

export class Site {
  idb: Tree = new Map();
  failSave: Error | null = null;
  failLoad: Error | null = null;
  failVerify: Error | null = null;
  lockUnavailable = false;
  readonly listeners = new Set<{ tab: Tab; onChange: () => void }>();
  private tail: Promise<unknown> = Promise.resolve();
  private held = 0;

  /** One lock (shared and exclusive alike) granted in request order; a wait gives up after `waitMs`. */
  tabs(tab: Tab, locks = true): TabCoordination {
    return {
      lock: locks ? <T>(_mode: LockMode, task: () => Promise<T>, waitMs = LOCK_WAIT_MS) => {
        if (this.lockUnavailable || (waitMs <= 0 && this.held > 0)) return Promise.reject(timedOut());
        this.held++;
        let gaveUp = false;
        let timer: ReturnType<typeof setTimeout> | undefined;
        const run = this.tail.then(() => {
          clearTimeout(timer);
          return gaveUp ? undefined : task();
        }).finally(() => { this.held--; });
        this.tail = run.catch(() => undefined);
        const timeout = new Promise<never>((_, reject) => {
          timer = setTimeout(() => { gaveUp = true; reject(timedOut()); }, waitMs);
        });
        return Promise.race([run, timeout]) as Promise<T>;
      } : null,
      announce: () => { for (const listener of this.listeners) if (listener.tab !== tab) listener.onChange(); },
      listen: onChange => { this.listeners.add({ tab, onChange }); },
    };
  }

  /** Holds the lock (another tab frozen mid-request) until the returned function is called. */
  hold(tab: Tab): () => Promise<void> {
    let release!: () => void;
    const held = this.tabs(tab).lock!("exclusive", () => new Promise<void>(resolve => { release = resolve; }));
    // Also lets the waits that gave up meanwhile leave the queue.
    return async () => { release(); await held; await this.tail; };
  }
}

export class Tab {
  local: Tree;
  presets: string[];
  /** What PresetStore.Refresh reports, e.g. a record from a newer version of the site. */
  reloadError = "";
  readonly log = { error: vi.fn(), warn: vi.fn() };
  readonly coordinator: PersistenceCoordinator;
  readonly storage: PersistentStorage;

  constructor(readonly site: Site, locks = true, timing: Partial<CoordinatorTiming> = {}) {
    this.local = copy(site.idb);
    this.presets = this.readLibrary();
    const storage: PersistentStorage = {
      load: async () => {
        if (site.failLoad) throw site.failLoad;
        this.local = copy(site.idb);
      },
      save: async () => {
        if (site.failSave) throw site.failSave;
        site.idb = copy(this.local);
      },
      trackChanges: () => {
        const before = this.fingerprint();
        return () => this.fingerprint() !== before;
      },
      verifySaved: async () => { if (site.failVerify) throw site.failVerify; },
      readGeneration: () => this.local.get(".generation")?.content ?? "",
      writeGeneration: generation => this.write(".generation", generation),
      reloadStores: () => {
        if (this.reloadError) return this.reloadError;
        this.presets = this.readLibrary();
        return "";
      },
    };
    this.storage = storage;
    this.coordinator = new PersistenceCoordinator(storage, site.tabs(this, locks), this.log, timing);
  }

  async boot() {
    await this.coordinator.initialLoad();
    this.presets = this.readLibrary();
  }

  /** POST /api/presets: adds to the cached library and writes the whole cached view. */
  create(name: string) {
    return this.coordinator.run("POST", "/api/presets", async () => {
      this.presets = [...this.presets, name];
      this.write("presets/presets.json", JSON.stringify(this.presets));
      return frame(200, { name });
    });
  }

  list() {
    return this.coordinator.run("GET", "/api/presets", async () => frame(200, this.presets));
  }

  private fingerprint() {
    return [...this.local].map(([path, { content, mtime }]) => `${path} ${content.length} ${mtime}`).sort().join("\n");
  }

  private write(path: string, content: string) {
    this.local.set(path, { content, mtime: tick() });
  }

  private readLibrary(): string[] {
    const file = this.local.get("presets/presets.json");
    return file ? JSON.parse(file.content) as string[] : [];
  }
}

export const frame = (status: number, body: unknown) =>
  encodeFrame(status, [["Content-Type", "application/json"]], new TextEncoder().encode(JSON.stringify(body)));
export const read = (bytes: Uint8Array) => {
  const { status, bodyOffset } = decodeFrame(bytes);
  return { status, body: JSON.parse(new TextDecoder().decode(bytes.subarray(bodyOffset))) as Record<string, unknown> };
};
export const library = (site: Site) => JSON.parse(site.idb.get("presets/presets.json")?.content ?? "[]") as string[];

export async function bootTab(site: Site, locks = true, timing: Partial<CoordinatorTiming> = {}) {
  const tab = new Tab(site, locks, timing);
  await tab.boot();
  return tab;
}
