import { expect, test, type Locator, type Page, type TestInfo } from "@playwright/test";
import type { ActionPreview } from "../src/api/actionReview";
import type { EditResult } from "../src/api/protocol";
import {
  captionCompanionPath, captionLabel, captionPrimaryPath, captionSfoPath, captionValueId, captionWarning,
  documentSnapshot, editedCaption, expectedGlyphSfo, exportCaptionApi, generateSuckerPunchFixtures,
  isCaptionDocumentRequest, openSuckerPunch,
  exportCaptionScope, type SuckerPunchFixture,
} from "./suckerpunch-caption-helpers";
import { expectFiles } from "./helpers";

let fixture: SuckerPunchFixture;
test.beforeAll(async ({ browserName }, testInfo) => {
  expect(browserName).toBe("chromium");
  fixture = await generateSuckerPunchFixtures(testInfo);
});

function captionAction(page: Page) {
  return page.getByRole("button", { name: /Set save caption \(manual, Experimental\)/ });
}

async function previewCaption(page: Page, value = editedCaption, testInfo?: TestInfo, evidenceName?: string,
  documentPath?: string, expectedCurrent = fixture.caption) {
  await captionAction(page).click();
  const dialog = page.getByRole("dialog", { name: /Preview: Set save caption/ });
  const input = dialog.getByRole("textbox", { name: "Caption" });
  await expect(input).toHaveValue(expectedCurrent);
  await input.fill(value);
  const planning = page.waitForResponse(response => response.request().method() === "POST" &&
    (documentPath ? isCaptionDocumentRequest(response.url(), documentPath, "/actions/plan") : response.url().endsWith("/actions/plan")));
  await dialog.getByRole("button", { name: "Preview", exact: true }).click();
  const response = await planning;
  expect(response.ok()).toBe(true);
  const preview = await response.json() as ActionPreview;
  expect(preview.plan.changes).toHaveLength(value === expectedCurrent ? 0 : 1);
  if (value !== expectedCurrent) {
    expect(preview.plan.changes[0]).toMatchObject({ valueId: captionValueId, label: captionLabel,
      oldValue: expectedCurrent, newValue: value, oldExists: true, newExists: true, derived: false });
  }
  if (testInfo && evidenceName) await testInfo.attach(evidenceName, { body: JSON.stringify(preview, null, 2), contentType: "application/json" });
  await expect(dialog.getByText(`${Buffer.byteLength(value, "utf8")} / ${fixture.maxUtf8Bytes} UTF-8 payload bytes.`, { exact: false })).toBeVisible();
  return dialog;
}

async function noPageOverflow(page: Page, width: number) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
  const dialog = page.getByRole("dialog");
  if (await dialog.isVisible()) {
    const bounds = await dialog.boundingBox();
    expect(bounds).not.toBeNull();
    expect(bounds!.x).toBeGreaterThanOrEqual(0);
    expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width);
  }
}

test("real caption preview exposes exact metadata contract, cancel and no-op history", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1024, height: 900 });
  const documentPath = await openSuckerPunch(page, fixture.files);
  const before = await documentSnapshot(page, documentPath);
  expect(before.plugin).toMatchObject({ id: "suckerpunch", maturity: "Experimental" });
  expect(before.state).toMatchObject({ revision: 0, canUndo: false, canRedo: false, isDirty: false });
  await expect(page.getByText("param.sfo SUBTITLE: Moon", { exact: true })).toBeVisible();

  const noop = await previewCaption(page, fixture.caption, testInfo, "caption-no-op-plan.json", documentPath);
  await expect(noop.getByRole("status")).toHaveText("No changes. No undo step will be created.");
  await expect(noop.getByRole("button", { name: "Apply", exact: true })).toBeDisabled();
  await noop.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(captionAction(page)).toBeFocused();
  const afterNoop = await documentSnapshot(page, documentPath);
  expect(afterNoop.state).toEqual(before.state);

  const dialog = await previewCaption(page, editedCaption, testInfo, "caption-plan.json", documentPath);
  await expect(dialog.getByRole("status")).toHaveText("1 field changes · 0 skipped rows · one undo step");
  const row = dialog.getByRole("table", { name: "Preview changes", exact: true }).getByRole("row").nth(1);
  await expect(row.getByRole("rowheader")).toHaveText(captionLabel);
  await expect(row.getByRole("cell").nth(0)).toHaveText("Moon");
  await expect(row.getByRole("cell").nth(1)).toHaveText(editedCaption);
  await expect(dialog.getByText(captionWarning, { exact: false })).toBeVisible();
  await expect(dialog.getByText(/Experimental\/Inferred manual metadata-only override/, { exact: false })).toBeVisible();
  await expect(dialog.getByText(/Preview source SHA-256: [0-9a-f]{64}/, { exact: false })).toBeVisible();
  await expect(dialog.getByText("sce_sys/param.sfo metadata only", { exact: false })).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Apply", exact: true })).toBeEnabled();
  await dialog.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(captionAction(page)).toBeFocused();
  expect((await documentSnapshot(page, documentPath)).state).toEqual(before.state);
  await page.screenshot({ path: testInfo.outputPath("caption-preview-1024.png"), fullPage: true });
  await noPageOverflow(page, 1024);
});

test("real original caption action is one whole-byte undo and redo unit", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1024, height: 900 });
  const documentPath = await openSuckerPunch(page, fixture.files);
  const dialog = await previewCaption(page, editedCaption, undefined, undefined, documentPath);
  const applying = page.waitForResponse(response => response.request().method() === "POST" &&
    isCaptionDocumentRequest(response.url(), documentPath, "/actions/apply"));
  await dialog.getByRole("button", { name: "Apply", exact: true }).click();
  const applied = await applying;
  expect(applied.status()).toBe(200);
  expect((await applied.json() as EditResult).ok).toBe(true);
  const editedFiles = {
    [captionPrimaryPath]: fixture.files[captionPrimaryPath]!,
    [captionSfoPath]: expectedGlyphSfo(fixture.files[captionSfoPath]!),
    [captionCompanionPath]: fixture.files[captionCompanionPath]!,
  };
  expect((await documentSnapshot(page, documentPath)).state).toMatchObject({ revision: 1, canUndo: true, canRedo: false, isDirty: true });
  await expect(page.getByText(`param.sfo SUBTITLE: ${editedCaption}`, { exact: true })).toBeVisible();

  const undoing = page.waitForResponse(response => response.request().method() === "POST" &&
    isCaptionDocumentRequest(response.url(), documentPath, "/undo"));
  await page.getByRole("button", { name: "Undo", exact: true }).click();
  const undone = await undoing;
  expect(undone.status()).toBe(200);
  expect((await undone.json() as EditResult).ok).toBe(true);
  const afterUndo = await documentSnapshot(page, documentPath);
  expect(afterUndo.state).toMatchObject({ revision: 2, canUndo: false, canRedo: true, isDirty: false });
  await expect(page.getByText("param.sfo SUBTITLE: Moon", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeDisabled();
  await expect(page.getByRole("button", { name: "Redo", exact: true })).toBeEnabled();
  expectFiles(await exportCaptionScope(page, testInfo, /Whole dump/, "caption-original-action-undo.zip"), fixture.files);

  const redoing = page.waitForResponse(response => response.request().method() === "POST" &&
    isCaptionDocumentRequest(response.url(), documentPath, "/redo"));
  await page.getByRole("button", { name: "Redo", exact: true }).click();
  const redone = await redoing;
  expect(redone.status()).toBe(200);
  expect((await redone.json() as EditResult).ok).toBe(true);
  const afterRedo = await documentSnapshot(page, documentPath);
  expect(afterRedo.state).toMatchObject({ revision: 3, canUndo: true, canRedo: false, isDirty: true });
  await expect(page.getByText(`param.sfo SUBTITLE: ${editedCaption}`, { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeEnabled();
  await expect(page.getByRole("button", { name: "Redo", exact: true })).toBeDisabled();
  expectFiles(await exportCaptionScope(page, testInfo, /Whole dump/, "caption-original-action-redo.zip"), editedFiles);
});

test("real caption no-op and cancel preserve bytes across clean, dirty and redo states", async ({ page }) => {
  await page.setViewportSize({ width: 1024, height: 900 });
  const documentPath = await openSuckerPunch(page, fixture.files);
  const clean = await documentSnapshot(page, documentPath);
  const cleanBytes = await exportCaptionApi(page, documentPath);
  const cleanNoop = await previewCaption(page, fixture.caption, undefined, undefined, documentPath);
  await expect(cleanNoop.getByRole("status")).toHaveText("No changes. No undo step will be created.");
  await expect(cleanNoop.getByRole("button", { name: "Apply", exact: true })).toBeDisabled();
  await cleanNoop.getByRole("button", { name: "Cancel", exact: true }).click();
  expect((await documentSnapshot(page, documentPath)).state).toEqual(clean.state);
  expectFiles(await exportCaptionApi(page, documentPath), cleanBytes);

  const dirtyDialog = await previewCaption(page, editedCaption, undefined, undefined, documentPath);
  const firstApply = page.waitForResponse(response => response.request().method() === "POST" &&
    isCaptionDocumentRequest(response.url(), documentPath, "/actions/apply"));
  await dirtyDialog.getByRole("button", { name: "Apply", exact: true }).click();
  expect((await firstApply).status()).toBe(200);
  const dirty = await documentSnapshot(page, documentPath);
  const dirtyBytes = await exportCaptionApi(page, documentPath);
  expect(dirty.state).toMatchObject({ canUndo: true, canRedo: false, isDirty: true });

  const serverNoop = await page.request.post(`${documentPath}/edits`, {
    data: { edits: [{ valueId: captionValueId, value: editedCaption }], revision: dirty.state.revision },
  });
  expect(serverNoop.status()).toBe(200);
  const serverNoopResult = await serverNoop.json() as EditResult;
  expect(serverNoopResult).toMatchObject({ ok: true, changes: [], structureChanged: false, state: dirty.state });
  expect((await documentSnapshot(page, documentPath)).state).toEqual(dirty.state);
  expectFiles(await exportCaptionApi(page, documentPath), dirtyBytes);

  const dirtyNoop = await previewCaption(page, editedCaption, undefined, undefined, documentPath, editedCaption);
  await expect(dirtyNoop.getByRole("status")).toHaveText("No changes. No undo step will be created.");
  await expect(dirtyNoop.getByRole("button", { name: "Apply", exact: true })).toBeDisabled();
  await dirtyNoop.getByRole("button", { name: "Cancel", exact: true }).click();
  expect((await documentSnapshot(page, documentPath)).state).toEqual(dirty.state);
  expectFiles(await exportCaptionApi(page, documentPath), dirtyBytes);

  const undoing = page.waitForResponse(response => response.request().method() === "POST" &&
    isCaptionDocumentRequest(response.url(), documentPath, "/undo"));
  await page.getByRole("button", { name: "Undo", exact: true }).click();
  expect((await undoing).status()).toBe(200);
  const afterUndo = await documentSnapshot(page, documentPath);
  expect(afterUndo.state).toMatchObject({ canUndo: false, canRedo: true, isDirty: false });
  const undoBytes = await exportCaptionApi(page, documentPath);
  expectFiles(undoBytes, cleanBytes);

  const redoNoop = await previewCaption(page, fixture.caption, undefined, undefined, documentPath, fixture.caption);
  await expect(redoNoop.getByRole("status")).toHaveText("No changes. No undo step will be created.");
  await expect(redoNoop.getByRole("button", { name: "Apply", exact: true })).toBeDisabled();
  await redoNoop.getByRole("button", { name: "Cancel", exact: true }).click();
  expect((await documentSnapshot(page, documentPath)).state).toEqual(afterUndo.state);
  expectFiles(await exportCaptionApi(page, documentPath), undoBytes);

  const redoing = page.waitForResponse(response => response.request().method() === "POST" &&
    isCaptionDocumentRequest(response.url(), documentPath, "/redo"));
  await page.getByRole("button", { name: "Redo", exact: true }).click();
  expect((await redoing).status()).toBe(200);
  const afterRedo = await documentSnapshot(page, documentPath);
  expect(afterRedo.state).toMatchObject({ canUndo: true, canRedo: false, isDirty: true });
  expectFiles(await exportCaptionApi(page, documentPath), dirtyBytes);
});

test("real caption apply, Changes revert, undo/redo, all export scopes and reimport preserve bytes", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  const documentPath = await openSuckerPunch(page, fixture.files);
  const dialog = await previewCaption(page, editedCaption, undefined, undefined, documentPath);
  const applied = page.waitForResponse(response => response.url().endsWith("/actions/apply") && response.request().method() === "POST");
  await dialog.getByRole("button", { name: "Apply", exact: true }).click();
  expect((await applied).status()).toBe(200);
  await expect(dialog).not.toBeVisible();
  const editedFiles = { [captionPrimaryPath]: fixture.files[captionPrimaryPath]!, [captionSfoPath]: expectedGlyphSfo(fixture.files[captionSfoPath]!), [captionCompanionPath]: fixture.files[captionCompanionPath]! };
  const afterApply = await documentSnapshot(page, documentPath);
  expect(afterApply.state).toMatchObject({ revision: 1, canUndo: true, canRedo: false, isDirty: true });
  expect(afterApply.title).toContain(editedCaption);
  await expect(page.getByText(`param.sfo SUBTITLE: ${editedCaption}`, { exact: true })).toBeVisible();

  const changedFiles = await exportCaptionScope(page, testInfo, /Only changed files/, "caption-changed-files.zip");
  expectFiles(changedFiles, { [captionPrimaryPath]: editedFiles[captionPrimaryPath]!, [captionSfoPath]: editedFiles[captionSfoPath]! });
  const changedDirectories = await exportCaptionScope(page, testInfo, /Changed save folders/, "caption-changed-directories.zip");
  expectFiles(changedDirectories, editedFiles);
  const everything = await exportCaptionScope(page, testInfo, /Whole dump/, "caption-everything.zip");
  expectFiles(everything, editedFiles);

  await page.getByText("Changes against original", { exact: true }).click();
  const changes = page.getByRole("table").filter({ hasText: captionLabel });
  await expect(changes).toContainText(captionLabel);
  await expect(changes).toContainText("Moon");
  await expect(changes).toContainText(editedCaption);
  const revert = changes.getByRole("button", { name: `Revert ${captionLabel}`, exact: true });
  await expect(revert).toBeEnabled();
  await revert.click();
  await expect(page.getByText("No semantic changes.", { exact: true })).toBeVisible();
  expect((await documentSnapshot(page, documentPath)).state).toMatchObject({ revision: 2, canUndo: true, canRedo: false, isDirty: true });
  expectFiles(await exportCaptionScope(page, testInfo, /Whole dump/, "caption-reverted.zip"), fixture.files);

  await page.getByRole("button", { name: "Undo", exact: true }).click();
  await expect(page.getByText(`param.sfo SUBTITLE: ${editedCaption}`, { exact: true })).toBeVisible();
  expect((await documentSnapshot(page, documentPath)).state).toMatchObject({ revision: 3, canUndo: true, canRedo: true, isDirty: true });
  expectFiles(await exportCaptionScope(page, testInfo, /Whole dump/, "caption-revert-undo.zip"), editedFiles);
  await page.getByRole("button", { name: "Redo", exact: true }).click();
  await expect(page.getByText("param.sfo SUBTITLE: Moon", { exact: true })).toBeVisible();
  expect((await documentSnapshot(page, documentPath)).state).toMatchObject({ revision: 4, canUndo: true, canRedo: false, isDirty: true });
  expectFiles(await exportCaptionScope(page, testInfo, /Whole dump/, "caption-revert-redo.zip"), fixture.files);

  await openSuckerPunch(page, everything, "dis.dat", null);
  await expect(page.getByRole("heading", { name: /PPSA02225 · 🌙/ })).toBeVisible();
  await expect(page.getByText(`param.sfo SUBTITLE: ${editedCaption}`, { exact: true })).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath("caption-reimport-1440.png"), fullPage: true });
  await noPageOverflow(page, 1440);
});

test("real stale caption apply preserves Unicode draft and uses the fresh document plan", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 375, height: 900 });
  const documentPath = await openSuckerPunch(page, fixture.files);
  const planRequests: Record<string, unknown>[] = [];
  const applyRequests: Record<string, unknown>[] = [];
  page.on("request", request => {
    if (isCaptionDocumentRequest(request.url(), documentPath, "/actions/plan")) planRequests.push(request.postDataJSON() as Record<string, unknown>);
    if (isCaptionDocumentRequest(request.url(), documentPath, "/actions/apply")) applyRequests.push(request.postDataJSON() as Record<string, unknown>);
  });
  const initialPlanResponse = page.waitForResponse(response => response.request().method() === "POST" &&
    isCaptionDocumentRequest(response.url(), documentPath, "/actions/plan"));
  const dialog = await previewCaption(page, editedCaption, testInfo, "caption-stale-initial-plan.json", documentPath);
  const initialPlan = await initialPlanResponse.then(response => response.json() as Promise<ActionPreview>);
  const before = await documentSnapshot(page, documentPath);
  expect(planRequests).toHaveLength(1);
  expect(planRequests[0]).toMatchObject({ actionId: "metadata/set-save-caption", revision: before.state.revision, parameters: { caption: editedCaption }, scope: {} });
  expect(initialPlan.plan.changes[0]).toMatchObject({ valueId: captionValueId, label: captionLabel, oldValue: fixture.caption, newValue: editedCaption });
  const independent = await page.request.post(`${documentPath}/edits`, {
    data: { edits: [{ valueId: captionValueId, value: "Sun" }], revision: before.state.revision },
  });
  expect(independent.status()).toBe(200);
  expect((await independent.json() as EditResult).ok).toBe(true);
  const applying = page.waitForResponse(response => response.request().method() === "POST" &&
    isCaptionDocumentRequest(response.url(), documentPath, "/actions/apply"));
  await dialog.getByRole("button", { name: "Apply", exact: true }).click();
  const staleApply = await applying;
  expect(staleApply.status()).toBe(409);
  expect(applyRequests).toHaveLength(1);
  expect(applyRequests[0]).toMatchObject({ planId: initialPlan.planId, revision: before.state.revision });
  await expect(dialog.getByRole("alert")).toContainText("This preview is stale");
  await expect(dialog.getByRole("alert")).toBeFocused();
  await expect(dialog.getByRole("textbox", { name: "Caption" })).toHaveValue(editedCaption);
  const stale = await documentSnapshot(page, documentPath);
  expect(stale.title).toContain("Sun");
  expect(stale.state).toMatchObject({ revision: before.state.revision! + 1, canUndo: true });
  await expect(page.locator('[role="alert"]')).toHaveCount(1);
  await expect(page.locator(".toast")).toHaveCount(0);
  await noPageOverflow(page, 375);

  const sunBeforePlan = await documentSnapshot(page, documentPath);
  const sunFilesBeforePlan = await exportCaptionApi(page, documentPath);
  const freshPlanResponse = page.waitForResponse(response => response.request().method() === "POST" &&
    isCaptionDocumentRequest(response.url(), documentPath, "/actions/plan"));
  await dialog.getByRole("button", { name: "Preview again", exact: true }).click();
  const freshPlan = await freshPlanResponse.then(response => response.json() as Promise<ActionPreview>);
  await testInfo.attach("caption-stale-fresh-plan.json", { body: JSON.stringify(freshPlan, null, 2), contentType: "application/json" });
  expect(planRequests).toHaveLength(2);
  expect(planRequests[1]).toMatchObject({ actionId: "metadata/set-save-caption", revision: sunBeforePlan.state.revision,
    parameters: { caption: editedCaption }, scope: {} });
  expect(freshPlan.planId).toBeTruthy();
  expect(freshPlan.planId).not.toBe(initialPlan.planId);
  expect(freshPlan.revision).toBe(sunBeforePlan.state.revision);
  expect(freshPlan.scope).toEqual(initialPlan.scope);
  expect(freshPlan.plan.errors).toEqual([]);
  expect(freshPlan.plan.skippedRows).toEqual([]);
  expect(freshPlan.plan.changes).toEqual([{ valueId: captionValueId, label: captionLabel, oldValue: "Sun", newValue: editedCaption,
    oldExists: true, newExists: true, derived: false }]);
  expect((await documentSnapshot(page, documentPath)).state).toEqual(sunBeforePlan.state);
  expectFiles(await exportCaptionApi(page, documentPath), sunFilesBeforePlan);
  await expect(dialog.getByRole("button", { name: "Apply", exact: true })).toBeEnabled();
  const freshApplying = page.waitForResponse(response => response.request().method() === "POST" &&
    isCaptionDocumentRequest(response.url(), documentPath, "/actions/apply"));
  await dialog.getByRole("button", { name: "Apply", exact: true }).click();
  const applied = await freshApplying;
  expect(applied.status()).toBe(200);
  expect((await applied.json() as EditResult).ok).toBe(true);
  expect(applyRequests).toHaveLength(2);
  expect(applyRequests[1]).toMatchObject({ planId: freshPlan.planId, revision: freshPlan.revision });
  await expect(dialog).not.toBeVisible();
  const after = await documentSnapshot(page, documentPath);
  expect(after.state.revision).toBe(before.state.revision! + 2);
  expect(after.state.canUndo).toBe(true);
  expect(after.title).toContain(editedCaption);
  await expect(page.getByText(`param.sfo SUBTITLE: ${editedCaption}`, { exact: true })).toBeVisible();
  expectFiles(await exportCaptionApi(page, documentPath), {
    [captionPrimaryPath]: fixture.files[captionPrimaryPath]!,
    [captionSfoPath]: expectedGlyphSfo(fixture.files[captionSfoPath]!),
    [captionCompanionPath]: fixture.files[captionCompanionPath]!,
  });
});

async function tabUntil(page: Page, target: Locator, maxTabs = 24) {
  for (let index = 0; index < maxTabs; index++) {
    if (await target.evaluate(element => element === document.activeElement)) return;
    await page.keyboard.press("Tab");
  }
  await expect(target).toBeFocused();
}

for (const width of [375, 768, 1024, 1440]) {
  test(`real caption control is Tab reachable with a populated planned table at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    const documentPath = await openSuckerPunch(page, fixture.files);
    const action = captionAction(page);
    await expect(action).toBeVisible();
    const stableFocus = page.getByRole("tab", { name: "Overview", exact: true });
    await stableFocus.focus();
    await tabUntil(page, action);
    await page.keyboard.press("Enter");
    const dialog = page.getByRole("dialog", { name: /Preview: Set save caption/ });
    const heading = dialog.getByRole("heading", { name: /Preview: Set save caption/ });
    const input = dialog.getByRole("textbox", { name: "Caption" });
    await expect(heading).toBeFocused();
    await page.keyboard.press("Tab");
    await expect(input).toBeFocused();
    await input.fill(editedCaption);
    await expect(input).toHaveAttribute("aria-invalid", "false");
    const planning = page.waitForResponse(response => response.request().method() === "POST" &&
      isCaptionDocumentRequest(response.url(), documentPath, "/actions/plan"));
    await page.keyboard.press("Tab");
    const cancel = dialog.getByRole("button", { name: "Cancel", exact: true });
    await expect(cancel).toBeFocused();
    await page.keyboard.press("Tab");
    const preview = dialog.getByRole("button", { name: "Preview", exact: true });
    await expect(preview).toBeFocused();
    await page.keyboard.press("Enter");
    expect((await planning).ok()).toBe(true);
    const region = dialog.getByRole("region", { name: "Preview changes", exact: true });
    const table = dialog.getByRole("table", { name: "Preview changes", exact: true });
    const footer = dialog.locator(".dialog__footer");
    await expect(region).toBeVisible();
    await expect(table).toContainText(captionLabel);
    await expect(table).toContainText(editedCaption);
    await expect(footer).toBeVisible();
    await footer.getByRole("button", { name: "Apply", exact: true }).scrollIntoViewIfNeeded();
    await expect(footer.getByRole("button", { name: "Apply", exact: true })).toBeInViewport();
    await noPageOverflow(page, width);
    await page.screenshot({ path: testInfo.outputPath(`caption-planned-${width}.png`), fullPage: true });
    await tabUntil(page, footer.getByRole("button", { name: "Cancel", exact: true }));
    await tabUntil(page, footer.getByRole("button", { name: "Change parameters", exact: true }));
    await tabUntil(page, footer.getByRole("button", { name: "Apply", exact: true }));
    await page.keyboard.press("Escape");
    await expect(action).toBeFocused();

    await page.keyboard.press("Enter");
    await expect(heading).toBeFocused();
    await page.keyboard.press("Tab");
    await expect(input).toBeFocused();
    await page.keyboard.press("Tab");
    await expect(cancel).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(action).toBeFocused();
  });
}

test("missing SFO is a real bounded unavailable caption state", async ({ page }) => {
  const files = { [captionPrimaryPath]: fixture.files[captionPrimaryPath]!, [captionCompanionPath]: fixture.files[captionCompanionPath]! };
  await openSuckerPunch(page, files, "dis.dat", null);
  await expect(page.getByText("Save caption unavailable: this directory has no sce_sys/param.sfo.", { exact: true })).toBeVisible();
  await expect(captionAction(page)).toHaveCount(0);
});
