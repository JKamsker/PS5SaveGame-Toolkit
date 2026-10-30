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
  await expect(panel.getByRole("status", { name: "Preset save result" })).toHaveText("Saved “Browser preset”.");

  const row = panel.getByRole("listitem").filter({ hasText: "Browser preset" }).first();
  await expect(row.getByRole("button", { name: "Preview", exact: true })).toBeEnabled();
  await row.getByRole("button", { name: "Preview", exact: true }).click();
  await expect(panel.getByRole("heading", { name: "Preset preview", exact: true })).toBeVisible();
  await expect(panel.getByRole("button", { name: "Apply preset", exact: true })).toBeEnabled();

});

for (const width of [375, 768, 1024, 1440]) {
  test(`typed stale preview recovery remains bounded at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await importSave(page);
    await openJson(page);
    await loadJson(page);
    await editCoins(page);
    await page.getByRole("tab", { name: "Overview", exact: true }).click();

    const panel = page.locator(".presets-panel");
    await panel.getByText("Toolkit presets", { exact: true }).click();
    await panel.getByLabel("Name", { exact: true }).fill(`R9 ${width}`);
    await panel.getByRole("button", { name: "Save preset", exact: true }).click();
    await expect(panel.getByRole("status", { name: "Preset save result" })).toContainText("Saved");

    let stalePreviewRequests = 0;
    let applyRequests = 0;
    let releaseRefresh!: () => void;
    const refreshGate = new Promise<void>(resolve => { releaseRefresh = resolve; });
    let heldRefresh = false;
    await page.route("**/api/workspaces/*/documents/*/presets/*/preview", async route => {
      const response = await route.fetch();
      const body = await response.json() as { preview: { stale?: boolean; plan?: { errors?: unknown[] } } };
      stalePreviewRequests++;
      body.preview.stale = true;
      body.preview.plan = { ...body.preview.plan, errors: [{ valueId: "", message: "The document is stale." }] };
      await route.fulfill({ status: 409, contentType: "application/json", body: JSON.stringify(body) });
    });
    await page.route("**/api/workspaces/*/documents/*", async route => {
      const url = new URL(route.request().url());
      if (route.request().method() === "GET" && /\/documents\/[^/]+$/.test(url.pathname) && !heldRefresh) {
        heldRefresh = true;
        await refreshGate;
      }
      await route.fallback();
    });
    await page.route("**/api/workspaces/*/documents/*/presets/*/apply", async route => {
      applyRequests++;
      await route.continue();
    });

    const row = panel.getByRole("listitem").filter({ hasText: `R9 ${width}` }).first();
    const opener = row.getByRole("button", { name: "Preview", exact: true });
    await opener.focus();
    await opener.click();
    const staleHeading = panel.getByRole("heading", { name: "Preset preview is stale", exact: true });
    await expect(staleHeading).toBeVisible();
    await expect(staleHeading).toBeFocused();
    await expect(panel.getByRole("button", { name: "Preview again", exact: true })).toBeDisabled();
    await expect(panel.getByRole("button", { name: "Apply preset", exact: true })).toBeDisabled();
    await expect(panel.getByRole("alert")).toHaveCount(1);

    await staleHeading.press("Escape");
    await expect(staleHeading).not.toBeVisible();
    await expect(opener).toBeFocused();
    await expect(panel.getByText("Refreshing the document before another preset preview…", { exact: true })).toBeVisible();
    await opener.click();
    expect(stalePreviewRequests).toBe(1);

    releaseRefresh();
    await expect(opener).toBeEnabled();
    await opener.click();
    await expect(panel.getByRole("heading", { name: "Preset preview is stale", exact: true })).toBeVisible();
    await expect(panel.getByRole("button", { name: "Apply preset", exact: true })).toBeDisabled();
    expect(stalePreviewRequests).toBe(2);
    expect(applyRequests).toBe(0);
  });
}
