import { expect, test, type Page, type TestInfo } from "@playwright/test";
import type { ActionPreview } from "../src/api/actionReview";
import type { EditResult } from "../src/api/protocol";
import {
  captionSfoPath, captionValueId, documentSnapshot, editedCaption, expectedGlyphSfo,
  exportCaptionApi, generateSuckerPunchFixtures, isCaptionDocumentRequest, openSuckerPunch, type SuckerPunchFixture,
} from "./suckerpunch-caption-helpers";
import { expectFiles } from "./helpers";

let fixture: SuckerPunchFixture;
test.beforeAll(async ({ browserName }, testInfo) => {
  expect(browserName).toBe("chromium");
  fixture = await generateSuckerPunchFixtures(testInfo);
});

const nativeCases = [
  { name: "literal-feff", value: "\uFEFF", bytes: 3 },
  { name: "empty", value: "", bytes: 0 },
  { name: "exact-31", value: "a".repeat(31), bytes: 31 },
  { name: "over-32", value: "a".repeat(32), bytes: 32 },
] as const;
const rejectedCases = [
  { name: "over-32", value: "a".repeat(32), reason: "At most 31 UTF-8 bytes." },
  { name: "embedded-nul", value: "a\0b", reason: "NUL characters are not allowed." },
  { name: "lone-surrogate", value: "\uD800", reason: "Text must contain valid Unicode scalar sequences." },
  { name: "carriage-return", value: "a\rb", reason: "Line breaks are not allowed." },
  { name: "line-feed", value: "a\nb", reason: "Line breaks are not allowed." },
] as const;

function action(page: Page) { return page.getByRole("button", { name: /Set save caption \(manual, Experimental\)/ }); }
function dialog(page: Page) { return page.getByRole("dialog", { name: /Preview: Set save caption/ }); }

async function responseTo(page: Page, documentPath: string, suffix: string, activate: () => Promise<unknown>) {
  const pending = page.waitForResponse(response => response.request().method() === "POST" &&
    isCaptionDocumentRequest(response.url(), documentPath, suffix));
  await activate();
  return pending;
}

async function applyCaption(page: Page, documentPath: string) {
  const applied = await responseTo(page, documentPath, "/actions/apply", () => dialog(page).getByRole("button", { name: "Apply", exact: true }).click());
  expect(applied.status()).toBe(200);
  expect((await applied.json() as EditResult).ok).toBe(true);
  await expect(dialog(page)).not.toBeVisible();
  const edited = await documentSnapshot(page, documentPath);
  expect(edited.state).toMatchObject({ canUndo: true, canRedo: false, isDirty: true });
  return edited;
}

async function undoCaption(page: Page, documentPath: string) {
  const response = await responseTo(page, documentPath, "/undo", () => page.getByRole("button", { name: "Undo", exact: true }).click());
  expect(response.status()).toBe(200);
  expect((await response.json() as EditResult).ok).toBe(true);
  await expect(page.getByText("param.sfo SUBTITLE: Moon", { exact: true })).toBeVisible();
  expect((await documentSnapshot(page, documentPath)).state).toMatchObject({ canUndo: false, canRedo: true, isDirty: false });
}

async function nativeBoundary(page: Page, documentPath: string, boundary: typeof nativeCases[number], testInfo: TestInfo) {
  const before = await documentSnapshot(page, documentPath);
  await action(page).click();
  const input = dialog(page).getByRole("textbox", { name: "Caption" });
  await expect(input).toHaveValue(fixture.caption);
  await input.fill(boundary.value);
  await input.press("Tab"); // Commit the native draft using the actual consumer blur path.
  await expect(input).toHaveValue(boundary.value);
  const remaining = Math.abs(fixture.maxUtf8Bytes - boundary.bytes);
  const suffix = boundary.bytes > fixture.maxUtf8Bytes ? "over capacity" : "remaining";
  await expect(dialog(page).getByText(`${boundary.bytes} / 31 UTF-8 bytes used · ${remaining} ${remaining === 1 ? "byte" : "bytes"} ${suffix}`, { exact: true })).toBeVisible();
  const preview = dialog(page).getByRole("button", { name: "Preview", exact: true });
  await expect(input).toHaveAttribute("aria-invalid", String(boundary.bytes > fixture.maxUtf8Bytes));
  if (boundary.bytes > fixture.maxUtf8Bytes) {
    await expect(preview).toBeDisabled();
    await expect(dialog(page).getByRole("alert")).toHaveText("At most 31 UTF-8 bytes.");
    await page.screenshot({ path: testInfo.outputPath("caption-native-32-retained.png"), fullPage: true });
    await dialog(page).getByRole("button", { name: "Cancel", exact: true }).click();
  } else {
    await expect(preview).toBeEnabled();
    const response = await responseTo(page, documentPath, "/actions/plan", () => preview.click());
    expect(response.status()).toBe(200);
    const body = await response.json() as ActionPreview;
    await testInfo.attach(`caption-native-${boundary.name}.json`, { body: JSON.stringify(body, null, 2), contentType: "application/json" });
    expect(body.plan.changes).toHaveLength(1);
    expect(body.plan.changes[0]).toMatchObject({ valueId: captionValueId, oldValue: fixture.caption, newValue: boundary.value });
    expect(body.plan.errors).toEqual([]);
    expect((await documentSnapshot(page, documentPath)).state).toEqual(before.state);
    expectFiles(await exportCaptionApi(page, documentPath), fixture.files);
    await applyCaption(page, documentPath);
    await action(page).click();
    await expect(dialog(page).getByRole("textbox", { name: "Caption" })).toHaveValue(boundary.value);
    await dialog(page).getByRole("button", { name: "Cancel", exact: true }).click();
    await undoCaption(page, documentPath);
  }
  const after = await documentSnapshot(page, documentPath);
  if (boundary.bytes > fixture.maxUtf8Bytes) expect(after.state).toEqual(before.state);
  expectFiles(await exportCaptionApi(page, documentPath, testInfo, `caption-native-${boundary.name}-after.zip`), fixture.files);
}

test("real caption native UTF-8 drafts, counters and accepted values are exact without truncation", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 768, height: 900 });
  const documentPath = await openSuckerPunch(page, fixture.files);
  const plans: string[] = [];
  page.on("request", request => { if (isCaptionDocumentRequest(request.url(), documentPath, "/actions/plan")) plans.push(request.url()); });
  for (const boundary of nativeCases) await nativeBoundary(page, documentPath, boundary, testInfo);
  expect(plans).toHaveLength(3); // The invalid 32-byte native draft never requests a token.
});

test("real caption JSON boundary values preserve canonical diagnostics and bytes", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 768, height: 900 });
  const documentPath = await openSuckerPunch(page, fixture.files);
  await action(page).click();
  await dialog(page).getByRole("textbox", { name: "Caption" }).fill(editedCaption);
  await responseTo(page, documentPath, "/actions/plan", () => dialog(page).getByRole("button", { name: "Preview", exact: true }).click());
  await applyCaption(page, documentPath);
  const editedFiles = { ...fixture.files, [captionSfoPath]: expectedGlyphSfo(fixture.files[captionSfoPath]!) };
  expectFiles(await exportCaptionApi(page, documentPath), editedFiles);
  await undoCaption(page, documentPath);
  const before = await documentSnapshot(page, documentPath);
  expect(before.state).toMatchObject({ revision: 2, canUndo: false, canRedo: true, isDirty: false });
  const beforeFiles = await exportCaptionApi(page, documentPath, testInfo, "caption-boundary-before.zip");
  expectFiles(beforeFiles, fixture.files);
  // Raw JSON preserves values that single-line HTML sanitizes or cannot meaningfully fill.
  for (const boundary of rejectedCases) {
    const response = await page.request.post(`${documentPath}/actions/plan`, {
      data: { actionId: "metadata/set-save-caption", parameters: { caption: boundary.value }, scope: null, revision: before.state.revision },
    });
    expect(response.status()).toBe(422);
    const body = await response.json() as ActionPreview;
    await testInfo.attach(`caption-boundary-${boundary.name}.json`, { body: JSON.stringify(body, null, 2), contentType: "application/json" });
    expect(body.planId).toBeUndefined();
    expect(body.plan.changes).toEqual([]);
    expect(body.plan.errors).toEqual([{ valueId: "caption", message: boundary.reason }]);
    expect((await documentSnapshot(page, documentPath)).state).toEqual(before.state);
    expectFiles(await exportCaptionApi(page, documentPath, testInfo, `caption-boundary-${boundary.name}-after.zip`), beforeFiles);
  }
  const redone = await responseTo(page, documentPath, "/redo", () => page.getByRole("button", { name: "Redo", exact: true }).click());
  expect(redone.status()).toBe(200);
  expect((await redone.json() as EditResult).ok).toBe(true);
  expect((await documentSnapshot(page, documentPath)).state).toMatchObject({ revision: 3, canUndo: true, canRedo: false, isDirty: true });
  await expect(page.getByText(`param.sfo SUBTITLE: ${editedCaption}`, { exact: true })).toBeVisible();
  expectFiles(await exportCaptionApi(page, documentPath, testInfo, "caption-boundary-redone.zip"), editedFiles);
});
