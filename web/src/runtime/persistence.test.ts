// @vitest-environment node
import { describe, expect, it, vi } from "vitest";
import { decodeFrame, encodeFrame } from "./framing";
import { PersistenceCoordinator, storageFailure, writesDurableData, type LockMode, type PersistentStorage, type TabCoordination } from "./persistence";

/**
 * A model of the browser: one IndexedDB shared by tabs, each tab with its own in-memory copy of the
 * mount (IDBFS mirrors whole trees, exactly like FS.syncfs) and a .NET preset store that caches the
 * library and writes its whole cached view back (like PresetStore.Persist).
 */
type Tree = Map<string, { content: string; mtime: number }>;
let clock = 1;
const copy = (tree: Tree): Tree => new Map([...tree].map(([path, entry]) => [path, { ...entry }]));

class Site {
  idb: Tree = new Map();
  failSave: Error | null = null;
  failLoad: Error | null = null;
  lockUnavailable = false;
  readonly listeners = new Set<{ tab: Tab; onChange: () => void }>();
  private tail: Promise<unknown> = Promise.resolve();

  tabs(tab: Tab, locks = true): TabCoordination {
    return {
      lock: locks ? <T>(_mode: LockMode, task: () => Promise<T>) => {
        if (this.lockUnavailable) return Promise.reject(new DOMException("Timed out", "TimeoutError"));
        const run = this.tail.then(task);
        this.tail = run.catch(() => undefined);
        return run;
      } : null,
      announce: () => { for (const listener of this.listeners) if (listener.tab !== tab) listener.onChange(); },
      listen: onChange => { this.listeners.add({ tab, onChange }); },
    };
  }
}

class Tab {
  local: Tree;
  presets: string[];
  readonly log = { error: vi.fn(), warn: vi.fn() };
  readonly coordinator: PersistenceCoordinator;
  readonly storage: PersistentStorage;

  constructor(readonly site: Site, locks = true) {
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
      fingerprint: () => [...this.local].map(([path, { content, mtime }]) => `${path} ${content.length} ${mtime}`).sort().join("\n"),
      readGeneration: () => this.local.get(".generation")?.content ?? "",
      writeGeneration: generation => this.write(".generation", generation),
      reloadStores: () => { this.presets = this.readLibrary(); return ""; },
    };
    this.storage = storage;
    this.coordinator = new PersistenceCoordinator(storage, site.tabs(this, locks), this.log);
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

  private write(path: string, content: string) {
    this.local.set(path, { content, mtime: clock++ });
  }

  private readLibrary(): string[] {
    const file = this.local.get("presets/presets.json");
    return file ? JSON.parse(file.content) as string[] : [];
  }
}

const frame = (status: number, body: unknown) => encodeFrame(status, [["Content-Type", "application/json"]], new TextEncoder().encode(JSON.stringify(body)));
const read = (bytes: Uint8Array) => {
  const { status, bodyOffset } = decodeFrame(bytes);
  return { status, body: JSON.parse(new TextDecoder().decode(bytes.subarray(bodyOffset))) as Record<string, unknown> };
};
const library = (site: Site) => JSON.parse(site.idb.get("presets/presets.json")?.content ?? "[]") as string[];

async function bootTab(site: Site, locks = true) {
  const tab = new Tab(site, locks);
  await tab.boot();
  return tab;
}

describe("two tabs sharing IndexedDB", () => {
  it.each([true, false])("keeps both tabs' saves (Web Locks: %s)", async locks => {
    const site = new Site();
    const a = await bootTab(site, locks);
    const b = await bootTab(site, locks);
    expect(read(await a.create("from A")).status).toBe(200);
    expect(read(await b.create("from B")).status).toBe(200);

    expect(library(site)).toEqual(["from A", "from B"]);
    expect(b.presets).toEqual(["from A", "from B"]);
    expect((await bootTab(site)).presets).toEqual(["from A", "from B"]);
  });

  it("serializes concurrent saves from both tabs", async () => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site)];
    await Promise.all([a.create("one"), b.create("two"), a.create("three")]);
    expect([...library(site)].sort()).toEqual(["one", "three", "two"]);
  });

  it("refreshes reads after another tab announces a change, and only then", async () => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site)];
    const load = vi.spyOn(b.storage, "load");
    expect(read(await b.list()).body).toEqual([]);
    expect(load).not.toHaveBeenCalled();

    await a.create("from A");
    expect(read(await b.list()).body).toEqual(["from A"]);
    expect(load).toHaveBeenCalledOnce();
    await b.list();
    expect(load).toHaveBeenCalledOnce();
  });

  it("does not flush or announce a mutation that left the tree unchanged", async () => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site)];
    const save = vi.spyOn(a.storage, "save");
    const reload = vi.spyOn(b.storage, "reloadStores");
    await a.coordinator.run("POST", "/api/presets/transfer/export", async () => frame(200, {}));
    expect(save).not.toHaveBeenCalled();
    await b.list();
    expect(reload).not.toHaveBeenCalled();
  });
});

describe("failures are never reported as saved", () => {
  it("answers a quota failure with 507 and shows what storage really holds", async () => {
    const site = new Site();
    const a = await bootTab(site);
    await a.create("kept");
    site.failSave = new DOMException("The quota has been exceeded.", "QuotaExceededError");

    const response = read(await a.create("lost"));
    expect(response.status).toBe(507);
    expect(response.body.detail).toMatch(/no storage space left.*not saved/i);
    expect(response.body.detail).toBe(response.body.error);
    expect(library(site)).toEqual(["kept"]);
    expect(a.presets).toEqual(["kept"]);
    expect(a.log.error).toHaveBeenCalled();
  });

  it("keeps the original error when a failed request could not be flushed either", async () => {
    const site = new Site();
    const a = await bootTab(site);
    site.failSave = new Error("disk gone");
    const response = await a.coordinator.run("DELETE", "/api/presets/x", async () => {
      a.local.set("presets/stray.tmp", { content: "x", mtime: clock++ });
      return frame(404, { error: "Preset not found." });
    });
    expect(read(response)).toMatchObject({ status: 404, body: { error: "Preset not found." } });
    expect(a.local.has("presets/stray.tmp")).toBe(false);
  });

  it("refuses to run a mutation when storage cannot be read or another tab holds the lock", async () => {
    const site = new Site();
    const a = await bootTab(site);
    const dispatch = vi.fn(async () => frame(200, {}));
    site.failLoad = new Error("IndexedDB closed");
    expect(read(await a.coordinator.run("POST", "/api/presets", dispatch))).toMatchObject({ status: 503, body: { detail: expect.stringMatching(/could not be read/) } });
    site.failLoad = null;
    site.lockUnavailable = true;
    expect(read(await a.coordinator.run("POST", "/api/presets", dispatch))).toMatchObject({ status: 503, body: { detail: expect.stringMatching(/Another tab/) } });
    expect(dispatch).not.toHaveBeenCalled();
  });

  it("reverts a durable change left behind by a request that threw", async () => {
    const site = new Site();
    const a = await bootTab(site);
    await expect(a.coordinator.run("POST", "/api/reference-packs", async () => {
      a.local.set("reference-packs/pack-x.zip", { content: "zip", mtime: clock++ });
      throw new Error("interop failed");
    })).rejects.toThrow("interop failed");
    expect(a.local.has("reference-packs/pack-x.zip")).toBe(false);
  });
});

it("describes other storage failures and a revert that failed", () => {
  const { status, body } = read(storageFailure(new TypeError("transaction aborted"), false));
  expect(status).toBe(503);
  expect(body.detail).toMatch(/TypeError: transaction aborted.*not saved.*until the page is reloaded/);
});

it("routes everything that may write presets or packs through the lock, but not workspace work", () => {
  for (const [method, url] of [["POST", "/api/presets"], ["PATCH", "/api/presets/p1"], ["DELETE", "/api/presets/p1"],
    ["POST", "/api/presets/transfer/commit"], ["POST", "/api/reference-packs"], ["DELETE", "/api/reference-packs/x"],
    ["POST", "/api/workspaces/w/documents/d/presets/capture"], ["POST", "/api/anything-new?x=1"]] as const)
    expect(writesDurableData(method, url), `${method} ${url}`).toBe(true);
  for (const [method, url] of [["GET", "/api/presets"], ["HEAD", "/api/reference-packs"], ["POST", "/api/workspaces"],
    ["POST", "/api/workspaces/w/documents/d/edits"], ["POST", "/api/workspaces/w/export?presets=1"], ["DELETE", "/api/workspaces/w"]] as const)
    expect(writesDurableData(method, url), `${method} ${url}`).toBe(false);
});
