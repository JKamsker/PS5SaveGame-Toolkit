import { expect, test, type Page, type TestInfo } from "@playwright/test";
import type { ActionPreview } from "../src/api/actionReview";
import type { EditResult } from "../src/api/protocol";
import {
  captionCompanionPath, captionLabel, captionPrimaryPath, captionSfoPath, captionValueId, captionWarning,
  documentSnapshot, editedCaption, expectedGlyphSfo, generateSuckerPunchFixtures, openSuckerPunch,
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

async function previewCaption(page: Page, value = editedCaption, testInfo?: TestInfo, evidenceName?: string) {
  await captionAction(page).click();
  const dialog = page.getByRole("dialog", { name: /Preview: Set save caption/ });
  const input = dialog.getByRole("textbox", { name: "Caption" });
  await expect(input).toHaveValue(fixture.caption);
  await input.fill(value);
  const planning = page.waitForResponse(response => response.url().endsWith("/actions/plan") && response.request().method() === "POST");
  await dialog.getByRole("button", { name: "Preview", exact: true }).click();
  const response = await planning;
  expect(response.ok()).toBe(true);
  const preview = await response.json() as ActionPreview;
  expect(preview.plan.changes).toHaveLength(value === fixture.caption ? 0 : 1);
  if (value !== fixture.caption) {
    expect(preview.plan.changes[0]).toMatchObject({ valueId: captionValueId, label: captionLabel,
      oldValue: fixture.caption, newValue: value, oldExists: true, newExists: true, derived: false });
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

  const noop = await previewCaption(page, fixture.caption, testInfo, "caption-no-op-plan.json");
  await expect(noop.getByRole("status")).toHaveText("No changes. No undo step will be created.");
  await expect(noop.getByRole("button", { name: "Apply", exact: true })).toBeDisabled();
  await noop.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(captionAction(page)).toBeFocused();
  const afterNoop = await documentSnapshot(page, documentPath);
  expect(afterNoop.state).toEqual(before.state);

  const dialog = await previewCaption(page, editedCaption, testInfo, "caption-plan.json");
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

test("real caption apply, Changes revert, undo/redo, all export scopes and reimport preserve bytes", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  const documentPath = await openSuckerPunch(page, fixture.files);
  const dialog = await previewCaption(page);
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
  await page.getByRole("button", { name: "Redo", exact: true }).click();
  await expect(page.getByText("param.sfo SUBTITLE: Moon", { exact: true })).toBeVisible();
  expect((await documentSnapshot(page, documentPath)).state).toMatchObject({ revision: 4, canUndo: true, canRedo: false, isDirty: true });

  await openSuckerPunch(page, everything, "dis.dat", null);
  await expect(page.getByRole("heading", { name: /PPSA02225 · 🌙/ })).toBeVisible();
  await expect(page.getByText(`param.sfo SUBTITLE: ${editedCaption}`, { exact: true })).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath("caption-reimport-1440.png"), fullPage: true });
  await noPageOverflow(page, 1440);
});

test("real stale caption apply preserves Unicode draft and requires an explicit fresh preview", async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 900 });
  const documentPath = await openSuckerPunch(page, fixture.files);
  const dialog = await previewCaption(page);
  const before = await documentSnapshot(page, documentPath);
  const independent = await page.request.post(`${documentPath}/edits`, {
    data: { edits: [{ valueId: captionValueId, value: "Sun" }], revision: before.state.revision },
  });
  expect(independent.status()).toBe(200);
  expect((await independent.json() as EditResult).ok).toBe(true);
  const applying = page.waitForResponse(response => response.url().endsWith("/actions/apply") && response.request().method() === "POST");
  await dialog.getByRole("button", { name: "Apply", exact: true }).click();
  expect((await applying).status()).toBe(409);
  await expect(dialog.getByRole("alert")).toContainText("This preview is stale");
  await expect(dialog.getByRole("alert")).toBeFocused();
  await expect(dialog.getByRole("textbox", { name: "Caption" })).toHaveValue(editedCaption);
  const stale = await documentSnapshot(page, documentPath);
  expect(stale.title).toContain("Sun");
  expect(stale.state).toMatchObject({ revision: before.state.revision! + 1, canUndo: true });
  await expect(page.locator('[role="alert"]')).toHaveCount(1);
  await expect(page.locator(".toast")).toHaveCount(0);
  await noPageOverflow(page, 375);

  await dialog.getByRole("button", { name: "Preview again", exact: true }).click();
  await expect(dialog.getByRole("button", { name: "Apply", exact: true })).toBeEnabled();
  await dialog.getByRole("button", { name: "Apply", exact: true }).click();
  await expect(dialog).not.toBeVisible();
  const after = await documentSnapshot(page, documentPath);
  expect(after.state.revision).toBe(before.state.revision! + 2);
  expect(after.state.canUndo).toBe(true);
});

for (const width of [375, 768, 1024, 1440]) {
  test(`real caption control remains labelled, keyboard reachable and contained at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await openSuckerPunch(page, fixture.files);
    const action = captionAction(page);
    await expect(action).toBeVisible();
    await action.focus();
    await page.keyboard.press("Enter");
    const dialog = page.getByRole("dialog", { name: /Preview: Set save caption/ });
    const input = dialog.getByRole("textbox", { name: "Caption" });
    await expect(dialog.getByRole("heading", { name: /Preview: Set save caption/ })).toBeFocused();
    await page.keyboard.press("Tab");
    await expect(input).toBeFocused();
    await input.fill("🌙");
    await expect(input).toHaveAttribute("aria-invalid", "false");
    await noPageOverflow(page, width);
    await page.screenshot({ path: testInfo.outputPath(`caption-${width}.png`), fullPage: true });
    await page.keyboard.press("Escape");
    await expect(action).toBeFocused();
  });
}

test("missing SFO is a real bounded unavailable caption state", async ({ page }) => {
  const files = { [captionPrimaryPath]: fixture.files[captionPrimaryPath]!, [captionCompanionPath]: fixture.files[captionCompanionPath]! };
  await openSuckerPunch(page, files, "dis.dat", null);
  await expect(page.getByText("Save caption unavailable: this directory has no sce_sys/param.sfo.", { exact: true })).toBeVisible();
  await expect(captionAction(page)).toHaveCount(0);
});
