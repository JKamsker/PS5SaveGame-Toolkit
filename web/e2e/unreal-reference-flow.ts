import { expect, type Page, type Request, type TestInfo } from "@playwright/test";
import { readFile } from "node:fs/promises";
import type { EditResult, SaveDocument } from "../src/api/protocol";
import { expectFiles, exportFiles } from "./helpers";
import type { SemanticChangeLog } from "../src/api/actionReview";
import { brightnessName, consoleWarning, screenshot, showSearch, tabTo } from "./unreal-helpers";

export async function editBrightness(page: Page): Promise<EditResult> {
  const writes: string[] = [];
  page.on("request", request => { if (request.method() === "POST" && request.url().endsWith("/edits")) writes.push(request.postData() ?? ""); });
  await showSearch(page);
  const field = page.getByRole("textbox", { name: brightnessName });
  await expect(field).toHaveValue("1.25");
  await expect(field).toHaveAccessibleDescription(consoleWarning + ". Raw serialized property; the name and storage type are confirmed by its tag.");
  await tabTo(page, field);
  await field.fill("not-a-number");
  await page.keyboard.press("Enter");
  await expect(field).toHaveAttribute("aria-invalid", "true");
  await page.keyboard.press("Escape");
  await expect(field).toHaveValue("1.25");
  await expect(field).toHaveAttribute("aria-invalid", "false");
  expect(writes).toEqual([]);
  await field.fill("1.75");
  const editing = page.waitForResponse(response => response.url().endsWith("/edits") && response.request().method() === "POST");
  await page.keyboard.press("Enter");
  const result = await (await editing).json() as EditResult;
  expect(result.ok).toBe(true);
  expect(result.changes).toEqual([{ valueId: "property/0", oldValue: 1.25, newValue: 1.75 }]);
  await expect(field).toBeFocused();
  await expect(field).toHaveValue("1.75");
  expect(writes).toHaveLength(1);
  return result;
}

export async function reviewBrightness(page: Page, documentPath: string, testInfo: TestInfo): Promise<SemanticChangeLog> {
  await tabTo(page, page.locator(".changes-panel summary"));
  await page.keyboard.press("Enter");
  const review = page.locator(".changes-panel");
  await expect(review.getByRole("searchbox", { name: "Find a changed field" })).toBeVisible();
  await expect(review.getByRole("status", { name: "Changes review" })).toHaveText("1 of 1 changed fields");
  const response = await page.request.get(`${documentPath}/changes`);
  expect(response.ok()).toBe(true);
  const log = await response.json() as SemanticChangeLog;
  expect(log.changes).toHaveLength(1);
  expect(log.changes[0]).toMatchObject({ valueId: "property/0", originalValue: 1.25, currentValue: 1.75 });
  const change = log.changes[0]!;
  const changed = review.getByRole("row").filter({ has: page.getByRole("rowheader") });
  await expect(changed).toHaveCount(1);
  await expect(changed.getByRole("rowheader")).toContainText(change.label);
  await expect(changed.getByRole("cell").nth(0)).toHaveText("1.25");
  await expect(changed.getByRole("cell").nth(1)).toHaveText("1.75");
  const partial = review.getByText(/Review coverage is incomplete/);
  if (log.complete) await expect(partial).toHaveCount(0);
  else await expect(partial).toBeVisible();
  const jump = changed.getByRole("button", { name: /^Jump to/ });
  if (change.currentExists && (change.target || change.path.length > 0)) await expect(jump).toBeEnabled();
  else await expect(jump).toBeDisabled();
  const revert = changed.getByRole("button", { name: /^Revert/ });
  if (change.canRevert) await expect(revert).toBeEnabled();
  else await expect(revert).toBeDisabled();
  await screenshot(page, testInfo, "real-unreal-review");
  return log;
}

export async function downloadBrightness(page: Page, testInfo: TestInfo, expected: Buffer): Promise<void> {
  const downloading = page.waitForEvent("download");
  await page.getByRole("link", { name: "Download file", exact: true }).click();
  const download = await downloading;
  const destination = testInfo.outputPath("settings-download.sav");
  await download.saveAs(destination);
  expect(await download.failure()).toBeNull();
  expect(await readFile(destination)).toEqual(expected);
}

export async function proveScalarNoOp(page: Page, documentPath: string, value: number, testInfo: TestInfo,
  stage: string, expectedFiles: Record<string, Buffer>): Promise<void> {
  const before = await (await page.request.get(documentPath)).json() as SaveDocument;
  const response = await page.request.post(`${documentPath}/edits`, {
    data: { edits: [{ valueId: "property/0", value }], revision: before.state.revision },
  });
  expect(response.ok()).toBe(true);
  const result = await response.json() as EditResult;
  expect(result.ok).toBe(true);
  expect(result.changes).toEqual([]);
  expect(result.state).toEqual(before.state);
  const after = await (await page.request.get(documentPath)).json() as SaveDocument;
  expect(after.state).toEqual(before.state);
  await testInfo.attach(`real-api-noop-${stage}.json`, {
    body: JSON.stringify({ before, result, after }, null, 2), contentType: "application/json",
  });
  expectFiles(await exportFiles(page, testInfo, `noop-${stage}.zip`), expectedFiles);
}

export async function proveUnchangedDraft(page: Page, documentPath: string): Promise<void> {
  const before = await (await page.request.get(documentPath)).json() as SaveDocument;
  const writes: string[] = [];
  const record = (request: Request) => {
    if (request.method() === "POST" && request.url().endsWith("/edits")) writes.push(request.url());
  };
  page.on("request", record);
  try {
    const field = page.getByRole("textbox", { name: brightnessName });
    await field.fill("1.75");
    await field.press("Enter");
    const after = await (await page.request.get(documentPath)).json() as SaveDocument;
    expect(after.state).toEqual(before.state);
    expect(writes).toEqual([]);
  } finally { page.off("request", record); }
}
