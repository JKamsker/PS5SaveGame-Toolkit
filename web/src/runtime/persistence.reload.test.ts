// @vitest-environment node
import { describe, expect, it, vi } from "vitest";
import { bootTab, frame, library, read, Site, tick, type Tab } from "../test/persistenceSite";
import { RuntimeHungError } from "./persistence";

/** Settles with "answered" or "rejected", or "pending" when `request` is still running after `ms`. */
const outcome = (request: Promise<unknown>, ms = 500) => Promise.race([
  request.then(() => "answered", () => "rejected"),
  new Promise(resolve => setTimeout(() => resolve("pending"), ms)),
]);
const pause = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));

/** The installed reference packs, read lazily from the tree as ReferencePackStore.UserPacks does after a reload. */
const packs = (tab: Tab) => [...tab.local.keys()].filter(path => path.startsWith("packs/")).map(path => path.slice(6)).sort();
const listPacks = (tab: Tab) => tab.coordinator.run("GET", "/api/reference-packs", async () => frame(200, packs(tab)));
/** A pack import (and removals) in `tab`; the coordinator flushes and announces it. */
const changePacks = (tab: Tab, add: string[], remove: string[] = []) =>
  tab.coordinator.run("POST", "/api/reference-packs", async () => {
    for (const name of remove) tab.local.delete(`packs/${name}`);
    for (const name of add) tab.local.set(`packs/${name}`, { content: name, mtime: tick() });
    return frame(200, {});
  });

/**
 * IDBFS' reconcile in `syncfs(true)`: entries IndexedDB no longer holds are removed from the tree at once,
 * and new or changed ones are written later, from IndexedDB's callbacks (here after `ms`).
 */
function slowReconcile(tab: Tab, ms: number) {
  return vi.spyOn(tab.storage, "load").mockImplementation(async () => {
    const stored = new Map([...tab.site.idb].map(([path, entry]) => [path, { ...entry }]));
    for (const path of [...tab.local.keys()]) if (!stored.has(path)) tab.local.delete(path);
    await pause(ms);
    for (const [path, entry] of stored) if (tab.local.get(path)?.mtime !== entry.mtime) tab.local.set(path, entry);
  });
}

describe("a durable read whose reload is slower than the read's lock wait", () => {
  it("waits for the reload and answers the whole new view, never a tree half way between two saves", async () => {
    const site = new Site();
    const a = await bootTab(site);
    await changePacks(a, ["P", "R"]);
    const b = await bootTab(site, true, { readLockWaitMs: 20 });
    expect(read(await listPacks(b)).body).toEqual(["P", "R"]);
    // Another tab replaces P with Q and announces it.
    await changePacks(a, ["Q"], ["P"]);
    slowReconcile(b, 60);
    // Answering before the reload finished would list ["R"]: P is gone, Q not yet written.
    expect(read(await listPacks(b)).body).toEqual(["Q", "R"]);
    expect(read(await listPacks(b)).body).toEqual(["Q", "R"]);
  });

  it("shows another tab's announced save although every reload takes longer than that wait", async () => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site, true, { readLockWaitMs: 20 })];
    slowReconcile(b, 40);
    await a.create("announced");
    for (let i = 0; i < 3; i++) {
      expect(read(await b.list()).body).toEqual(["announced"]);
      await pause(50);
    }
  });
});

describe("a reload that never settles when the runtime stops", () => {
  it("rejects the read and the mutation waiting behind it, and releases the storage lock", async () => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site, true, { readLockWaitMs: 20 })];
    await a.create("kept");
    const load = vi.spyOn(b.storage, "load").mockImplementation(() => new Promise<void>(() => undefined));
    const reading = b.list();
    const dispatch = vi.fn(async () => frame(200, {}));
    const mutation = b.coordinator.run("POST", "/api/presets", dispatch);
    // Well past the read's lock wait: the read still waits for its reload, the mutation behind it.
    await pause(50);
    expect(await outcome(reading, 0)).toBe("pending");
    expect(await outcome(mutation, 0)).toBe("pending");

    b.coordinator.stop("RuntimeError: memory access out of bounds");
    expect(await outcome(mutation)).toBe("rejected");
    await expect(reading).rejects.toBeInstanceOf(RuntimeHungError);
    await expect(mutation).rejects.toBeInstanceOf(RuntimeHungError);
    expect(dispatch).not.toHaveBeenCalled();
    expect(load).toHaveBeenCalledOnce();
    // The lock is free: the other tab saves at once, and the stopped tab never writes anything.
    const save = vi.spyOn(b.storage, "save");
    expect(await outcome(a.create("from A"))).toBe("answered");
    expect(save).not.toHaveBeenCalled();
    expect(library(site)).toEqual(["kept", "from A"]);
  });

  it("rejects a mutation waiting for its own reload and releases the exclusive lock", async () => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site)];
    vi.spyOn(b.storage, "load").mockImplementation(() => new Promise<void>(() => undefined));
    const dispatch = vi.fn(async () => frame(200, {}));
    const mutation = b.coordinator.run("POST", "/api/presets", dispatch);
    await pause(20);
    b.coordinator.stop("Aborted(OOM)");
    expect(await outcome(mutation)).toBe("rejected");
    await expect(mutation).rejects.toBeInstanceOf(RuntimeHungError);
    expect(dispatch).not.toHaveBeenCalled();
    expect(await outcome(a.create("from A"))).toBe("answered");
    expect(library(site)).toEqual(["from A"]);
  });
});
