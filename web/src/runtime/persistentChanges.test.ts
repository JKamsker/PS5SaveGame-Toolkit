// @vitest-environment node
import { afterEach, expect, it, vi } from "vitest";
import { encodeFrame } from "./framing";
import { browserTabs, PersistenceCoordinator, type PersistentStorage } from "./persistence";
import { changedData, parsePersistentChange, PERSISTENT_CHANGES_CHANNEL, runtimeWorkerName, tabOfWorkerName } from "./persistentChanges";

afterEach(() => { vi.unstubAllGlobals(); });

it("names what a durable mutation may have changed, and everything for an unknown route", () => {
  expect(changedData("/api/reference-packs")).toEqual(["referencePacks"]);
  expect(changedData("/api/reference-packs/pack%3Aa?x=1")).toEqual(["referencePacks"]);
  expect(changedData("/api/presets")).toEqual(["presets"]);
  expect(changedData("/api/presets/transfer/commit")).toEqual(["presets"]);
  expect(changedData("/api/workspaces/w/documents/d/presets/capture")).toEqual(["presets"]);
  expect(changedData("/api/something-new")).toEqual(["presets", "referencePacks"]);
});

it("reads change messages; anything else is a change of everything by another tab", () => {
  expect(parsePersistentChange({ type: "persistentChange", tab: "a", changed: ["presets"] })).toEqual({ tab: "a", changed: ["presets"] });
  expect(parsePersistentChange("changed")).toEqual({ tab: undefined, changed: ["presets", "referencePacks"] });
  expect(parsePersistentChange({ type: "persistentChange", tab: "a", changed: ["themes"] }))
    .toEqual({ tab: "a", changed: ["presets", "referencePacks"] });
});

it("hands the page's tab nonce to its worker through the worker's name", () => {
  expect(tabOfWorkerName(runtimeWorkerName("nonce-1"))).toBe("nonce-1");
  expect(tabOfWorkerName("savetoolkit-runtime")).toBeUndefined();
  expect(tabOfWorkerName(undefined)).toBeUndefined();
});

/** Just enough storage for one mutation that changes the tree. */
const storage = (): PersistentStorage => ({
  load: async () => undefined, save: async () => undefined, trackChanges: () => () => true,
  verifySaved: async () => undefined, readGeneration: () => "", writeGeneration: () => undefined, reloadStores: () => "",
});
const ok = () => encodeFrame(200, [], new Uint8Array());

it("announces what this tab's worker saved, tagged with the tab nonce from its name", async () => {
  vi.stubGlobal("name", runtimeWorkerName("tab-a"));
  const coordinator = new PersistenceCoordinator(storage(), browserTabs());
  const received: unknown[] = [];
  const listener = new BroadcastChannel(PERSISTENT_CHANGES_CHANNEL);
  listener.onmessage = event => received.push((event as MessageEvent).data);
  try {
    await coordinator.run("POST", "/api/reference-packs", async () => ok());
    await coordinator.run("PATCH", "/api/presets/p", async () => ok());
    await vi.waitFor(() => expect(received).toHaveLength(2));
    expect(received).toEqual([
      { type: "persistentChange", tab: "tab-a", changed: ["referencePacks"] },
      { type: "persistentChange", tab: "tab-a", changed: ["presets"] },
    ]);
  } finally {
    listener.close();
  }
});
