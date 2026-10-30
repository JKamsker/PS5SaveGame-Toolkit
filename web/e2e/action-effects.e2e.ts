import { test, expect } from "@playwright/test";
import { writeFile } from "node:fs/promises";
import { fixture, importSave, loadJson, openJson } from "./helpers";

const widths = [375, 768, 1024, 1440] as const;
const longPayload = "exact-long-" + "Z".repeat(700);

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
    ];
    const plan = { ...original, planId: "synthetic-effect-plan", stale: false,
      plan: { ...original.plan, changes, skippedRows: [],
        warnings: ["Synthetic fixture only; no production provider claim"], errors: [] } };
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

  await importSave(page, { "PPSA00000/sdimg_E2E/save.json": fixture("save.json") });
  await openJson(page);
  await loadJson(page);
  await page.getByRole("tab", { name: "Overview", exact: true }).click();
  const action = "Reset existing values to imported values";
  await page.getByRole("button", { name: action, exact: true }).click();

  const dialog = page.getByRole("dialog", { name: `Preview: ${action}`, exact: true });
  await expect(dialog.getByRole("status")).toHaveText("101 field changes · 0 skipped rows · one undo step");
  await expect(dialog.getByText("101 action effects · 101 total ordered rows · one whole-action undo", { exact: true })).toBeVisible();
  await expect(dialog.getByText("Synthetic fixture only; no production provider claim", { exact: true })).toBeVisible();
  const effectRegion = dialog.getByRole("region", { name: "Action effects", exact: true });
  await expect(effectRegion).toHaveAttribute("tabindex", "0");
  await expect(effectRegion.getByRole("row").filter({ hasText: "Effect absent" })).toBeVisible();
  await expect(effectRegion.getByText("StructuralMemento · effect-absent", { exact: false })).toBeVisible();
  await expect(effectRegion.getByText("Synthetic warning", { exact: true })).toBeVisible();
  await expect(effectRegion.getByRole("row").filter({ hasText: "synthetic renderer fixture" }).first()).toBeVisible();
  await expect(effectRegion.getByText("(absent)", { exact: true }).first()).toBeVisible();
  await expect(effectRegion.getByText("null (explicit)", { exact: true }).first()).toBeVisible();
  await expect(effectRegion.getByText("text: null", { exact: true }).first()).toBeVisible();
  await expect(effectRegion.getByText("count: integer: 9007199254740993", { exact: true }).first()).toBeVisible();

  await effectRegion.focus();
  await page.keyboard.press("PageDown");
  const scrollTop = await effectRegion.evaluate(element => element.scrollTop);
  const longDetails = effectRegion.locator("details").nth(1);
  await longDetails.locator("summary").click();
  await longDetails.getByRole("button", { name: "Copy exact", exact: true }).click();
  const clipboardWrites = await page.evaluate(() => (window as unknown as { __effectClipboardWrites: string[] }).__effectClipboardWrites);
  expect(clipboardWrites).toContain(`text: ${longPayload}`);

  const effectPager = dialog.getByRole("navigation", { name: "Action effects pages", exact: true });
  await expect(effectPager.getByRole("button", { name: "Next", exact: true })).toBeVisible();
  await effectPager.getByRole("button", { name: "Next", exact: true }).click();
  await expect(effectRegion.getByRole("row").filter({ hasText: "Effect page 97" })).toBeVisible();

  const screenshots: string[] = [];
  for (const width of widths) {
    await page.setViewportSize({ width, height: 800 });
    const screenshot = testInfo.outputPath(`synthetic-effects-${width}.png`);
    await page.screenshot({ path: screenshot, fullPage: true });
    screenshots.push(screenshot);
    await expect(dialog).toBeVisible();
  }

  await dialog.getByRole("button", { name: "Apply", exact: true }).click();
  await expect(dialog).not.toBeVisible();
  await writeFile(testInfo.outputPath("synthetic-effect-oracles.json"), JSON.stringify({
    authoredIds, requests, screenshots, firstPageRowCount: 101, scrollTop,
  }, null, 2));
  expect(authoredIds).toHaveLength(101);
  expect(requests.plan).toBeTruthy();
  expect(requests.apply).toEqual({ planId: "synthetic-effect-plan", revision: 0 });
  expect(scrollTop).toBeGreaterThanOrEqual(0);
});
