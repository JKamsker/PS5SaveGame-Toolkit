import { test, expect, type Locator, type Page, type TestInfo } from "@playwright/test";
import { writeFile } from "node:fs/promises";
import { fixture, importSave, loadJson, openJson } from "./helpers";

const widths = [375, 768, 1024, 1440] as const;
const longPayload = "exact-long-" + "Z".repeat(700);

async function renderedEffectIds(region: Locator) {
  return region.locator("tbody tr").evaluateAll(rows => rows.map(row => {
    const marker = row.querySelector("th small")?.textContent ?? "";
    return marker.slice(marker.lastIndexOf("·") + 1).trim();
  }));
}

type Requests = { plan?: unknown; apply?: unknown };

async function installSyntheticRoutes(page: Page, requests: Requests) {
  await page.route("**/actions/plan", async route => {
    const upstream = await route.fetch();
    const original = await upstream.json();
    const changes = [
      { valueId: "effect-absent", label: "Effect absent", oldValue: null, newValue: longPayload,
        derived: false, oldExists: false, newExists: true,
        effect: { kind: "StructuralMemento", spec: { kind: "text", maxLength: 1024 },
          presentation: { confidence: "Inferred", warning: "Synthetic warning", description: "Long serial evidence" },
          path: ["inventory", "item:0"], source: "synthetic renderer fixture" } },
      { valueId: "effect-null-text", label: "Effect nullable text", oldValue: null, newValue: "null",
        derived: false, oldExists: true, newExists: true,
        effect: { kind: "Observation", spec: { kind: "nullable", inner: { kind: "text" } },
          presentation: { confidence: "Certain", description: "Explicit null versus text" },
          path: ["inventory", "item:1"], source: "synthetic renderer fixture" } },
      { valueId: "effect-union-exact", label: "Effect union exact",
        oldValue: { kind: "union", type: "count", value: { kind: "exactNumber", text: "9007199254740993" } },
        newValue: { kind: "union", type: "count", value: { kind: "exactNumber", text: "9007199254740994" } },
        derived: false, oldExists: true, newExists: true,
        effect: { kind: "Observation", spec: { kind: "union", options: [{ id: "count", label: "Count",
          spec: { kind: "integer", min: "0", max: "99999999999999999999" } }] },
          presentation: { confidence: "Inferred", description: "Exact integer union" },
          path: ["inventory", "item:2"], source: "synthetic renderer fixture" } },
      ...Array.from({ length: 98 }, (_, index) => ({ valueId: `effect-page-${index}`, label: `Effect page ${index}`,
        oldValue: index, newValue: index + 1, derived: false, oldExists: true, newExists: true,
        effect: { kind: index % 2 ? "Observation" : "StructuralMemento",
          spec: { kind: "integer", min: "0", max: "99999999999999999999" },
          presentation: { confidence: "Unknown" }, path: ["bulk", `row:${index}`], source: "synthetic renderer fixture" } })),
      { valueId: "canonical-preserved", label: "Canonical existing row", oldValue: 7, newValue: 7,
        derived: false, oldExists: true, newExists: true },
    ];
    const plan = { ...original, planId: "synthetic-effect-plan", stale: false,
      plan: { ...original.plan, changes, skippedRows: [],
        warnings: ["Synthetic fixture only; no production provider claim"],
        errors: [{ valueId: "synthetic-diagnostic", message: "Synthetic diagnostic alert" }] } };
    requests.plan = { request: route.request().postDataJSON(), response: plan };
    await route.fulfill({ response: upstream, json: plan });
  });
  await page.route("**/actions/apply", async route => {
    requests.apply = route.request().postDataJSON();
    const upstream = await route.fetch();
    const original = await upstream.json();
    await route.fulfill({ response: upstream, json: { ...original, ok: true, errors: [],
      warnings: original.warnings ?? [], changes: original.changes ?? [], structureChanged: original.structureChanged ?? true,
      state: original.state ?? { canUndo: true, canRedo: false, isDirty: true, revision: 1 } } });
  });
}

async function assertTypedEvidence(dialog: Locator, effectRegion: Locator) {
  await expect(dialog.getByRole("status")).toHaveText("102 field changes · 0 skipped rows · one undo step");
  await expect(dialog.getByText("101 action effects · 102 total ordered rows · one whole-action undo", { exact: true })).toBeVisible();
  await expect(dialog.getByText("Synthetic fixture only; no production provider claim", { exact: true })).toBeVisible();
  await expect(dialog.getByRole("alert").filter({ hasText: "Synthetic diagnostic alert" })).toBeVisible();
  await expect(effectRegion.getByRole("row").filter({ hasText: "Effect absent" })).toBeVisible();
  await expect(effectRegion.getByText("StructuralMemento · effect-absent", { exact: false })).toBeVisible();
  await expect(effectRegion.getByText("Synthetic warning", { exact: true })).toBeVisible();
  await expect(effectRegion.getByRole("row").filter({ hasText: "synthetic renderer fixture" }).first()).toBeVisible();
  await expect(effectRegion.getByText("(absent)", { exact: true }).first()).toBeVisible();
  await expect(effectRegion.getByText("null (explicit)", { exact: true }).first()).toBeVisible();
  await expect(effectRegion.getByText("text: null", { exact: true }).first()).toBeVisible();
  await expect(effectRegion.getByText("count: integer: 9007199254740993", { exact: true }).first()).toBeVisible();
  await expect(effectRegion.getByText("count: integer: 9007199254740994", { exact: true }).first()).toBeVisible();
  await expect(effectRegion.getByRole("row").filter({ hasText: "Certain" }).first()).toBeVisible();
  await expect(effectRegion.getByText("Long serial evidence", { exact: true })).toBeVisible();
  await expect(effectRegion.getByRole("row").filter({ hasText: "inventory / item:0" }).first()).toBeVisible();
  await expect(effectRegion.getByRole("row").filter({ hasText: "effect-null-text" })).toContainText("Observation");
  await expect(effectRegion.locator("input, select, textarea")).toHaveCount(0);
}

async function assertEffectPreview(dialog: Locator, effectRegion: Locator, authoredIds: string[], page: Page) {
  await assertTypedEvidence(dialog, effectRegion);
  const firstIds = await renderedEffectIds(effectRegion);
  expect(firstIds).toEqual(authoredIds.slice(0, 100));
  await effectRegion.focus();
  await expect(effectRegion).toBeFocused();
  const initialScroll = await effectRegion.evaluate(element => ({ scrollTop: element.scrollTop,
    scrollHeight: element.scrollHeight, clientHeight: element.clientHeight,
    scrollWidth: element.scrollWidth, clientWidth: element.clientWidth }));
  expect(initialScroll.scrollHeight).toBeGreaterThan(initialScroll.clientHeight);
  expect(initialScroll.scrollWidth).toBeLessThanOrEqual(initialScroll.clientWidth);
  await effectRegion.press("PageDown");
  await expect.poll(() => effectRegion.evaluate(element => element.scrollTop)).toBeGreaterThan(initialScroll.scrollTop + initialScroll.clientHeight / 2);
  const pagedScrollTop = await effectRegion.evaluate(element => element.scrollTop);
  await effectRegion.press("Home");
  await expect.poll(() => effectRegion.evaluate(element => element.scrollTop)).toBe(0);
  const longDetails = effectRegion.locator("details").nth(1);
  await longDetails.locator("summary").click();
  await longDetails.getByRole("button", { name: "Copy exact", exact: true }).click();
  const clipboardWrites = await page.evaluate(() => (window as unknown as { __effectClipboardWrites: string[] }).__effectClipboardWrites);
  expect(clipboardWrites).toContain(`text: ${longPayload}`);
  const effectPager = dialog.getByRole("navigation", { name: "Action effects pages", exact: true });
  await expect(effectPager.getByText("Page 1 of 2", { exact: true })).toBeVisible();
  await effectPager.getByRole("button", { name: "Next", exact: true }).click();
  await expect(effectRegion.getByRole("row").filter({ hasText: "Effect page 97" })).toBeVisible();
  await expect(effectPager.getByText("Page 2 of 2", { exact: true })).toBeVisible();
  const secondIds = await renderedEffectIds(effectRegion);
  expect([...firstIds, ...secondIds]).toEqual(authoredIds);
  expect(new Set([...firstIds, ...secondIds]).size).toBe(authoredIds.length);
  await expect(effectRegion.locator("tbody tr")).toHaveCount(1);
  await effectPager.getByRole("button", { name: "Previous", exact: true }).click();
  await expect(effectRegion.locator("tbody tr")).toHaveCount(100);
  const changesRegion = dialog.getByRole("region", { name: "Preview changes", exact: true });
  const changesPager = dialog.getByRole("navigation", { name: "Preview changes pages", exact: true });
  await changesPager.getByRole("button", { name: "Next", exact: true }).click();
  await expect(changesRegion.getByRole("row").filter({ hasText: "Canonical existing row" })).toBeVisible();
  await changesPager.getByRole("button", { name: "Previous", exact: true }).click();
  return { firstIds, secondIds, initialScroll, pagedScrollTop, clipboardWrites };
}

async function tabToFooter(page: Page, dialog: Locator, apply: Locator) {
  const tabStops = await dialog.locator("summary, button:not(:disabled), [tabindex='0']").count();
  for (let tabs = 1; tabs <= tabStops + 1; tabs++) {
    await page.keyboard.press("Tab");
    if (await apply.evaluate(element => element === document.activeElement)) return tabs;
  }
  throw new Error(`Apply was not keyboard-reachable within ${tabStops + 1} native Tab presses.`);
}

async function assertResponsivePreview(page: Page, testInfo: TestInfo, dialog: Locator, effectRegion: Locator) {
  const screenshots: string[] = [];
  const measurements = [];
  for (const width of widths) {
    await page.setViewportSize({ width, height: 800 });
    await effectRegion.focus();
    await expect(effectRegion).toBeFocused();
    await expect(effectRegion).toBeInViewport({ ratio: 0.5 });
    const viewport = page.viewportSize()!;
    const dialogBox = await dialog.boundingBox();
    const regionBox = await effectRegion.boundingBox();
    expect(dialogBox).toBeTruthy();
    expect(regionBox).toBeTruthy();
    expect(dialogBox!.width).toBeLessThanOrEqual(viewport.width);
    expect(regionBox!.x).toBeGreaterThanOrEqual(dialogBox!.x - 1);
    expect(regionBox!.x + regionBox!.width).toBeLessThanOrEqual(dialogBox!.x + dialogBox!.width + 1);
    const overflow = await dialog.evaluate(element => ({ scrollWidth: element.scrollWidth, clientWidth: element.clientWidth }));
    expect(overflow.scrollWidth).toBeLessThanOrEqual(overflow.clientWidth + 1);
    const pageOverflow = await page.evaluate(() => ({ scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth }));
    expect(pageOverflow.scrollWidth).toBeLessThanOrEqual(pageOverflow.clientWidth + 1);
    const apply = dialog.getByRole("button", { name: "Apply", exact: true });
    await expect(apply).toBeVisible();
    await expect(apply).toBeEnabled();
    const populatedScreenshot = testInfo.outputPath(`synthetic-effects-${width}-region.png`);
    await page.screenshot({ path: populatedScreenshot, fullPage: true });
    screenshots.push(populatedScreenshot);
    const nativeTabs = await tabToFooter(page, dialog, apply);
    await expect(apply).toBeFocused();
    const applyBox = await apply.boundingBox();
    expect(applyBox).toBeTruthy();
    expect(applyBox!.x).toBeGreaterThanOrEqual(0);
    expect(applyBox!.y).toBeGreaterThanOrEqual(0);
    expect(applyBox!.x + applyBox!.width).toBeLessThanOrEqual(viewport.width);
    expect(applyBox!.y + applyBox!.height).toBeLessThanOrEqual(viewport.height);
    await apply.click({ trial: true });
    measurements.push({ width, viewport, dialogBox, regionBox, applyBox, overflow, pageOverflow, nativeTabs });
    const screenshot = testInfo.outputPath(`synthetic-effects-${width}.png`);
    await page.screenshot({ path: screenshot, fullPage: true });
    screenshots.push(screenshot);
    await expect(dialog).toBeVisible();
  }
  return { screenshots, measurements };
}

test("synthetic action effects preserve ordered typed preview evidence across widths", async ({ page }, testInfo) => {
  await page.addInitScript(() => {
    const writes: string[] = [];
    Object.defineProperty(window, "__effectClipboardWrites", { value: writes });
    if (navigator.clipboard) {
      Object.defineProperty(navigator.clipboard, "writeText", {
        configurable: true, value: async (text: string) => { writes.push(text); },
      });
    }
  });
  const authoredIds = ["effect-absent", "effect-null-text", "effect-union-exact",
    ...Array.from({ length: 98 }, (_, index) => `effect-page-${index}`)];
  const requests: { plan?: unknown; apply?: unknown } = {};

  await installSyntheticRoutes(page, requests);

  await importSave(page, { "PPSA00000/sdimg_E2E/save.json": fixture("save.json") });
  await openJson(page);
  await loadJson(page);
  await page.getByRole("tab", { name: "Overview", exact: true }).click();
  const action = "Reset existing values to imported values";
  await page.getByRole("button", { name: action, exact: true }).click();

  const dialog = page.getByRole("dialog", { name: `Preview: ${action}`, exact: true });
  const effectRegion = dialog.getByRole("region", { name: "Action effects", exact: true });
  const { firstIds, secondIds, initialScroll, pagedScrollTop, clipboardWrites } =
    await assertEffectPreview(dialog, effectRegion, authoredIds, page);
  const { screenshots, measurements } = await assertResponsivePreview(page, testInfo, dialog, effectRegion);

  await page.keyboard.press("Enter");
  await expect(dialog).not.toBeVisible();
  await writeFile(testInfo.outputPath("synthetic-effect-oracles.json"), JSON.stringify({
    authoredIds, requests, screenshots, firstPageRowCount: firstIds.length, secondPageRowCount: secondIds.length,
    renderedEffectIds: [...firstIds, ...secondIds], initialScroll, pagedScrollTop, clipboardWrites, measurements,
  }, null, 2));
  expect(authoredIds).toHaveLength(101);
  expect(requests.plan).toBeTruthy();
  expect(requests.apply).toEqual({ planId: "synthetic-effect-plan", revision: 0 });
  expect(pagedScrollTop).toBeGreaterThan(initialScroll.scrollTop);
});
