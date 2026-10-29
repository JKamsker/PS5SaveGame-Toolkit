import { test, expect, type Locator, type Page } from "@playwright/test";
import {
  archive, coinsField, downloadedFiles, editedCoins, expectArchiveBytes,
  home, originalCoins, originalSave, withCoins,
} from "./helpers";

async function tabTo(page: Page, target: Locator) {
  await expect(target).toBeVisible();
  // A bounded real Tab walk detects unreachable controls instead of assigning focus.
  for (let stops = 0; stops < 100; stops++) {
    if (await target.evaluate(element => element === document.activeElement)) return;
    await page.keyboard.press("Tab");
  }
  await expect(target, "Control must be reachable using Tab").toBeFocused();
}

test("imports, opens, edits and exports using only the keyboard", async ({ page }, testInfo) => {
  await home(page);
  await tabTo(page, page.getByRole("button", { name: "Open zip", exact: true }));
  const choosing = page.waitForEvent("filechooser");
  const importing = page.waitForResponse(response => response.url().endsWith("/api/workspaces") && response.request().method() === "POST");
  await page.keyboard.press("Enter");
  // Playwright supplies the native chooser's file; all application interaction is keyboard-only.
  await (await choosing).setFiles({ name: "keyboard.zip", mimeType: "application/zip", buffer: archive() });
  expect((await importing).status()).toBe(200);
  await expect(page.getByRole("heading", { name: "Pick a save file" })).toBeVisible();
  const directories = page.getByRole("navigation", { name: "Save directories" });
  await tabTo(page, directories.getByRole("button", { name: / E2E / }));
  await page.keyboard.press("Enter");
  await tabTo(page, directories.getByRole("button", { name: /^save\.json / }));
  await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", { name: "save.json", exact: true })).toBeVisible();
  const plugin = page.getByRole("combobox", { name: "Open with" });
  await tabTo(page, plugin);
  // Native select arrow keys, rather than selectOption(), exercise its keyboard support.
  await page.keyboard.press("Home");
  const optionCount = await plugin.getByRole("option").count();
  for (let option = 0; option < optionCount && await plugin.inputValue() !== "format.json"; option++) {
    await page.keyboard.press("ArrowDown");
  }
  await expect(plugin).toHaveValue("format.json");
  await tabTo(page, page.getByRole("tab", { name: "Overview", exact: true }));
  await page.keyboard.press("ArrowRight");
  await expect(page.getByRole("tab", { name: "JSON", exact: true })).toBeFocused();
  await expect(page.getByRole("tab", { name: "JSON", exact: true })).toHaveAttribute("aria-selected", "true");
  await tabTo(page, page.getByRole("button", { name: "Load Entries", exact: true }));
  await page.keyboard.press("Enter");
  await expect(coinsField(page)).toHaveValue(originalCoins);
  await tabTo(page, coinsField(page));
  await page.keyboard.press("ControlOrMeta+A");
  await page.keyboard.insertText(editedCoins);
  await page.keyboard.press("Enter");
  await expect(coinsField(page)).toHaveAccessibleName(/modified/);
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeEnabled();
  await tabTo(page, page.getByRole("button", { name: /^Export/ }));
  await page.keyboard.press("Enter");
  const dialog = page.getByRole("dialog", { name: "Export edited saves" });
  await tabTo(page, dialog.getByRole("radio", { name: /Changed save folders/ }));
  await page.keyboard.press("ArrowDown");
  await page.keyboard.press("ArrowDown");
  await expect(dialog.getByRole("radio", { name: /Whole dump/ })).toBeChecked();
  const download = dialog.getByRole("button", { name: "Download zip", exact: true });
  await expect(download).toBeEnabled();
  await tabTo(page, download);
  const downloading = page.waitForEvent("download");
  await page.keyboard.press("Enter");
  expectArchiveBytes(await downloadedFiles(await downloading, testInfo), withCoins(originalSave, originalCoins, editedCoins));
  await expect(dialog).not.toBeVisible();
});
