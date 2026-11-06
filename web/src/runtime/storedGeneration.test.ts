// @vitest-environment node
import { describe, expect, it, vi } from "vitest";
import { bootTab, frame, library, read, Site } from "../test/persistenceSite";
import { PersistenceCoordinator, type PersistentStorage, type TabCoordination } from "./persistence";

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

  it("while storage is gone too, answers every read from the cached view and warns once", async () => {
    const site = new Site();
    const b = await bootTab(site);
    site.failMarkerRead = new DOMException("The database connection is closing.", "InvalidStateError");
    site.failLoad = new DOMException("Storage is gone.", "UnknownError");
    for (let i = 0; i < 5; i++) expect(read(await b.list()).status).toBe(200);
    expect(b.log.warn).toHaveBeenCalledOnce();
  });

  it("checks again after the retry period, reloading only once the marker can be read and differs", async () => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site, true, { markerRetryMs: 0 })];
    site.failMarkerRead = new DOMException("The database connection is closing.", "InvalidStateError");
    const load = vi.spyOn(b.storage, "load");
    const marker = vi.spyOn(b.storage, "readStoredGeneration");
    for (let i = 0; i < 3; i++) await b.list();
    expect(marker).toHaveBeenCalledTimes(3);
    expect(load).toHaveBeenCalledOnce();
    site.failMarkerRead = null;
    site.failAnnounce = new DOMException("The channel is closed.", "InvalidStateError");
    await a.create("unannounced");
    expect(read(await b.list()).body).toEqual(["unannounced"]);
    expect(load).toHaveBeenCalledTimes(2);
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
