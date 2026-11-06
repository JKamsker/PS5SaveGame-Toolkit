// @vitest-environment node
import { describe, expect, it, vi } from "vitest";
import { bootTab, frame, library, read, Site } from "../test/persistenceSite";
import { readsDurableData, RuntimeHungError } from "./persistence";

const quick = { readLockWaitMs: 40, mutationWatchdogMs: 60 };
const elapsed = async (work: () => Promise<unknown>) => {
  const started = performance.now();
  await work();
  return performance.now() - started;
};

describe("another tab holding the storage lock for long", () => {
  it("never delays reads: unrelated ones skip the refresh, preset reads wait once and keep the cached view", async () => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site, true, quick)];
    await a.create("from A");
    const release = site.hold(a);
    const load = vi.spyOn(b.storage, "load");

    for (let i = 0; i < 3; i++)
      expect(await elapsed(() => b.coordinator.run("GET", "/api/plugins", async () => frame(200, [])))).toBeLessThan(30);
    expect(load).not.toHaveBeenCalled();
    // The first preset read waits briefly; later ones do not wait while the lock stays held.
    expect(await elapsed(async () => expect(read(await b.list()).body).toEqual([]))).toBeGreaterThanOrEqual(35);
    for (let i = 0; i < 3; i++) expect(await elapsed(() => b.list())).toBeLessThan(30);
    expect(load).not.toHaveBeenCalled();
    expect(b.log.warn).toHaveBeenCalledOnce();

    await release();
    expect(read(await b.list()).body).toEqual(["from A"]);
    expect(load).toHaveBeenCalledOnce();
  });

  it("refreshes only reads of presets, reference packs and document opens", () => {
    for (const url of ["/api/presets", "/api/reference-packs?x=1", "/api/workspaces/w/documents/d/presets",
      "/api/workspaces/w/files/f/open", "/api/presets/transfer/previews/t"])
      expect(readsDurableData(url), url).toBe(true);
    for (const url of ["/api/plugins", "/api/version", "/api/workspaces/w", "/api/workspaces/w/documents/d/nodes?id=presets"])
      expect(readsDurableData(url), url).toBe(false);
  });
});

describe("a mutation whose runtime never answers", () => {
  it("releases the lock after the watchdog, never flushes again and reports the runtime as hung", async () => {
    const site = new Site();
    const [a, b] = [await bootTab(site, true, quick), await bootTab(site)];
    await a.create("kept");
    const hung = a.coordinator.run("POST", "/api/presets", () => new Promise<Uint8Array>(() => undefined));
    await expect(hung).rejects.toBeInstanceOf(RuntimeHungError);
    expect(a.log.error).toHaveBeenCalledWith(expect.stringMatching(/did not finish.*Reload the page/));

    // The other tab can save again, and the hung tab never writes or answers anything else.
    expect(read(await b.create("from B")).status).toBe(200);
    const save = vi.spyOn(a.storage, "save");
    await expect(a.list()).rejects.toBeInstanceOf(RuntimeHungError);
    await expect(a.create("never")).rejects.toBeInstanceOf(RuntimeHungError);
    expect(save).not.toHaveBeenCalled();
    expect(library(site)).toEqual(["kept", "from B"]);
  });

  it("leaves slow but finishing mutations alone", async () => {
    const site = new Site();
    const a = await bootTab(site, true, { mutationWatchdogMs: 200 });
    const slow = a.coordinator.run("POST", "/api/presets", () => new Promise(resolve => setTimeout(() => resolve(frame(200, {})), 20)));
    expect(read(await slow).status).toBe(200);
    expect(read(await a.create("after")).status).toBe(200);
  });
});

describe("a runtime that stops during a mutation", () => {
  it("releases the lock at once when the runtime stops during a mutation, without waiting for the watchdog", async () => {
    const site = new Site();
    // The default watchdog (10 minutes) would outlast the test: only stop() can release the lock here.
    const [a, b] = [await bootTab(site), await bootTab(site)];
    await a.create("kept");
    let started!: () => void;
    const running = new Promise<void>(resolve => { started = resolve; });
    // A trap in an async continuation: the .NET runtime never settles the dispatch's promise.
    const trapped = a.coordinator.run("POST", "/api/presets", () => { started(); return new Promise<Uint8Array>(() => undefined); });
    await running;
    a.coordinator.stop("RuntimeError: memory access out of bounds");
    await expect(trapped).rejects.toThrow("memory access out of bounds");
    await expect(trapped).rejects.toBeInstanceOf(RuntimeHungError);

    expect(await elapsed(async () => expect(read(await b.create("from B")).status).toBe(200))).toBeLessThan(1_000);
    const save = vi.spyOn(a.storage, "save");
    await expect(a.create("never")).rejects.toBeInstanceOf(RuntimeHungError);
    expect(save).not.toHaveBeenCalled();
    expect(library(site)).toEqual(["kept", "from B"]);
  });

  it("does not dispatch a mutation when the runtime stopped while the tree was reloading", async () => {
    const site = new Site();
    const a = await bootTab(site);
    const load = a.storage.load.bind(a.storage);
    vi.spyOn(a.storage, "load").mockImplementation(async () => { await load(); a.coordinator.stop("Aborted(OOM)"); });
    const dispatch = vi.fn(async () => frame(200, {}));
    await expect(a.coordinator.run("POST", "/api/presets", dispatch)).rejects.toBeInstanceOf(RuntimeHungError);
    expect(dispatch).not.toHaveBeenCalled();
  });

  it("touches no storage for a mutation whose runtime stopped while it waited for the lock", async () => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site)];
    const release = site.hold(b);
    await new Promise(resolve => setTimeout(resolve, 0));
    const dispatch = vi.fn(async () => frame(200, {}));
    const waiting = a.coordinator.run("POST", "/api/presets", dispatch);
    a.coordinator.stop("Aborted(OOM)");
    // A stopped runtime's file system cannot reload: that failure must not mask the stop as a 503.
    const load = vi.spyOn(a.storage, "load").mockRejectedValue(new Error("runtime exited"));
    const reloadStores = vi.spyOn(a.storage, "reloadStores");
    await release();
    await expect(waiting).rejects.toBeInstanceOf(RuntimeHungError);
    expect(load).not.toHaveBeenCalled();
    expect(reloadStores).not.toHaveBeenCalled();
    expect(dispatch).not.toHaveBeenCalled();
  });
});

describe("site data cleared while the tab is open", () => {
  it("reloads on the next preset read and writes into what IndexedDB holds now", async () => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site)];
    await a.create("deleted with the site data");
    expect(read(await b.list()).body).toEqual(["deleted with the site data"]);

    site.idb = new Map();
    // What the IndexedDB connection's close event reports (runtime/idbfsTree.ts reconnectingSync).
    b.coordinator.invalidate();
    expect(read(await b.list()).body).toEqual([]);
    expect(read(await b.create("after clearing")).status).toBe(200);
    expect(library(site)).toEqual(["after clearing"]);
    // Without a close event, the next write still loads first and drops what is gone.
    expect(read(await a.create("from A")).status).toBe(200);
    expect(library(site)).toEqual(["after clearing", "from A"]);
  });
});
