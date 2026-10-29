import { test, expect } from "@playwright/test";
import {
  coinsField, editCoins, editedCoins, expectArchiveBytes, expectFiles, exportFiles,
  fixture, importSave, loadJson, openJson, originalCoins, originalFiles, originalSave,
  profilePath, savePath, withCoins,
} from "./helpers";

const profile = fixture("profile.json");
const multiFiles = { ...originalFiles, [profilePath]: profile };
const profileCoins = "77";
const editedProfileCoins = "88";

test("document switching keeps edits and undo/redo histories separate", async ({ page }, testInfo) => {
  await importSave(page, multiFiles);
  await openJson(page);
  await loadJson(page);
  await editCoins(page);

  await openJson(page, "profile.json");
  await loadJson(page);
  await expect(coinsField(page)).toHaveValue(profileCoins);
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeDisabled();
  await editCoins(page, editedProfileCoins);
  await page.getByRole("button", { name: "Undo", exact: true }).click();
  await expect(coinsField(page)).toHaveValue(profileCoins);
  await expect(page.getByRole("button", { name: "Redo", exact: true })).toBeEnabled();

  await openJson(page);
  await loadJson(page);
  await expect(coinsField(page)).toHaveValue(editedCoins);
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeEnabled();
  await expect(page.getByRole("button", { name: "Redo", exact: true })).toBeDisabled();

  await openJson(page, "profile.json");
  await loadJson(page);
  await expect(coinsField(page)).toHaveValue(profileCoins);
  await page.getByRole("button", { name: "Redo", exact: true }).click();
  await expect(coinsField(page)).toHaveValue(editedProfileCoins);
  await expect(page.getByRole("button", { name: "Redo", exact: true })).toBeDisabled();
  expectFiles(await exportFiles(page, testInfo), {
    ...multiFiles,
    [savePath]: withCoins(originalSave, originalCoins, editedCoins),
    [profilePath]: withCoins(profile, profileCoins, editedProfileCoins),
  });
});

test("an in-flight edit cannot replace another document's values or history", async ({ page }, testInfo) => {
  await importSave(page, multiFiles);
  await openJson(page);
  await loadJson(page);
  let release!: () => void;
  const released = new Promise<void>(resolve => { release = resolve; });
  let acknowledge!: () => void;
  const acknowledged = new Promise<void>(resolve => { acknowledge = resolve; });
  await page.route("**/documents/*/edits", async route => {
    // Execute the real mutation, but hold its response while the user switches files.
    const response = await route.fetch();
    expect(response.status()).toBe(200);
    acknowledge();
    await released;
    await route.fulfill({ response });
  }, { times: 1 });
  try {
    await coinsField(page).fill(editedCoins);
    await coinsField(page).press("Enter");
    await acknowledged;
    await openJson(page, "profile.json");
    await loadJson(page);
    await editCoins(page, editedProfileCoins);
    release();
    await page.unrouteAll({ behavior: "wait" });
    await expect(coinsField(page)).toHaveValue(editedProfileCoins);
    await page.getByRole("button", { name: "Undo", exact: true }).click();
    await expect(coinsField(page)).toHaveValue(profileCoins);
    await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeDisabled();
    await openJson(page);
    await loadJson(page);
    await expect(coinsField(page)).toHaveValue(editedCoins);
    expectFiles(await exportFiles(page, testInfo), {
      ...multiFiles, [savePath]: withCoins(originalSave, originalCoins, editedCoins),
    });
  } finally {
    release();
    await page.unrouteAll({ behavior: "wait" });
  }
});

test("repeated exports preserve every payload byte and leave edits undoable", async ({ page }, testInfo) => {
  await importSave(page);
  await openJson(page);
  await loadJson(page);
  await editCoins(page);
  const first = await exportFiles(page, testInfo, "first.zip");
  const second = await exportFiles(page, testInfo, "second.zip");
  expectArchiveBytes(first, withCoins(originalSave, originalCoins, editedCoins));
  expectFiles(second, first);
  await expect(coinsField(page)).toHaveValue(editedCoins);
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeEnabled();
  await page.getByRole("button", { name: "Undo", exact: true }).click();
  await expect(coinsField(page)).toHaveValue(originalCoins);
  expectArchiveBytes(await exportFiles(page, testInfo, "after-undo.zip"), originalSave);
});
