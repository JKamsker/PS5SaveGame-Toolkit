import { test, expect } from "@playwright/test";
import {
  coinsField, editCoins, editedCoins, expectArchiveBytes, exportFiles,
  importSave, loadJson, openJson, originalCoins, originalSave, savePath,
} from "./helpers";

const consoleWarning = "Not validated on console. The game may reject or overwrite the edited save.";

test.beforeEach(async ({ page }) => {
  await importSave(page);
  await openJson(page);
  await loadJson(page);
  await expect(coinsField(page)).toHaveValue(originalCoins);
});
test("imports, edits, reviews and exports without changing any other bytes", async ({ page }, testInfo) => {
  await editCoins(page);
  await page.getByText("Changes against original", { exact: true }).click();
  const review = page.getByRole("table");
  await expect(review.getByRole("row")).toHaveCount(2);
  const change = review.getByRole("row").filter({ has: page.getByRole("rowheader", { name: /^coins/ }) });
  await expect(change.getByRole("cell").nth(0)).toHaveText(`number: ${originalCoins}`);
  await expect(change.getByRole("cell").nth(1)).toHaveText(`number: ${editedCoins}`);
  // The reviewed field must still display its unknown-confidence label and console warning.
  await expect(coinsField(page)).toHaveAccessibleName(/meaning unknown/);
  await expect(coinsField(page)).toHaveAccessibleDescription(new RegExp(consoleWarning.replaceAll(".", "\\.")));
  const field = page.locator(".field").filter({ has: coinsField(page) });
  await expect(field.getByText("meaning unknown", { exact: true })).toBeVisible();
  await expect(field.getByText(consoleWarning, { exact: false })).toBeVisible();

  const files = await exportFiles(page, testInfo);
  expect(JSON.parse(Buffer.from(files[savePath]!).toString("utf8")).coins).toBe(Number(editedCoins));
  const expectedSave = Buffer.from(originalSave.toString("utf8").replace(`"coins" : ${originalCoins}`, `"coins" : ${editedCoins}`));
  expectArchiveBytes(files, expectedSave);
});

test("undo restores the original field and every exported byte", async ({ page }, testInfo) => {
  await editCoins(page);
  await page.getByRole("button", { name: "Undo", exact: true }).click();
  await expect(coinsField(page)).toHaveValue(originalCoins);
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeDisabled();
  await expect(page.getByRole("button", { name: "Redo", exact: true })).toBeEnabled();
  await page.getByText("Changes against original", { exact: true }).click();
  await expect(page.getByText("No semantic changes.", { exact: true })).toBeVisible();
  expectArchiveBytes(await exportFiles(page, testInfo), originalSave);
});
