import { test, expect, type Page } from "@playwright/test";
import { mkdirSync, readFileSync } from "node:fs";
import path from "node:path";
import type { SemanticChangeLog } from "../src/api/actionReview";
import type { EditResult } from "../src/api/protocol";
import { importSave, loadJson, openJson } from "./helpers";

type FixtureOptions = { incomplete?: boolean; failOnce?: boolean; mockRevert?: boolean };

async function installReviewFixture(page: Page, options: FixtureOptions = {}) {
  const source = JSON.parse(readFileSync(new URL("./fixtures/semantic-review.json", import.meta.url), "utf8")) as
    Omit<SemanticChangeLog, "revision">;
  let failures = options.failOnce ? 4 : 0;
  let reverted = false;
  let revision = 0;
  await page.route("**/changes", async route => {
    if (route.request().method() !== "GET") return route.continue();
    if (failures > 0) {
      failures--;
      return route.fulfill({ status: 503, contentType: "application/json", body: JSON.stringify({ message: "Synthetic review unavailable." }) });
    }
    const upstream = await route.fetch();
    const live = await upstream.json() as SemanticChangeLog;
    revision = live.revision;
    const log: SemanticChangeLog = {
      ...source,
      revision,
      complete: options.incomplete ? false : source.complete,
      warnings: options.incomplete ? ["The synthetic review stopped at its bounded target budget."] : source.warnings,
      coverage: options.incomplete ? { ...source.coverage!, omittedCount: 2, omittedReasons: ["target budget"] } : source.coverage,
      changes: reverted ? [] : options.incomplete ? [] : source.changes,
    };
    await route.fulfill({ response: upstream, json: log });
  });
  if (options.mockRevert) {
    await page.route("**/changes/revert", async route => {
      if (route.request().method() !== "POST") return route.continue();
      reverted = true;
      const result: EditResult = {
        ok: true, errors: [], changes: [], warnings: [], structureChanged: false,
        state: { canUndo: false, canRedo: false, isDirty: false, revision },
      };
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(result) });
    });
  }
}

async function openReview(page: Page) {
  await importSave(page);
  await openJson(page);
  await loadJson(page);
  await page.getByRole("tab", { name: "Overview", exact: true }).click();
  await page.getByText("Changes against original", { exact: true }).click();
  return page.locator(".changes-panel");
}

test("metadata-backed review renders canonical aliases, member context and safe targets at four widths", async ({ page }) => {
  await installReviewFixture(page);
  const review = await openReview(page);
  await expect(review.getByRole("status", { name: "Changes review" })).toHaveText("2 of 2 changed fields");

  const primary = review.getByRole("row").filter({ hasText: "Character balance" });
  await expect(primary.getByRole("cell").nth(0)).toHaveText("12345");
  await expect(primary.getByRole("cell").nth(1)).toHaveText("54321");
  await expect(primary).toContainText("Verified");
  await expect(primary).toContainText("character · PPSA00000/sdimg_E2E/character.dat");
  await expect(primary).toContainText("Technical ID: character/balance");
  await primary.getByText(/Also shown as 1 projection/).click();
  await expect(primary).toContainText("character/raw/balance");
  await expect(primary).toContainText("Derived");

  const search = review.getByRole("searchbox", { name: "Find a changed field" });
  await search.fill("character/raw/balance");
  await expect(review.getByRole("row").filter({ hasText: "Character balance" })).toHaveCount(1);
  await search.fill("profile");
  const removed = review.getByRole("row").filter({ hasText: "Profile removed field" });
  await expect(removed.getByRole("button", { name: "Jump to Profile removed field" })).toBeDisabled();
  const revert = removed.getByRole("button", { name: "Revert Profile removed field" });
  await expect(revert).toBeDisabled();
  await expect(revert).toHaveAttribute("aria-describedby", /change-reason/);
  await expect(removed).toContainText("The reviewed target is unavailable");

  await search.fill("");
  const scratch = process.env.SAVETOOLKIT_E2E_SCRATCH ?? "D:/Temp/saveeditor/test-scratch/e2e-smoke";
  const screenshotDir = path.join(scratch, "screenshots");
  mkdirSync(screenshotDir, { recursive: true });
  for (const width of [375, 768, 1024, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    await expect(page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).resolves.toBe(true);
    await page.screenshot({ path: path.join(screenshotDir, "f13-review-panel-" + width + ".png"), fullPage: true });
  }
  await search.focus();
  await page.keyboard.press("Tab");
  await expect(page.getByRole("button", { name: "Jump to Character balance — 長い名前" })).toBeVisible();
});

test("incomplete review states that only the reviewed portion is proven", async ({ page }) => {
  await installReviewFixture(page, { incomplete: true });
  const review = await openReview(page);
  await expect(review.getByText("Review coverage is incomplete.", { exact: false })).toBeVisible();
  await expect(review.getByText("No proven changes in the reviewed portion.", { exact: true })).toBeVisible();
  await expect(review.getByText(/Examined 4; omitted 2/)).toBeVisible();
  await expect(review.getByText("No semantic changes.", { exact: true })).toHaveCount(0);
});

test("review failure offers retry and successful revert returns focus to the summary", async ({ page }) => {
  await installReviewFixture(page, { failOnce: true, mockRevert: true });
  const review = await openReview(page);
  await expect(review.getByRole("alert")).toHaveText(/503/);
  await review.getByRole("button", { name: "Retry changes" }).click();
  await expect(review.getByRole("status", { name: "Changes review" })).toHaveText("2 of 2 changed fields");
  const primary = review.getByRole("row").filter({ hasText: "Character balance" });
  await primary.getByRole("button", { name: "Revert Character balance — 長い名前" }).click();
  await expect(review.getByText("No semantic changes.", { exact: true })).toBeVisible();
  await expect(review.locator("summary")).toBeFocused();
});
