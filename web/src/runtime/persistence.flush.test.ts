// @vitest-environment node
import { describe, expect, it, vi } from "vitest";
import { bootTab, frame, library, Site } from "../test/persistenceSite";
import { RuntimeHungError } from "./persistence";

/** Settles with "answered" or "rejected", or "pending" when `request` is still running after `ms`. */
const outcome = (request: Promise<unknown>, ms = 300) => Promise.race([
  request.then(() => "answered", () => "rejected"),
  new Promise(resolve => setTimeout(() => resolve("pending"), ms)),
]);
const pause = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));

describe("a mutation's flush when the runtime stops", () => {
  it("rejects a mutation whose flush never settles and releases the exclusive lock", async () => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site)];
    vi.spyOn(b.storage, "save").mockImplementation(() => new Promise<void>(() => undefined));
    const mutation = b.create("x");
    await pause(20);
    expect(await outcome(mutation, 0)).toBe("pending");

    b.coordinator.stop("Aborted(trap in a .NET timer)");
    expect(await outcome(mutation)).toBe("rejected");
    await expect(mutation).rejects.toBeInstanceOf(RuntimeHungError);
    // A crash, not a storage failure: nothing reports a failed save or reverts.
    expect(b.log.error).not.toHaveBeenCalled();
    // The other tab saves at once.
    expect(await outcome(a.create("from A"))).toBe("answered");
    expect(library(site)).toEqual(["from A"]);
  });

  it("rejects a mutation whose verification never settles and releases the exclusive lock", async () => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site)];
    vi.spyOn(b.storage, "verifySaved").mockImplementation(() => new Promise<void>(() => undefined));
    const mutation = b.create("x");
    await pause(20);
    b.coordinator.stop("RuntimeError: unreachable");
    expect(await outcome(mutation)).toBe("rejected");
    await expect(mutation).rejects.toBeInstanceOf(RuntimeHungError);
    expect(await outcome(a.create("from A"))).toBe("answered");
  });

  it("flushes nothing after a stop that lands between the dispatch settling and the flush", async () => {
    const flushedAfterStop: number[] = [];
    // The stop lands this many microtasks after the dispatch's answer; from some depth on it lands after
    // the flush (the mutation then succeeds), never in between.
    for (let depth = 0; depth < 12; depth++) {
      const site = new Site();
      const b = await bootTab(site);
      let stopped = false;
      vi.spyOn(b.storage, "save").mockImplementation(async () => {
        if (stopped) flushedAfterStop.push(depth);
        site.idb = new Map(b.local);
      });
      const mutation = b.coordinator.run("POST", "/api/presets", async () => {
        b.local.set("presets/presets.json", { content: "[\"after-trap\"]", mtime: 1e9 });
        let settled: Promise<void> = Promise.resolve();
        for (let i = 0; i < depth; i++) settled = settled.then(() => undefined);
        void settled.then(() => { b.coordinator.stop("RuntimeError: unreachable"); stopped = true; });
        return frame(200, {});
      });
      await mutation.catch(() => undefined);
    }
    expect(flushedAfterStop).toEqual([]);
  });
});

describe("a revert interrupted by a stop", () => {
  it("rejects with RuntimeHungError, not a storage-failure answer, when the revert's reload is abandoned", async () => {
    const site = new Site();
    const b = await bootTab(site);
    site.failSave = new Error("QuotaExceededError");
    // The mutation's own refresh loads once; the revert's load after the failed flush never settles.
    let calls = 0;
    vi.spyOn(b.storage, "load").mockImplementation(async () => {
      if (++calls > 1) return new Promise<void>(() => undefined);
      b.local = new Map(site.idb);
    });
    const mutation = b.create("x");
    await pause(20);
    expect(calls).toBe(2);
    b.coordinator.stop("Aborted(OOM)");
    await expect(mutation).rejects.toBeInstanceOf(RuntimeHungError);
  });
});
