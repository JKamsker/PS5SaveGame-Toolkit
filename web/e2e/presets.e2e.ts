import { expect, test } from "@playwright/test";
import { editCoins, importSave, loadJson, openJson } from "./helpers";

test("preset capture, compatibility summary and preview remain reachable", async ({ page }) => {
  await importSave(page);
  await openJson(page);
  await loadJson(page);
  await editCoins(page);
  await page.getByRole("tab", { name: "Overview", exact: true }).click();

  const panel = page.locator(".presets-panel");
  await panel.getByText("Toolkit presets", { exact: true }).click();
  await expect(panel.getByRole("heading", { name: "Saved presets", exact: true })).toBeVisible();
  await panel.getByLabel("Name", { exact: true }).fill("Browser preset");
  await panel.getByRole("button", { name: "Save preset", exact: true }).click();
  const saved = panel.getByRole("status", { name: "Preset save result", exact: true });
  await expect(saved).toHaveCount(1);
  await expect(saved).toHaveText("Saved “Browser preset”.");

  const row = panel.getByRole("listitem").filter({ hasText: "Browser preset" }).first();
  await expect(row.getByRole("button", { name: "Preview", exact: true })).toBeEnabled();
  await row.getByRole("button", { name: "Preview", exact: true }).click();
  await expect(panel.getByRole("heading", { name: "Preset preview", exact: true })).toBeVisible();
  await expect(panel.getByRole("button", { name: "Apply preset", exact: true })).toBeEnabled();

});
