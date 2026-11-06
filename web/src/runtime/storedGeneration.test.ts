// @vitest-environment node
import { describe, expect, it, vi } from "vitest";
import { bootTab, frame, library, read, Site } from "../test/persistenceSite";
import { PersistenceCoordinator, READ_RETRY_MS, type PersistentStorage, type TabCoordination } from "./persistence";

describe("a marker read that keeps failing", () => {
  it("reloads once, not before every preset read, and still sees announced and later-written changes", async () => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site)];
    site.failMarkerRead = new DOMException("The database connection is closing.", "InvalidStateError");
    const load = vi.spyOn(b.storage, "load");
    const marker = vi.spyOn(b.storage, "readStoredGeneration");
    for (let i = 0; i < 5; i++) expect(read(await b.list()).status).toBe(200);
    // The first unknown answer reloads (which reconnects); the next reads skip the check for a while.
    expect(load).toHaveBeenCalledOnce();
    expect(marker).toHaveBeenCalledOnce();
    // An announced change is still seen by the next read.
    await a.create("announced");
    expect(read(await b.list()).body).toEqual(["announced"]);
    // An unannounced one is still seen by the next mutation, which reloads first and loses nothing.
    site.failAnnounce = new DOMException("The channel is closed.", "InvalidStateError");
    await a.create("unannounced");
    expect(read(await b.create("from B")).status).toBe(200);
    expect(library(site)).toEqual(["announced", "unannounced", "from B"]);
  });

  it("reloads at most once per retry period, so an unannounced save is still seen", async () => {
    const site = new Site();
    let now = 0;
    const [a, b] = [await bootTab(site), await bootTab(site, true, { now: () => now })];
    site.failMarkerRead = new DOMException("The database connection is closing.", "InvalidStateError");
    const load = vi.spyOn(b.storage, "load");
    const marker = vi.spyOn(b.storage, "readStoredGeneration");
    await b.list();
    site.failAnnounce = new DOMException("The channel is closed.", "InvalidStateError");
    await a.create("unannounced");
    now = READ_RETRY_MS - 1;
    expect(read(await b.list()).body).toEqual([]);
    expect([marker.mock.calls.length, load.mock.calls.length]).toEqual([1, 1]);
    // After the period the marker is unknown again, and that reload brings the other tab's save.
    now = READ_RETRY_MS;
    expect(read(await b.list()).body).toEqual(["unannounced"]);
    expect([marker.mock.calls.length, load.mock.calls.length]).toEqual([2, 2]);
    // Once the marker can be read again, a read reloads only when it differs.
    site.failMarkerRead = null;
    now = 2 * READ_RETRY_MS;
    await b.list();
    expect(load).toHaveBeenCalledTimes(2);
    await a.create("later");
    expect(read(await b.list()).body).toEqual(["unannounced", "later"]);
    expect(load).toHaveBeenCalledTimes(3);
  });

});

describe("the retry period after a failed marker read", () => {
  it("sees another tab's unannounced save on every read once the period has passed", async () => {
    const site = new Site();
    // retryMs: 0 stands for "the retry period has passed before every read".
    const [a, b] = [await bootTab(site), await bootTab(site, true, { retryMs: 0 })];
    site.failMarkerRead = new DOMException("The database connection is closing.", "InvalidStateError");
    await b.list();
    site.failAnnounce = new DOMException("The channel is closed.", "InvalidStateError");
    await a.create("unannounced");
    expect(read(await b.list()).body).toEqual(["unannounced"]);
  });

  it("does not stretch the retry period when the wall clock steps back", async () => {
    vi.useFakeTimers({ toFake: ["Date"] });
    try {
      vi.setSystemTime(new Date("2026-11-06T12:00:00Z"));
      const site = new Site();
      const [a, b] = [await bootTab(site), await bootTab(site, true, { retryMs: 50 })];
      site.failMarkerRead = new DOMException("The database connection is closing.", "InvalidStateError");
      await b.list();
      site.failMarkerRead = null;
      // The clock is corrected back by one hour; then more than the retry period passes.
      vi.setSystemTime(new Date("2026-11-06T11:00:00Z"));
      await new Promise(resolve => setTimeout(resolve, 100));
      site.failAnnounce = new DOMException("The channel is closed.", "InvalidStateError");
      await a.create("unannounced");
      expect(read(await b.list()).body).toEqual(["unannounced"]);
    } finally {
      vi.useRealTimers();
    }
  });
});

describe("a marker read and a reload that both keep failing", () => {
  it("reload at most once per retry period, answer from the cached view and warn once", async () => {
    const site = new Site();
    let now = 0;
    const b = await bootTab(site, true, { now: () => now });
    site.failMarkerRead = new DOMException("The database connection is closing.", "InvalidStateError");
    site.failLoad = new DOMException("Storage is gone.", "UnknownError");
    const load = vi.spyOn(b.storage, "load");
    for (let i = 0; i < 5; i++) expect(read(await b.list()).status).toBe(200);
    expect(load).toHaveBeenCalledOnce();
    now = READ_RETRY_MS;
    for (let i = 0; i < 5; i++) expect(read(await b.list()).status).toBe(200);
    expect(load).toHaveBeenCalledTimes(2);
    expect(b.log.warn).toHaveBeenCalledOnce();
  });

  it("try again as soon as another tab announces a save", async () => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site)];
    site.failMarkerRead = new DOMException("The database connection is closing.", "InvalidStateError");
    site.failLoad = new DOMException("Storage is gone.", "UnknownError");
    await b.list();
    site.failLoad = null;
    await a.create("announced");
    expect(read(await b.list()).body).toEqual(["announced"]);
  });
});

describe("a marker read that never settles", () => {
  it("answers the read after markerReadMs, as for an unknown marker, and does not wait again", async () => {
    const site = new Site();
    const b = await bootTab(site, true, { markerReadMs: 20 });
    const marker = vi.fn(() => new Promise<string>(() => undefined));
    b.storage.readStoredGeneration = marker;
    const answered = (request: Promise<unknown>) =>
      Promise.race([request.then(() => "answered"), new Promise(resolve => setTimeout(() => resolve("pending"), 500))]);
    expect(await answered(b.list())).toBe("answered");
    expect(await answered(b.list())).toBe("answered");
    expect(marker).toHaveBeenCalledOnce();
  });
});

describe("an IndexedDB that never answers", () => {
  const answered = (request: Promise<unknown>, ms = 500) =>
    Promise.race([request.then(() => "answered"), new Promise(resolve => setTimeout(() => resolve("pending"), ms))]);

  it("answers a read after a bounded wait for the reload, and later reads neither wait nor reload again", async () => {
    const site = new Site();
    const b = await bootTab(site, true, { markerReadMs: 20, readLockWaitMs: 20 });
    b.storage.readStoredGeneration = vi.fn(() => new Promise<string>(() => undefined));
    const load = vi.spyOn(b.storage, "load").mockImplementation(() => new Promise<void>(() => undefined));
    expect(await answered(b.list())).toBe("answered");
    // A reload is still running: no other read starts one or waits for anything, announced or not.
    b.coordinator.invalidate();
    expect(await answered(b.list(), 15)).toBe("answered");
    expect(load).toHaveBeenCalledOnce();
    expect(b.storage.readStoredGeneration).toHaveBeenCalledOnce();
    expect(b.log.warn).toHaveBeenCalledOnce();
  });

  it("makes a mutation wait for a reload a read gave up on, so its late completion cannot undo the change", async () => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site, true, { readLockWaitMs: 20 })];
    await a.create("first");
    // The read's reload captures IndexedDB as it is now, and applies that only much later.
    let finish!: () => void;
    const load = vi.spyOn(b.storage, "load").mockImplementationOnce(() => {
      const captured = site.idb;
      return new Promise<void>(resolve => { finish = () => { b.local = new Map(captured); resolve(); }; });
    });
    expect(read(await b.list()).body).toEqual([]);
    // The read released the shared lock: another tab can save meanwhile.
    await a.create("second");
    expect(read(await b.list()).body).toEqual([]);
    expect(load).toHaveBeenCalledOnce();
    let created = false;
    const create = b.create("from B").then(() => { created = true; });
    await new Promise(resolve => setTimeout(resolve, 50));
    expect(created).toBe(false);
    finish();
    await create;
    expect(library(site)).toEqual(["first", "second", "from B"]);
    expect(read(await b.list()).body).toEqual(["first", "second", "from B"]);
  });
});

/**
 * IDBFS' populate (`syncfs(true)`) replaces a local entry only when its timestamp differs from the stored
 * one, so a stored marker with other contents but the same timestamp is never loaded.
 */
describe("a stored marker that differs while its IDBFS timestamp matches", () => {
  type Entry = { content: string; mtime: number };
  type Tree = Map<string, Entry>;
  const copy = (tree: Tree): Tree => new Map([...tree].map(([path, entry]) => [path, { ...entry }]));

  function model(idb: Tree, local: Tree) {
    const state = { idb, local, view: local.get("presets")!.content };
    const storage: PersistentStorage = {
      load: async () => {
        const next = copy(state.local);
        for (const [path, entry] of state.idb) if (state.local.get(path)?.mtime !== entry.mtime) next.set(path, { ...entry });
        for (const path of state.local.keys()) if (!state.idb.has(path)) next.delete(path);
        state.local = next;
      },
      save: async () => {}, trackChanges: () => () => false, verifySaved: async () => {},
      readGeneration: () => state.local.get(".generation")?.content ?? "",
      readStoredGeneration: async () => state.idb.get(".generation")?.content ?? "",
      writeGeneration: () => {},
      reloadStores: () => { state.view = state.local.get("presets")!.content; return ""; },
    };
    return { state, storage };
  }

  it("reloads once for it, and again as soon as IndexedDB holds another marker", async () => {
    const stored: Tree = new Map([[".generation", { content: "gen-B", mtime: 100 }], ["presets", { content: "B", mtime: 101 }]]);
    const loaded: Tree = new Map([[".generation", { content: "gen-A", mtime: 100 }], ["presets", { content: "A", mtime: 50 }]]);
    // Boot with the tree as loaded before the other tab's change.
    const { state, storage } = model(copy(loaded), loaded);
    const tabs: TabCoordination = { lock: (_mode, task) => task(), announce: () => {}, listen: () => {} };
    const coordinator = new PersistenceCoordinator(storage, tabs, { error: vi.fn(), warn: vi.fn() });
    await coordinator.initialLoad();
    state.idb = stored;
    const load = vi.spyOn(storage, "load");
    const list = async () => read(await coordinator.run("GET", "/api/presets", async () => frame(200, state.view))).body;
    for (let i = 0; i < 4; i++) await list();
    expect(load).toHaveBeenCalledOnce();
    expect(storage.readGeneration()).toBe("gen-A");
    // Another save gives the marker a new timestamp: the next read loads it.
    state.idb = new Map([[".generation", { content: "gen-C", mtime: 200 }], ["presets", { content: "C", mtime: 201 }]]);
    expect(await list()).toBe("C");
    expect(load).toHaveBeenCalledTimes(2);
    expect(await list()).toBe("C");
    expect(load).toHaveBeenCalledTimes(2);
  });
});
