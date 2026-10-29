import { expect, test } from "@playwright/test";
import { actions, capture, expectFiles, fixture, generateFf7Fixtures, openFf7, patch, preview, recordApi, snapshot, unchanged } from "./ff7-helpers";
import { applyAndHistory, selectedAction, selectRows, withFiles } from "./ff7-selection-helpers";
import { screenshot, tabTo } from "./unreal-helpers";

let directory: string;
test.use({ trace: "on", screenshot: "on" });
test.beforeAll(async ({ request }, testInfo) => { expect(request).toBeDefined(); directory = await generateFf7Fixtures(testInfo); });

test("FF7 selected source identity presents genuine ambiguous candidates and applies only an explicit moved choice", async ({ page }, testInfo) => {
  const flush = recordApi(page, testInfo);
  const source = await fixture(directory, "remake-unique");
  await openFf7(page, source);
  await selectRows(page, "Inventory", [0]);
  await selectedAction(page, actions.restock);
  const quantity = page.locator(".presets-panel").getByRole("textbox", { name: "Target count", exact: true });
  await quantity.fill("99"); await quantity.press("Tab");
  const preset = await capture(page, "FF7 semantic moved inventory");
  expect(preset.actions[0]!.instances).toHaveLength(1);
  expect(preset.actions[0]!.instances![0]!.path ?? null).toBeNull();
  const destination = await fixture(directory, "remake-ambiguous");
  const documentPath = await openFf7(page, destination);
  const before = await snapshot(page.request, documentPath);
  const ambiguous = await preview(page, preset.name);
  const resolution = ambiguous.preview.resolution.instances[0]!;
  expect(resolution.status).toBe("Ambiguous");
  expect(resolution.candidates).toHaveLength(2);
  expect(ambiguous.preview.planId ?? null).toBeNull();
  const review = page.locator(".presets-preview");
  await expect(review.getByRole("radio")).toHaveCount(2);
  expect(await review.getByRole("radio").evaluateAll(nodes => nodes.some(node => (node as HTMLInputElement).checked))).toBe(false);
  await expect(review.getByRole("button", { name: "Apply preset", exact: true })).toBeDisabled();
  await expect(review.getByRole("button", { name: "Preview again", exact: true })).toBeDisabled();
  await unchanged(page.request, documentPath, before);
  const index = resolution.candidates.findIndex(candidate => candidate.path === "inventory/7");
  expect(index).toBeGreaterThanOrEqual(0);
  await tabTo(page, review.getByRole("radio").first());
  for (let candidate = 0; candidate < index; candidate++) await page.keyboard.press("ArrowDown");
  await page.keyboard.press("Space");
  await expect(review.getByRole("radio").nth(index)).toBeChecked();
  await screenshot(page, testInfo, "ff7-genuine-ambiguity-choice");
  const resolving = page.waitForResponse(response => /\/presets\/[^/]+\/preview$/.test(response.url()));
  await review.getByRole("button", { name: "Preview again", exact: true }).click();
  const response = await resolving;
  expect(response.status()).toBe(200);
  expect(response.request().postDataJSON()).toMatchObject({ choices: { [resolution.requested.stableId]: resolution.candidates[index]!.stableId } });
  await expect(review.getByRole("radio")).toHaveCount(0);
  const expected = patch(destination, { "inventory/7/quantity": 99 });
  const downloaded = await applyAndHistory(page, documentPath, destination.files, expected, testInfo);
  const reimported = await openFf7(page, withFiles(destination, downloaded));
  expectFiles((await snapshot(page.request, reimported)).files, expected);
  await flush();
});

test("Rebirth owned native instance follows a moved detail and excludes its orphan", async ({ page }, testInfo) => {
  const flush = recordApi(page, testInfo);
  const source = await fixture(directory, "rebirth");
  await openFf7(page, source);
  await selectRows(page, "Materia", [0]);
  await selectedAction(page, actions.stars);
  const preset = await capture(page, "FF7 owned moved detail");
  const destination = await fixture(directory, "rebirth-moved");
  const documentPath = await openFf7(page, destination);
  const before = await snapshot(page.request, documentPath);
  const result = await preview(page, preset.name);
  expect(result.preview.resolution.instances[0]).toMatchObject({ status: "Resolved", candidates: [{ path: "materia/7" }] });
  expect(result.preview.resolution.instances[0]!.candidates).toHaveLength(1);
  expect(result.preview.plan.changes.map(change => change.valueId).sort()).toEqual(["materia/7/ap", "materia/7/level"]);
  await expect(page.locator(".presets-preview").getByRole("radio")).toHaveCount(0);
  await unchanged(page.request, documentPath, before);
  const expected = patch(destination, { "materia/7/ap": destination.secondTierAp, "materia/7/level": 2 });
  const downloaded = await applyAndHistory(page, documentPath, destination.files, expected, testInfo);
  const reimported = await openFf7(page, withFiles(destination, downloaded));
  expectFiles((await snapshot(page.request, reimported)).files, expected);
  await screenshot(page, testInfo, "ff7-rebirth-moved-owned-reimport");
  await flush();
});

test("selected character1 preserves character0 and explicit empty selection never widens", async ({ page }, testInfo) => {
  const flush = recordApi(page, testInfo);
  const binary = await fixture(directory, "remake");
  const documentPath = await openFf7(page, binary);
  await selectRows(page, "Characters", []);
  await selectedAction(page, actions.recover);
  const emptyRequest = page.waitForRequest(request => request.url().endsWith("/presets/capture"));
  await capture(page, "FF7 selected empty");
  expect((await emptyRequest).postDataJSON()).toMatchObject({ selectedRowKeys: [], scopeKind: "selected", parameters: {} });
  const before = await snapshot(page.request, documentPath);
  const empty = await preview(page, "FF7 selected empty");
  expect(empty.preview.plan.changes).toEqual([]);
  await page.locator(".presets-preview").getByRole("button", { name: "Cancel", exact: true }).click();
  await unchanged(page.request, documentPath, before);
  await selectRows(page, "Characters", [1]);
  await selectedAction(page, actions.recover);
  const selectedRequest = page.waitForRequest(request => request.url().endsWith("/presets/capture"));
  await capture(page, "FF7 character1 recovery");
  expect((await selectedRequest).postDataJSON()).toMatchObject({ selectedRowKeys: ["character/1"], scopeKind: "selected", parameters: {} });
  const result = await preview(page, "FF7 character1 recovery");
  expect(result.preview.plan.changes.map(change => change.valueId).sort()).toEqual(["character/1/hp", "character/1/mp"]);
  const original = Buffer.from(binary.files[binary.gameplayPath]!);
  const expected = patch(binary, {
    "character/1/hp": original.readUInt32LE(binary.fieldOffsets["character/1/max-hp"]!),
    "character/1/mp": original.readUInt32LE(binary.fieldOffsets["character/1/max-mp"]!),
  });
  const downloaded = await applyAndHistory(page, documentPath, binary.files, expected, testInfo);
  const reopened = await openFf7(page, withFiles(binary, downloaded));
  expectFiles((await snapshot(page.request, reopened)).files, expected);
  await flush();
});
