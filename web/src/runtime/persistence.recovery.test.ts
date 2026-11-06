// @vitest-environment node
import { describe, expect, it, vi } from "vitest";
import { bootTab, frame, library, read, Site, type Tab } from "../test/persistenceSite";
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

describe("a runtime that traps inside a mutation's dispatch", () => {
  it.each([
    ["a WebAssembly trap", new WebAssembly.RuntimeError("unreachable")],
    ["an aborted runtime", new Error("Aborted(OOM). Build with -sASSERTIONS for more info.")],
  ])("neither reverts nor reloads the stores when a mutation's dispatch rejects with %s", async (_, failure) => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site)];
    await a.create("kept");
    const load = vi.spyOn(a.storage, "load");
    const reloadStores = vi.spyOn(a.storage, "reloadStores");
    // The trap surfaces before the worker calls stop(): the dispatch changed the tree, then rejected.
    const trapped = a.coordinator.run("POST", "/api/presets", async () => { a.storage.writeGeneration("partial"); throw failure; });
    await expect(trapped).rejects.toBe(failure);
    expect(reloadStores).not.toHaveBeenCalled();
    expect(load).toHaveBeenCalledOnce(); // only the refresh before the dispatch, no revert
    // The lock was released with the rejection.
    expect(await elapsed(async () => expect(read(await b.create("from B")).status).toBe(200))).toBeLessThan(1_000);
    expect(library(site)).toEqual(["kept", "from B"]);
  });
});

describe("a trap the worker has not reported yet", () => {
  it("refuses every later request itself once a mutation's dispatch rejected with a trap", async () => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site)];
    await a.create("kept");
    // The trapped dispatch changed the .NET store's cached view and the tree, then rejected; the worker's
    // stop() is never called here, so only the coordinator can keep that view from being flushed.
    const trapped = a.coordinator.run("POST", "/api/presets", async () => {
      a.presets = [...a.presets, "ghost"];
      a.storage.writeGeneration("partial");
      throw new WebAssembly.RuntimeError("unreachable");
    });
    await expect(trapped).rejects.toBeInstanceOf(WebAssembly.RuntimeError);
    const save = vi.spyOn(a.storage, "save");
    await expect(a.create("next")).rejects.toThrow(new RuntimeHungError("unreachable"));
    await expect(a.list()).rejects.toBeInstanceOf(RuntimeHungError);
    expect(save).not.toHaveBeenCalled();
    expect(read(await b.create("from B")).status).toBe(200);
    expect(library(site)).toEqual(["kept", "from B"]);
  });

  it("reports a trap while a mutation makes the stores re-read another tab's saves as a crash, not a 503", async () => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site)];
    await b.create("from B"); // a must now reload its stores before its next mutation
    const trap = new WebAssembly.RuntimeError("memory access out of bounds");
    a.storage.reloadStores = () => { throw trap; };
    const dispatch = vi.fn(async () => frame(200, {}));
    // The worker reports the rethrown trap as a crash (isRuntimeStopped), never as a failed request.
    await expect(a.coordinator.run("POST", "/api/presets", dispatch)).rejects.toBe(trap);
    expect(dispatch).not.toHaveBeenCalled();
    await expect(a.create("next")).rejects.toBeInstanceOf(RuntimeHungError);
    // The lock was released with the rejection.
    expect(await elapsed(async () => expect(read(await b.create("again")).status).toBe(200))).toBeLessThan(1_000);
    expect(library(site)).toEqual(["from B", "again"]);
  });

  it("does not dispatch a preset read after a trap while it made the stores re-read another tab's saves", async () => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site)];
    await b.create("from B");
    const trap = new WebAssembly.RuntimeError("unreachable");
    a.storage.reloadStores = () => { throw trap; };
    const dispatch = vi.fn(async () => frame(200, []));
    await expect(a.coordinator.run("GET", "/api/presets", dispatch)).rejects.toBe(trap);
    expect(dispatch).not.toHaveBeenCalled();
    await expect(a.coordinator.run("GET", "/api/plugins", dispatch)).rejects.toBeInstanceOf(RuntimeHungError);
    expect(dispatch).not.toHaveBeenCalled();
  });
});

describe("a library another tab saved that this tab cannot read", () => {
  // PresetLibraryStaging echoes file content: $"Preset schema '{preset.SchemaVersion}' is not supported."
  // A .NET message is never a trap (a trap cannot reach .NET's catch), even one that reads exactly like it.
  const messages = ["Preset schema 'RuntimeError: 3' is not supported.", "RuntimeError: unreachable"];

  it.each(messages)("refuses a mutation with a 503 and keeps the tab usable when the stores report %j", async quoting => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site)];
    await b.create("from B");
    a.reloadError = quoting;
    expect(read(await a.create("refused")).status).toBe(503);
    a.reloadError = "";
    expect(read(await a.create("after the fix")).status).toBe(200);
    expect(library(site)).toEqual(["from B", "after the fix"]);
  });

  it.each(messages)("serves preset reads from the last good view when the stores report %j", async quoting => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site)];
    await b.create("from B");
    a.reloadError = quoting;
    expect(read(await a.list())).toEqual({ status: 200, body: [] });
    expect(read(await a.coordinator.run("GET", "/api/plugins", async () => frame(200, []))).status).toBe(200);
  });
});

describe("a change that was saved but could not be announced", () => {
  it("still answers the mutation as saved, and other tabs load it before their next change", async () => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site)];
    site.failAnnounce = new DOMException("The channel is closed.", "InvalidStateError");
    expect(read(await a.create("from A")).status).toBe(200);
    expect(a.log.warn).toHaveBeenCalledWith(expect.stringMatching(/other tabs/), site.failAnnounce);
    site.failAnnounce = null;
    expect(read(await b.create("from B")).status).toBe(200);
    expect(library(site)).toEqual(["from A", "from B"]);
  });
});

describe("a runtime that stops while another tab's saves are reloaded", () => {
  it("reports a runtime that stops while a mutation reloads another tab's saves as hung, not as unreadable storage", async () => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site)];
    await b.create("from B"); // a must now make its stores re-read the files before its next write
    stopWhileLoading(a);
    const reloadStores = vi.spyOn(a.storage, "reloadStores");
    const dispatch = vi.fn(async () => frame(200, {}));
    await expect(a.coordinator.run("POST", "/api/presets", dispatch)).rejects.toBeInstanceOf(RuntimeHungError);
    expect(reloadStores).not.toHaveBeenCalled();
    expect(dispatch).not.toHaveBeenCalled();
    expect(a.log.error).not.toHaveBeenCalled();
  });

  it("does not dispatch a preset read when the runtime stopped while the read refreshed another tab's saves", async () => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site)];
    await b.create("from B"); // a's next preset read refreshes first
    stopWhileLoading(a);
    const reloadStores = vi.spyOn(a.storage, "reloadStores");
    const dispatch = vi.fn(async () => frame(200, []));
    await expect(a.coordinator.run("GET", "/api/presets", dispatch)).rejects.toBeInstanceOf(RuntimeHungError);
    expect(reloadStores).not.toHaveBeenCalled();
    expect(dispatch).not.toHaveBeenCalled();
  });
});

/** The runtime traps while IndexedDB is read; a .NET store reload would then fail too. */
function stopWhileLoading(tab: Tab) {
  const load = tab.storage.load.bind(tab.storage);
  vi.spyOn(tab.storage, "load").mockImplementation(async () => {
    await load();
    tab.coordinator.stop("RuntimeError: unreachable");
    tab.reloadError = "The runtime has stopped.";
  });
}

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
