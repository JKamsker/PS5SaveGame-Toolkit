import { expect, test } from "@playwright/test";
import { coinsField, editCoins, editedCoins, importSave, loadJson, openJson } from "./helpers";

test("downloads the editor only after file selection and restores edited deep links", async ({ page }) => {
  const scripts: string[] = [];
  page.on("request", request => { if (request.resourceType() === "script") scripts.push(request.url()); });
  await importSave(page);
  expect(scripts.some(url => /\/WorkspacePage-[^/]+\.js$/.test(url))).toBe(true);
  expect(scripts.some(url => /\/(DocumentEditor|GraphView|MapView)-[^/]+\.js$/.test(url))).toBe(false);

  let release!: () => void;
  const pending = new Promise<void>(resolve => { release = resolve; });
  await page.route("**/assets/DocumentEditor-*.js", async route => { await pending; await route.continue(); });
  const opening = openJson(page);
  try {
    await expect(page.getByRole("status").filter({ hasText: "Opening…" })).toBeVisible();
  } finally {
    release();
  }
  await opening;
  expect(scripts.some(url => /\/DocumentEditor-[^/]+\.js$/.test(url))).toBe(true);
  await loadJson(page);
  await editCoins(page);
  await page.reload();
  await expect(page.getByRole("tab", { name: "JSON", exact: true })).toBeVisible();
  await loadJson(page);
  await expect(coinsField(page)).toHaveValue(editedCoins);
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeEnabled();
});
