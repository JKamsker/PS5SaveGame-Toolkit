import { expect, test } from "@playwright/test";
import { editCoins, importSave, loadJson, openJson } from "./helpers";

test("downloads the editor only after file selection and reports an edited deep link as gone after reload", async ({ page }) => {
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
  const deepLink = page.url();
  // Workspaces live in the page's in-browser runtime memory, so a reload cannot restore the edited
  // dump. The app must boot cleanly on the deep link and say so, rather than show a blank or stale editor.
  await page.reload();
  expect(page.url()).toBe(deepLink);
  const gone = page.getByRole("heading", { name: "This workspace is gone", exact: true });
  // The reloaded page boots a new runtime (download + start) before it can answer.
  await expect(gone).toBeVisible({ timeout: 30_000 });
  await expect(gone).toBeFocused();
  await expect(page.getByRole("tab", { name: "JSON", exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toHaveCount(0);
  await page.getByRole("link", { name: "Import a save dump", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Open a PS5 save dump" })).toBeVisible();
});
