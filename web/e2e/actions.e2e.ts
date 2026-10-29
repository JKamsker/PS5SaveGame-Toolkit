import { test, expect } from "@playwright/test";
import {
  coinsField, editCoins, editedCoins, expectArchiveBytes, expectFiles, exportFiles, importSave,
  loadJson, openJson, originalCoins, originalSave,
} from "./helpers";

const action = "Reset existing values to imported values";
const editedPlayer = "Synthetic edited player";

test("bulk preview and cancel preserve edits; apply restores exported values as one undo step", async ({ page }, testInfo) => {
  await importSave(page);
  await openJson(page);
  await loadJson(page);
  await editCoins(page);
  const player = page.getByRole("textbox", { name: /^player\s*meaning unknown/ });
  await player.fill(editedPlayer);
  const editing = page.waitForResponse(response => response.url().endsWith("/edits") && response.request().method() === "POST");
  // Generic JSON strings use a multiline editor and commit on blur.
  await player.press("Tab");
  expect((await editing).status()).toBe(200);
  await expect(player).toHaveAccessibleName(/modified/);
  const before = await exportFiles(page, testInfo, "before-preview.zip");
  await page.getByRole("tab", { name: "Overview", exact: true }).click();
  await page.getByRole("button", { name: action, exact: true }).click();
  const preview = page.getByRole("dialog", { name: `Preview: ${action}`, exact: true });
  await expect(preview.getByText("Whole document", { exact: true })).toBeVisible();
  await expect(preview.getByRole("status")).toHaveText("2 field changes · 0 skipped rows · one undo step");
  const table = preview.getByRole("table", { name: "Preview changes", exact: true });
  await expect(table.getByRole("row")).toHaveCount(3);
  const coins = table.getByRole("row").filter({ has: page.getByRole("rowheader", { name: /coins/ }) });
  await expect(coins.getByRole("cell").nth(0)).toHaveText(`number: ${editedCoins}`);
  await expect(coins.getByRole("cell").nth(1)).toHaveText(`number: ${originalCoins}`);
  await expect(preview.getByRole("button", { name: "Apply", exact: true })).toBeEnabled();
  await preview.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(preview).not.toBeVisible();
  const afterCancel = await exportFiles(page, testInfo, "after-cancel.zip");
  // Check all paths and bytes, including fields beyond the preview's first row.
  expectFiles(afterCancel, before);

  await page.getByRole("button", { name: action, exact: true }).click();
  await expect(preview.getByRole("status")).toHaveText("2 field changes · 0 skipped rows · one undo step");
  await preview.getByRole("button", { name: "Apply", exact: true }).click();
  await expect(preview).not.toBeVisible();
  expectArchiveBytes(await exportFiles(page, testInfo, "after-apply.zip"), originalSave);
  await page.getByRole("button", { name: "Undo", exact: true }).click();
  expectFiles(await exportFiles(page, testInfo, "undo-bulk.zip"), before);
  await loadJson(page);
  await expect(coinsField(page)).toHaveValue(editedCoins);
  await expect(player).toHaveValue(editedPlayer);
});
