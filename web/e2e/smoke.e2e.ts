import { test, expect, type Page, type TestInfo } from "@playwright/test";
import { readFileSync } from "node:fs";
import { readFile } from "node:fs/promises";
import { zipSync, unzipSync } from "fflate";

const savePath = "PPSA00000/sdimg_E2E/save.json";
const originalSave = readFileSync(new URL("./fixtures/save.json", import.meta.url));
const originalCoins = "12345";
const editedCoins = "54321";
const originalFiles = {
  [savePath]: originalSave,
  "PPSA00000/sdimg_E2E/nested/companion.txt": readFileSync(new URL("./fixtures/companion.txt", import.meta.url)),
  "PPSA00000/sdimg_E2E/empty.dat": new Uint8Array(),
};
const consoleWarning = "Not validated on console. The game may reject or overwrite the edited save.";

test.beforeEach(async ({ page }) => {
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "Open a PS5 save dump" })).toBeVisible();
  const fileChooser = page.waitForEvent("filechooser");
  await page.getByRole("button", { name: "Open zip", exact: true }).click();
  await (await fileChooser).setFiles({
    name: "synthetic-save.zip",
    mimeType: "application/zip",
    buffer: Buffer.from(zipSync(originalFiles)),
  });
  await expect(page.getByRole("heading", { name: "Pick a save file" })).toBeVisible();
  const directories = page.getByRole("navigation", { name: "Save directories" });
  await directories.getByRole("button", { name: / E2E / }).click();
  await directories.getByRole("button", { name: /^save\.json / }).click();
  await page.getByRole("combobox", { name: "Open with" }).selectOption({ label: "Open with Generic JSON" });
  await expect(page.getByRole("heading", { name: "save.json", exact: true })).toBeVisible();
  await page.getByRole("tab", { name: "JSON", exact: true }).click();
  await page.getByRole("button", { name: "Load Entries", exact: true }).click();
  await expect(coinsField(page)).toHaveValue(originalCoins);
});

function coinsField(page: Page) {
  return page.getByRole("textbox", { name: /^coins\s*meaning unknown/ });
}

async function editCoins(page: Page) {
  await coinsField(page).fill(editedCoins);
  await coinsField(page).press("Enter");
  // Wait for the real server mutation, rather than only the input's local draft.
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeEnabled();
  await expect(coinsField(page)).toHaveAccessibleName(/modified/);
}

async function exportFiles(page: Page, testInfo: TestInfo) {
  await page.getByRole("button", { name: /^Export/ }).click();
  const dialog = page.getByRole("dialog", { name: "Export edited saves" });
  await dialog.getByRole("radio", { name: /Whole dump/ }).check();
  await expect(dialog.getByRole("button", { name: "Download zip", exact: true })).toBeEnabled();
  const downloading = page.waitForEvent("download");
  await dialog.getByRole("button", { name: "Download zip", exact: true }).click();
  const download = await downloading;
  expect(download.suggestedFilename()).toMatch(/\.zip$/i);
  const archive = testInfo.outputPath("export.zip");
  await download.saveAs(archive);
  expect(await download.failure()).toBeNull();
  return unzipSync(await readFile(archive));
}

function expectArchiveBytes(files: Record<string, Uint8Array>, expectedSave: Uint8Array) {
  // ZIP metadata/compression can change; every uncompressed payload byte must match.
  expect(Object.keys(files).sort()).toEqual(Object.keys(originalFiles).sort());
  for (const [name, original] of Object.entries(originalFiles)) {
    expect(Buffer.from(files[name]!), name).toEqual(Buffer.from(name === savePath ? expectedSave : original));
  }
}

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
