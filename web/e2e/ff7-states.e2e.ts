import { test } from "./preset-library-test";
import { expect } from "@playwright/test";
import { actions, capture, fixture, generateFf7Fixtures, openFf7, preview, recordApi, snapshot, unchanged } from "./ff7-helpers";
import { selectedAction, selectRows } from "./ff7-selection-helpers";
import { contained, screenshot } from "./unreal-helpers";
import { waitForApiResponse } from "./apiRoutes";
import { apiRequest } from "./apiRequest";
import { persistentRoot, runtimeFiles } from "./runtimeFiles";

let directory: string;
test.use({ trace: "on", screenshot: "on" });
// Each FF7 case re-imports (a fresh page and in-browser runtime per import) and exports whole saves several times.
test.describe.configure({ timeout: 60_000 });
test.beforeAll(async ({ request }, testInfo) => { expect(request).toBeDefined(); directory = await generateFf7Fixtures(testInfo); });

test("missing inventory and unsupported owned joins expose no clickable candidate", async ({ page }, testInfo) => {
  const flush = recordApi(page, testInfo);
  for (const [sourceName, destinationName, action, tab, status] of [
    ["remake-unique", "remake-missing", actions.restock, "Inventory", "Missing"],
    ["rebirth", "rebirth-duplicate-detail", actions.stars, "Materia", "Unsupported"],
  ] as const) {
    const source = await fixture(directory, sourceName);
    await openFf7(page, source);
    await selectRows(page, tab, [0]);
    await selectedAction(page, action);
    const preset = await capture(page, `FF7 ${status} no choice`);
    const destination = await fixture(directory, destinationName);
    const documentPath = await openFf7(page, destination);
    const before = await snapshot(apiRequest(page), documentPath);
    const result = await preview(page, preset.name);
    expect(result.preview.resolution.instances[0]).toMatchObject({ status, candidates: [] });
    const review = page.locator(".presets-preview");
    await expect(review.getByRole("radio")).toHaveCount(0);
    await expect(review.getByRole("button", { name: "Apply preset", exact: true })).toBeDisabled();
    await expect(review.getByText("No selectable current candidate is available; applying the preset is blocked.", { exact: true })).toBeVisible();
    await unchanged(apiRequest(page), documentPath, before);
    await screenshot(page, testInfo, `ff7-real-${status.toLowerCase()}`);
    await review.getByRole("button", { name: "Cancel", exact: true }).click();
    await unchanged(apiRequest(page), documentPath, before);
  }
  await flush();
});

test("real stale preview rejects apply and retains the capture draft", async ({ page }, testInfo) => {
  const flush = recordApi(page, testInfo);
  const binary = await fixture(directory, "remake");
  const documentPath = await openFf7(page, binary);
  const panel = page.locator(".presets-panel");
  await panel.getByRole("combobox", { name: "Action", exact: true }).selectOption(actions.restock);
  await capture(page, "FF7 stale preview");
  await preview(page, "FF7 stale preview");
  await panel.getByLabel("Name", { exact: true }).fill("Unsaved FF7 draft");
  const editing = await apiRequest(page).post(`${documentPath}/edits`, {
    data: { revision: 0, edits: [{ valueId: "character/1/hp", value: "50" }] },
  });
  expect(editing.status()).toBe(200);
  const before = await snapshot(apiRequest(page), documentPath);
  const applying = waitForApiResponse(page, response => /\/presets\/[^/]+\/apply$/.test(response.url()));
  await panel.getByRole("button", { name: "Apply preset", exact: true }).click();
  expect((await applying).status()).toBe(409);
  await unchanged(apiRequest(page), documentPath, before);
  await expect(panel.getByRole("heading", { name: "Preset preview is stale", exact: true })).toBeVisible();
  await expect(panel.getByRole("button", { name: "Apply preset", exact: true })).toBeDisabled();
  await expect(panel.getByLabel("Name", { exact: true })).toHaveValue("Unsaved FF7 draft");
  await expect(page.getByRole("alert")).toHaveCount(1);
  await screenshot(page, testInfo, "ff7-real-stale-preview");
  await flush();
});

test("actual storage failure preserves draft, history, whole bytes and library; common is read-only", async ({ page }, testInfo) => {
  const flush = recordApi(page, testInfo);
  const binary = await fixture(directory, "rebirth");
  const documentPath = await openFf7(page, binary);
  const panel = page.locator(".presets-panel");
  await panel.getByRole("combobox", { name: "Action", exact: true }).selectOption(actions.ap);
  await panel.getByRole("combobox", { name: "Scope", exact: true }).selectOption("allCopies");
  const captured = await capture(page, "FF7 real all copies");
  expect(captured.actions[0]).toMatchObject({ scopeKind: "allCopies", parameters: { type: String(binary.materiaType), ap: "0" } });
  const allCopies = await preview(page, captured.name);
  expect(allCopies.preview.resolution.instances[0]!.status).toBe("Resolved");
  await panel.getByRole("button", { name: "Cancel", exact: true }).click();
  await panel.getByLabel("Name", { exact: true }).fill("Retain this draft after a real storage error");
  const before = await snapshot(apiRequest(page), documentPath);
  // The runtime's preset store lives in the page's runtime worker. Obstruct the store's atomic write
  // (it writes presets.json.tmp, then renames it) with a directory, leaving the library itself intact.
  const files = await runtimeFiles(page);
  const obstacle = `${persistentRoot}/presets/presets.json.tmp`;
  await files.change({ kind: "mkdir", path: obstacle });
  try {
    const saving = waitForApiResponse(page, response => response.url().endsWith("/presets/capture"));
    await panel.getByRole("button", { name: "Save preset", exact: true }).click();
    expect((await saving).status()).toBe(503);
  } finally {
    await files.change({ kind: "rmdir", path: obstacle });
  }
  await unchanged(apiRequest(page), documentPath, before);
  await expect(panel.getByLabel("Name", { exact: true })).toHaveValue("Retain this draft after a real storage error");
  await expect(panel.getByRole("button", { name: "Retry saving preset", exact: true })).toBeVisible();
  await expect(panel.getByRole("alert")).toHaveCount(1);
  await screenshot(page, testInfo, "ff7-real-storage-error-draft");
  const commonPath = await openFf7(page, binary, binary.commonName);
  await expect(page.locator(".presets-panel").getByRole("button", { name: "Save preset", exact: true })).toBeDisabled();
  const commonBefore = await snapshot(apiRequest(page), commonPath);
  await expect(page.locator(".presets-panel")).toContainText("read-only");
  await contained(page, page.locator(".presets-panel"), 1280);
  await unchanged(apiRequest(page), commonPath, commonBefore);
  await flush();
});
