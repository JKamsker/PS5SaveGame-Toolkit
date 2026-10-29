import { test, expect } from "@playwright/test";
import {
  archive, coinsField, expectFiles, exportFiles, fixture, home, loadJson,
  openJson, originalCoins, upload,
} from "./helpers";

const invalidArchives = [
  {
    name: "not-a-zip.zip",
    bytes: () => fixture("companion.txt"),
    error: /Could not import archive 'not-a-zip\.zip':.*(central directory|ZIP|zip)/i,
  },
  {
    name: "truncated.zip",
    // Cut a generated, valid ZIP inside its local header, before the directory record.
    bytes: () => archive().subarray(0, 16),
    error: /Could not import archive 'truncated\.zip':.*empty or truncated.*end-of-central-directory/i,
  },
  {
    name: "empty.zip",
    bytes: () => archive({}),
    error: /No save files found in the upload\./,
  },
];

for (const invalid of invalidArchives) {
  test(`rejects ${invalid.name} with a specific error and recovers on the next import`, async ({ page }) => {
    await home(page);
    const importing = page.waitForResponse(response => response.url().endsWith("/api/workspaces") && response.request().method() === "POST");
    await upload(page, invalid.bytes(), invalid.name);
    expect((await importing).status()).toBe(422);
    await expect(page.getByRole("alert")).toHaveText(invalid.error);
    await expect(page.getByRole("button", { name: "Retry import", exact: true })).toBeEnabled();
    await expect(page.getByRole("button", { name: "Open zip", exact: true })).toBeEnabled();
    // Recover in the same page, without a reload or clearing browser state.
    await upload(page);
    await expect(page.getByRole("heading", { name: "Pick a save file" })).toBeVisible();
    await page.getByRole("navigation", { name: "Save directories" }).getByRole("button", { name: / E2E / }).click();
    await openJson(page);
    await loadJson(page);
    await expect(coinsField(page)).toHaveValue(originalCoins);
    await expect(page.getByRole("alert")).toHaveCount(0);
  });
}

test("an unknown layout opens read-only in the raw file inspector", async ({ page }) => {
  const files = { "unrecognized-layout/mystery.dat": fixture("unknown.txt") };
  await home(page);
  await upload(page, archive(files));
  await expect(page.getByRole("heading", { name: "Pick a save file" })).toBeVisible();
  const directories = page.getByRole("navigation", { name: "Save directories" });
  await directories.getByRole("button", { name: /unrecognized-layout/ }).click();
  await directories.getByRole("button", { name: /^mystery\.dat / }).click();
  await expect(page.getByRole("heading", { name: "mystery.dat", exact: true })).toBeVisible();
  await expect(page.getByText("Raw file inspector", { exact: false })).toBeVisible();
  await expect(page.getByText(/No game plugin claimed this file\./)).toBeVisible();
  await expect(page.getByText("This plugin shows the data but cannot write it.", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeDisabled();
  await expect(page.locator(".field").getByRole("textbox")).toHaveCount(0);
  await expect(page.getByText(fixture("unknown.txt").subarray(0, 16).toString("hex").toUpperCase(), { exact: true })).toBeVisible();
});

// #40 regression: title-less layouts must not gain the synthetic UNKNOWN grouping prefix on export.
test("unknown-layout export preserves the imported path (#40)", async ({ page }, testInfo) => {
  const files = { "unrecognized-layout/mystery.dat": fixture("unknown.txt") };
  await home(page);
  await upload(page, archive(files));
  await expect(page.getByRole("heading", { name: "Pick a save file" })).toBeVisible();
  expectFiles(await exportFiles(page, testInfo), files);
});
