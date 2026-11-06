import { expect, test, type Page } from "@playwright/test";
import { createHash } from "node:crypto";
import { zipSync } from "fflate";
import type { FieldNode, ReferenceRecordPair, SaveDocument } from "../src/api/protocol";
import { apiRequest } from "./apiRequest";
import { apiFlush, installApiHooks, onApi, routeApi } from "./apiRoutes";
import { editCoins, home, importSave, loadJson, openJson, upload } from "./helpers";
import { runtimeWorker } from "./runtimeFiles";

/**
 * Two tabs of one browser profile share the preset library: one IndexedDB database (IDBFS), one Web
 * Lock and one BroadcastChannel (web/src/runtime/persistence.ts). Each page here is a real tab of the
 * same browser context, with its own .NET runtime worker.
 */

/** Must match PERSISTENT_CHANGES_CHANNEL in web/src/runtime/persistentChanges.ts. */
const channelName = "savetoolkit-persistent-storage";
type Announcement = { type: string; tab: string; changed: string[] };

/** Records every change announcement the page receives (its own tab's echo included). */
function recordAnnouncements(name: string) {
  const scope = window as unknown as { __announcements: unknown[] };
  scope.__announcements = [];
  new BroadcastChannel(name).onmessage = event => { scope.__announcements.push(event.data); };
}

const announcements = (page: Page) => page.evaluate(() => (window as unknown as { __announcements: Announcement[] }).__announcements);

/** Counts the storage-lock requests of the page's runtime worker by mode, from now on. */
async function lockLog(page: Page) {
  const worker = await runtimeWorker(page);
  await worker.evaluate(() => {
    const scope = globalThis as unknown as { __locks: string[] };
    scope.__locks = [];
    const locks = navigator.locks;
    const request = locks.request.bind(locks) as unknown as (...args: unknown[]) => Promise<unknown>;
    (locks as unknown as { request: (...args: unknown[]) => Promise<unknown> }).request = (...args: unknown[]) => {
      const options = args[1];
      scope.__locks.push(typeof options === "object" && options !== null && "mode" in options ? String(options.mode) : "exclusive");
      return request(...args);
    };
  });
  return {
    tab: await worker.evaluate(() => (globalThis as unknown as { name: string }).name.replace(/^savetoolkit-runtime /, "")),
    taken: () => worker.evaluate(() => [...(globalThis as unknown as { __locks: string[] }).__locks]),
  };
}

/** Makes the page's runtime never finish its next preset save, like a tab frozen while it holds the lock. */
async function freezePresetSaves(page: Page) {
  const worker = await runtimeWorker(page);
  await worker.evaluate(async () => {
    type Interop = { DispatchAsync(method: string, url: string, ...rest: unknown[]): Promise<number> };
    const runtime = (globalThis as unknown as { getDotnetRuntime(id: number): { getAssemblyExports(name: string): Promise<unknown> } })
      .getDotnetRuntime(0);
    // The runtime caches each assembly's exports: this is the object the worker dispatches through.
    const interop = (await runtime.getAssemblyExports("SaveToolkit.Browser") as { SaveToolkit: { Browser: { Interop: Interop } } })
      .SaveToolkit.Browser.Interop;
    const dispatch = interop.DispatchAsync.bind(interop);
    interop.DispatchAsync = (method, url, ...rest) => method === "POST" && /\/presets(\/capture)?$/.test(url)
      ? new Promise<number>(() => undefined) : dispatch(method, url, ...rest);
  });
}

/** Imports and opens the fixture in the page's current runtime (a navigation would start a new one). */
async function openPresets(page: Page) {
  await upload(page);
  await expect(page.getByRole("heading", { name: "Pick a save file" })).toBeVisible();
  await page.getByRole("navigation", { name: "Save directories" }).getByRole("button", { name: / E2E / }).click();
  await openJson(page);
  await loadJson(page);
  await editCoins(page);
  await page.getByRole("tab", { name: "Overview", exact: true }).click();
  const panel = page.locator(".presets-panel");
  await panel.getByText("Toolkit presets", { exact: true }).click();
  await expect(panel.getByRole("heading", { name: "Saved presets", exact: true })).toBeVisible();
  return panel;
}

async function savePreset(page: Page, name: string) {
  const panel = page.locator(".presets-panel");
  await panel.getByLabel("Name", { exact: true }).fill(name);
  await panel.getByRole("button", { name: "Save preset", exact: true }).click();
}

const savedResult = (page: Page) => page.locator(".presets-panel").getByRole("status", { name: "Preset save result", exact: true });
const presetRow = (page: Page, name: string) => page.locator(".presets-panel").getByRole("listitem").filter({ hasText: name });
const libraryNames = async (page: Page) =>
  (await (await apiRequest(page).get("/api/presets")).json() as { name: string }[]).map(preset => preset.name).sort();

/** Where the storage lock stands, as every tab of the site sees it. */
const lockState = (page: Page) => page.evaluate(async name => {
  const state = await navigator.locks.query();
  return { held: state.held?.filter(lock => lock.name === name).map(lock => lock.mode) ?? [],
    pending: state.pending?.filter(lock => lock.name === name).map(lock => lock.mode) ?? [] };
}, channelName);

/**
 * Tab one freezes in the middle of a save, holding the exclusive lock; tab two's save waits for it rather
 * than writing over tab one's library. Closing the frozen tab releases its lock (Web Locks end with the
 * page and its worker): the waiting save completes on freshly loaded storage, and the frozen save never
 * reached it.
 */
async function recoverFromFrozenTab(one: Page, two: Page) {
  await freezePresetSaves(one);
  await savePreset(one, "Frozen in tab one");
  await expect.poll(() => lockState(two)).toEqual({ held: ["exclusive"], pending: [] });
  await savePreset(two, "Saved in tab two");
  await expect.poll(() => lockState(two)).toEqual({ held: ["exclusive"], pending: ["exclusive"] });
  await expect(savedResult(two)).toHaveCount(0);

  await one.close();
  await expect(savedResult(two)).toHaveText("Saved “Saved in tab two”.", { timeout: 15_000 });
  await expect(presetRow(two, "Saved in tab one")).toHaveCount(1);
  await expect(presetRow(two, "Saved in tab two")).toHaveCount(1);
  await expect(presetRow(two, "Frozen in tab one")).toHaveCount(0);
  expect(await libraryNames(two)).toEqual(["Saved in tab one", "Saved in tab two"]);
  expect(await lockState(two)).toEqual({ held: [], pending: [] });
}

test("two tabs share presets, the other tab reloads them, and closing a tab frozen mid-save frees the library", async ({ context }) => {
  // Three tabs boot their own runtime.
  test.setTimeout(120_000);
  const one = await context.newPage();
  const two = await context.newPage();
  for (const page of [one, two]) await page.addInitScript(recordAnnouncements, channelName);

  // Tab two boots first: its runtime has loaded the (empty) library before tab one saves.
  await home(two);
  expect(await libraryNames(two)).toEqual([]);
  const twoLocks = await lockLog(two);
  await home(one);
  await openPresets(one);
  const oneLocks = await lockLog(one);
  expect(oneLocks.tab).not.toBe(twoLocks.tab);

  await savePreset(one, "Saved in tab one");
  await expect(savedResult(one)).toHaveText("Saved “Saved in tab one”.");
  // The save ran under the exclusive lock and was announced with the saving tab's nonce, to both pages.
  expect(await oneLocks.taken()).toEqual(["exclusive"]);
  const expected = { type: "persistentChange", tab: oneLocks.tab, changed: ["presets"] };
  await expect.poll(() => announcements(two)).toEqual([expected]);
  await expect.poll(() => announcements(one)).toEqual([expected]);
  // Tab one's worker never hears its own announcement (a BroadcastChannel skips its sender), so it
  // still shows its library without reloading storage; the page-side echo filter has its own test below.
  expect(await libraryNames(one)).toEqual(["Saved in tab one"]);
  expect(await oneLocks.taken()).toEqual(["exclusive"]);

  // Tab two learned of the change: its first preset read reloads storage under the shared lock.
  await openPresets(two);
  await expect(presetRow(two, "Saved in tab one")).toHaveCount(1);
  expect(await twoLocks.taken()).toContain("shared");
  expect(await libraryNames(two)).toEqual(["Saved in tab one"]);

  await recoverFromFrozenTab(one, two);

  // A new tab reads exactly what IndexedDB kept.
  const three = await context.newPage();
  await home(three);
  expect(await libraryNames(three)).toEqual(["Saved in tab one", "Saved in tab two"]);
});

const png = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=", "base64");
const sha256 = (content: Uint8Array) => createHash("sha256").update(content).digest("hex");

/** A valid user pack (the manifest of tests/SaveToolkit.Core.Tests/References/PackBuilder.cs) for no installed plugin. */
function referencePack(): { name: string; mimeType: string; buffer: Buffer } {
  const records = Buffer.from(JSON.stringify({ records: [{ id: "1001", name: "Potion", assetId: "icons/potion.png" }] }));
  const attribution = { sourceUrl: "https://example.org/wiki/items", author: "Example wiki contributors", license: "CC-BY-SA-4.0",
    licenseUrl: "https://creativecommons.org/licenses/by-sa/4.0/", retrieved: "2026-10-01", modifications: "Synthetic e2e records." };
  const manifest = { schema: 1, id: "e2e.tab-items", version: "1.0.0", title: "E2E tab items", plugins: ["e2e.no-plugin"],
    titleIds: [], gameBuilds: [], locale: "en", datasets: [
      { id: "items", kind: "records", files: [{ path: "items.json", sha256: sha256(records) }], attribution },
      { id: "icons", kind: "assets", files: [{ path: "icons/potion.png", sha256: sha256(png) }], attribution }] };
  const zip = zipSync({ "pack.json": Buffer.from(JSON.stringify(manifest)), "items.json": records, "icons/potion.png": png });
  return { name: "e2e-tab-items.zip", mimeType: "application/zip", buffer: Buffer.from(zip) };
}

/**
 * Projects a synthetic reference field (GenericJson has none) whose only candidate has an icon, opens
 * its picker, and counts how often the page fetches that icon (apiObjectUrls caches it by path).
 */
async function referenceIcons(page: Page) {
  await installApiHooks(page);
  await routeApi(page, /\/api\/workspaces\/[^/]+\/(?:files\/[^/]+\/open|documents\/[^/]+)$/, async route => {
    const response = await route.fetch();
    const body = await response.json() as Partial<SaveDocument>;
    const field: FieldNode = { type: "field", valueId: "synthetic-reference", label: "Synthetic item", value: "1001",
      spec: { kind: "reference", catalogId: "synthetic-items", readOnly: true } };
    if (body.view) body.view = { type: "section", title: "Synthetic reference fixture", children: [body.view, field] };
    await route.fulfill({ response, json: body });
  });
  const candidate = { rawId: "1001", label: "Potion", assetId: "icons/potion.png",
    eligibility: { available: true, eligible: true, selectable: true, reasons: [] } };
  await routeApi(page, "**/reference-fields/records", async route => {
    const body = route.request().postDataJSON() as { revision: number; pairs: ReferenceRecordPair[] };
    await route.fulfill({ json: { revision: body.revision, results: body.pairs.map(pair => ({ ...pair, status: 200, candidate })) } });
  });
  await routeApi(page, "**/reference-fields/candidates?*", route => route.fulfill({ json: {
    catalogId: "synthetic-items", revision: 0, items: [candidate], total: 1, offset: 0, limit: 50, categories: [],
  } }));
  await routeApi(page, "**/reference-assets?*", route => route.fulfill({ contentType: "image/png", body: png }));
  let fetched = 0;
  onApi(page, "request", request => { if (request.url().includes("/reference-assets?")) fetched++; });
  await importSave(page);
  await openJson(page);
  await page.locator(".reference-trigger").click();
  await expect(page.getByRole("dialog", { name: "Browse items", exact: true }).getByRole("option", { name: /Potion/ })).toBeVisible();
  await expect.poll(() => fetched).toBe(1);
  return () => fetched;
}

/**
 * A pack imported in one tab may change any reference asset, so every other tab drops its cached icons
 * when the import is announced (web/src/lib/apiObjectUrls.ts). The importing page ignores the echo of
 * its own announcement: the answer to its request already dropped them. The import here bypasses the
 * app (apiRequest), so only the announcement could make tab one fetch its icon again.
 */
test("a pack imported in one tab drops the other tab's cached reference icons, not its own", async ({ context }) => {
  test.setTimeout(90_000);
  const one = await context.newPage();
  const two = await context.newPage();
  for (const page of [one, two]) await page.addInitScript(recordAnnouncements, channelName);
  const oneIcons = await referenceIcons(one);
  const twoIcons = await referenceIcons(two);

  const imported = await apiRequest(one).post("/api/reference-packs", { multipart: { pack: referencePack() } });
  expect(imported.status()).toBe(200);
  const tab = (await lockLog(one)).tab;
  const expected = [{ type: "persistentChange", tab, changed: ["referencePacks"] }];
  await expect.poll(() => announcements(two)).toEqual(expected);
  await expect.poll(() => announcements(one)).toEqual(expected);
  await expect.poll(twoIcons).toBe(2);
  await apiFlush(one);
  expect(oneIcons()).toBe(1);
});
