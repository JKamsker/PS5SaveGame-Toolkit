// @vitest-environment node
import { describe, expect, it, vi } from "vitest";
import { bootTab, frame, library, read, Site, tick, Tab } from "../test/persistenceSite";
import { storageFailure, writesDurableData } from "./persistence";

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

describe("another tab's saves are never overwritten", () => {
  it("refuses mutations while the stores cannot read another tab's files, then retries", async () => {
    const site = new Site();
    const [a, b] = [await bootTab(site), await bootTab(site)];
    await a.create("A1");
    await a.create("A2");
    // For example tab A runs a newer version of the site and saved a record schema this tab cannot read.
    b.reloadError = "Preset schema '2' is not supported.";

    const refused = read(await b.create("B1"));
    expect(refused.status).toBe(503);
    expect(refused.body.detail).toMatch(/saved in another tab could not be read here \(Preset schema '2'.*not made/);
    expect(library(site)).toEqual(["A1", "A2"]);
    // Reads keep the last good view; the failure is logged, not retried on every read.
    expect(read(await b.list()).body).toEqual([]);
    expect(b.log.error).toHaveBeenCalled();
    expect(read(await b.create("B2")).status).toBe(503);
    expect(library(site)).toEqual(["A1", "A2"]);

    b.reloadError = "";
    expect(read(await b.create("B3")).status).toBe(200);
    expect(library(site)).toEqual(["A1", "A2", "B3"]);
  });

  it("loads before its first write once the lock is free", async () => {
    const site = new Site();
    const a = await bootTab(site);
    await a.create("saved earlier");
    site.lockUnavailable = true;
    const b = new Tab(site);
    b.local = new Map();
    b.presets = [];
    expect(await b.coordinator.initialLoad()).toBe(false);
    expect(read(await b.list()).body).toEqual([]);
    expect(read(await b.create("refused")).status).toBe(503);

    site.lockUnavailable = false;
    expect(read(await b.list()).body).toEqual(["saved earlier"]);
    expect(read(await b.create("from B")).status).toBe(200);
    expect(library(site)).toEqual(["saved earlier", "from B"]);
  });

});

describe("a tab that booted while another tab held the lock", () => {
  it("does not wait for the lock on reads before the first load", async () => {
    const site = new Site();
    site.lockUnavailable = true;
    const b = new Tab(site);
    expect(await b.coordinator.initialLoad()).toBe(false);
    site.lockUnavailable = false;
    let release!: () => void;
    const held = site.tabs(b).lock!("exclusive", () => new Promise<void>(resolve => { release = resolve; }));
    expect(read(await b.list()).body).toEqual([]);
    release();
    await held;
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
      a.local.set("presets/stray.tmp", { content: "x", mtime: tick() });
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

});

describe("failed flushes", () => {
  it("never saves a change answered as not saved, even when undoing it failed", async () => {
    const site = new Site();
    const a = await bootTab(site);
    await a.create("kept");
    vi.spyOn(a.storage, "save").mockImplementationOnce(async () => {
      site.failLoad = new Error("IndexedDB closed");
      throw new Error("transaction aborted");
    });

    const rejected = read(await a.create("rejected"));
    expect(rejected.status).toBe(503);
    expect(rejected.body.detail).toMatch(/not saved.*until the page is reloaded/);
    expect(a.presets).toContain("rejected");
    site.failLoad = null;

    expect(read(await a.create("later")).status).toBe(200);
    expect(library(site)).toEqual(["kept", "later"]);
  });

  it("reports a flush that IndexedDB did not store as a failure and reverts it", async () => {
    const site = new Site();
    const a = await bootTab(site);
    // The flush "succeeds" but skips the file (what IDBFS does when modification times look equal).
    vi.spyOn(a.storage, "save").mockImplementationOnce(async () => undefined);
    site.failVerify = new Error("IndexedDB did not store 1 changed entry (presets/presets.json).");
    const response = read(await a.create("unverified"));
    expect(response.status).toBe(503);
    expect(response.body.detail).toMatch(/did not store 1 changed entry/);
    expect(a.presets).toEqual([]);
    expect(library(site)).toEqual([]);
  });

});

describe("failed requests", () => {
  it("reverts a durable change left behind by a request that threw", async () => {
    const site = new Site();
    const a = await bootTab(site);
    await expect(a.coordinator.run("POST", "/api/reference-packs", async () => {
      a.local.set("reference-packs/pack-x.zip", { content: "zip", mtime: tick() });
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

it("routes everything that may write presets or packs through the lock, but not workspace work or preset reads", () => {
  for (const [method, url] of [["POST", "/api/presets"], ["PATCH", "/api/presets/p1"], ["DELETE", "/api/presets/p1"],
    ["POST", "/api/presets/transfer/commit"], ["POST", "/api/reference-packs"], ["DELETE", "/api/reference-packs/x"],
    ["POST", "/api/workspaces/w/documents/d/presets/capture"], ["POST", "/api/workspaces/w/documents/d/presets/new-route"],
    ["POST", "/api/anything-new?x=1"]] as const)
    expect(writesDurableData(method, url), `${method} ${url}`).toBe(true);
  for (const [method, url] of [["GET", "/api/presets"], ["HEAD", "/api/reference-packs"], ["POST", "/api/workspaces"],
    ["POST", "/api/workspaces/w/documents/d/edits"], ["POST", "/api/workspaces/w/export?presets=1"], ["DELETE", "/api/workspaces/w"],
    ["POST", "/api/workspaces/w/documents/d/presets/p1/preview"], ["POST", "/api/workspaces/w/documents/d/presets/p1/apply"],
    ["POST", "/api/workspaces/w/documents/d/presets/transfer/compatibility"]] as const)
    expect(writesDurableData(method, url), `${method} ${url}`).toBe(false);
});
