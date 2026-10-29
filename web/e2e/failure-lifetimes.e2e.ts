import type { QueryablePage } from "../src/api/protocol";
import type { Page } from "@playwright/test";
import type { ActionPreview } from "../src/api/actionReview";
import { test, expect, focusedControl } from "./failure-harness";
import { actionPreview, resetAction } from "./action-helpers";
import { coinsField, editCoins, editedCoins, fixture, home, importSave, loadJson, openJson, originalCoins, savePath } from "./helpers";

// Let completed body parsing and React effects run before inspecting retired responses.
async function settleUi(page: Page) {
  await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
}

test("unknown workspace 404 offers retry and a focused import escape route", async ({ page, faults }) => {
  // Use the actual missing-workspace endpoint, without intercepting the static app.
  faults.expectNetworkFailures(/\/api\/workspaces\/missing-workspace$/);
  await page.goto("/w/missing-workspace");
  const heading = page.getByRole("heading", { name: "This workspace is gone", exact: true });
  await expect(heading).toBeVisible();
  await expect(heading).toBeFocused();
  await expect(page.getByRole("alert")).not.toBeEmpty();
  const retrying = page.waitForResponse(response => response.url().endsWith("/api/workspaces/missing-workspace"));
  await page.getByRole("button", { name: "Retry workspace", exact: true }).click();
  await (await retrying).finished();
  await expect(heading).toBeVisible();
  await expect(page.getByRole("button", { name: "Retry workspace", exact: true })).toBeEnabled();
  await focusedControl(page);
  await page.getByRole("link", { name: "Import a save dump", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Open a PS5 save dump" })).toBeVisible();
});

test("an expired document read shows a retryable 404 instead of a blank editor", async ({ page, faults }) => {
  await importSave(page);
  await openJson(page);
  const url = page.url();
  faults.expectNetworkFailures(/\/api\/workspaces\/[^/]+\/documents\/[^/?]+$/);
  let expired = true;
  await page.route(/\/api\/workspaces\/[^/]+\/documents\/[^/?]+$/, async route => {
    if (!expired) return route.fallback();
    await route.fulfill({ status: 404, json: { error: "The editing session expired. Reopen the save file or retry." } });
  });
  await page.goto(url);
  const heading = page.getByRole("heading", { name: "This file could not be opened", exact: true });
  await expect(heading).toBeVisible();
  await expect(heading).toBeFocused();
  await expect(page.getByRole("alert")).toHaveText("The editing session expired. Reopen the save file or retry.");
  expired = false;
  await page.getByRole("button", { name: "Retry document", exact: true }).click();
  await expect(page.getByRole("tab", { name: "JSON", exact: true })).toBeVisible();
  await focusedControl(page);
  await loadJson(page);
  await expect(coinsField(page)).toHaveValue(originalCoins);
});

test("a late table query cannot replace a newer search or restore an old selection", async ({ page }) => {
  await importSave(page, { [savePath]: fixture("queryable.json") });
  await openJson(page);
  await page.getByRole("tab", { name: "Search", exact: true }).click();
  const search = page.getByRole("searchbox", { name: "Search Whole-tree search" });
  await expect(page.getByText("64 of 64 rows", { exact: true })).toBeVisible();
  let finish!: () => void;
  const delayed = new Promise<void>(resolve => { finish = resolve; });
  let oldResponse!: QueryablePage;
  await page.route(/\/collections\?/, async route => {
    if (new URL(route.request().url()).searchParams.get("q") !== "score_0") return route.fallback();
    const response = await route.fetch();
    oldResponse = await response.json() as QueryablePage;
    await delayed;
    await route.fulfill({ response, json: oldResponse });
  });
  await search.fill("score_0");
  await expect.poll(() => Boolean(oldResponse)).toBe(true);
  await search.fill("score_55");
  await expect(page.getByText("1 of 64 rows", { exact: true })).toBeVisible();
  const table = page.getByRole("table", { name: "Whole-tree search", exact: true });
  await expect(table.getByRole("cell", { name: '$["score_55"]#1', exact: true })).toBeVisible();
  const oldFinished = page.waitForResponse(response => new URL(response.url()).searchParams.get("q") === "score_0");
  finish();
  await (await oldFinished).finished();
  await settleUi(page);
  await expect(table.getByRole("row")).toHaveCount(2);
  await expect(table.getByRole("cell", { name: '$["score_55"]#1', exact: true })).toBeVisible();
  await expect(search).toHaveValue("score_55");
  await expect(page.getByRole("status", { name: "Whole-tree search selection" })).toHaveText("No rows selected");
  await expect(search).toBeFocused();
});

test("cancelled export ignores a late download failure and restores opener focus", async ({ page, faults }) => {
  await importSave(page);
  await openJson(page);
  await loadJson(page);
  await editCoins(page);
  let finish!: () => void;
  let started = false;
  const delayed = new Promise<void>(resolve => { finish = resolve; });
  faults.expectNetworkFailures(/\/export$/);
  await page.route("**/export", async route => {
    started = true;
    await delayed;
    await route.fulfill({ status: 503, json: { error: "Late export failure must stay retired." } });
  });
  const opener = page.getByRole("button", { name: /^Export/ });
  await opener.click();
  const dialog = page.getByRole("dialog", { name: "Export edited saves" });
  await expect(dialog.getByRole("button", { name: "Download zip", exact: true })).toBeEnabled();
  await dialog.getByRole("button", { name: "Download zip", exact: true }).click();
  await expect.poll(() => started).toBe(true);
  await dialog.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(dialog).not.toBeVisible();
  await expect(opener).toBeFocused();
  const response = page.waitForResponse(response => response.url().endsWith("/export"));
  finish();
  await (await response).finished();
  await settleUi(page);
  await expect(page.getByRole("alert")).toHaveCount(0);
  await expect(coinsField(page)).toHaveValue(editedCoins);
  await expect(opener).toBeFocused();
  await page.unroute("**/export");
  await opener.click();
  await expect(dialog.getByRole("button", { name: "Download zip", exact: true })).toBeEnabled();
  await expect(dialog.getByRole("alert")).toHaveCount(0);
});

test("navigating away while opening a file retires a late error without changing the home page", async ({ page, faults }) => {
  await importSave(page);
  let finish!: () => void;
  let started = false;
  const delayed = new Promise<void>(resolve => { finish = resolve; });
  faults.expectNetworkFailures(/\/files\/[^/]+\/open$/);
  await page.route("**/files/*/open", async route => {
    started = true;
    await delayed;
    await route.fulfill({ status: 503, json: { error: "Late open failure must stay retired." } });
  });
  await page.getByRole("navigation", { name: "Save directories" }).getByRole("button")
    .filter({ has: page.getByText("save.json", { exact: true }) }).click();
  await expect.poll(() => started).toBe(true);
  // Close dump performs SPA navigation without discarding the pending fetch's JS realm.
  await page.getByRole("button", { name: "Close dump", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Open a PS5 save dump" })).toBeVisible();
  const response = page.waitForResponse(response => response.url().endsWith("/open"));
  finish();
  await (await response).finished();
  await settleUi(page);
  await expect(page).toHaveURL(/\/$/);
  await expect(page.getByRole("alert")).toHaveCount(0);
  await focusedControl(page);
  await home(page);
});

test("leaving a slow lazy view aborts its request and returning can load it again", async ({ page, faults }) => {
  await importSave(page);
  await openJson(page);
  let finish!: () => void;
  let started = false;
  let slow = true;
  const delayed = new Promise<void>(resolve => { finish = resolve; });
  faults.expectNetworkFailures(/\/nodes\?/);
  await page.route("**/nodes?*", async route => {
    if (!slow) return route.fallback();
    started = true;
    await delayed;
    await route.abort("aborted");
  });
  await page.getByRole("tab", { name: "JSON", exact: true }).click();
  await page.getByRole("button", { name: "Load Entries", exact: true }).click();
  await expect.poll(() => started).toBe(true);
  const aborted = page.waitForEvent("requestfailed", request => request.url().includes("/nodes?"));
  await page.getByRole("tab", { name: "Overview", exact: true }).click();
  expect((await aborted).failure()?.errorText).toContain("ERR_ABORTED");
  slow = false;
  finish();
  await expect(page.getByRole("alert")).toHaveCount(0);
  await loadJson(page);
  await expect(coinsField(page)).toHaveValue(originalCoins);
  await expect(page.getByRole("alert")).toHaveCount(0);
  await focusedControl(page);
});

async function holdFirstPreview(page: Page, outcome: "failure" | "success") {
  let finish!: () => void;
  let started = false;
  let requests = 0;
  const delayed = new Promise<void>(resolve => { finish = resolve; });
  await page.route("**/actions/plan", async route => {
    if (++requests !== 1) return route.fallback();
    const response = await route.fetch();
    const originalPlan = await response.json() as ActionPreview;
    started = true;
    await delayed;
    if (outcome === "failure") await route.fulfill({ status: 503, json: { error: "Retired preview failure." } });
    else await route.fulfill({ response, json: { ...originalPlan, planId: "obsolete-preview-token",
      plan: { ...originalPlan.plan, changes: [] } } });
  });
  return { finish, started: () => started };
}

for (const outcome of ["failure", "success"] as const) {
  test(`Escape retires a slow planned preview before its late ${outcome}`, async ({ page, faults }) => {
    await importSave(page);
    await openJson(page);
    await loadJson(page);
    await editCoins(page);
    await page.getByRole("tab", { name: "Overview", exact: true }).click();
    faults.expectNetworkFailures(/\/actions\/plan$/);
    const delayed = await holdFirstPreview(page, outcome);
    const opener = page.getByRole("button", { name: resetAction, exact: true });
    await opener.click();
    const dialog = actionPreview(page);
    await expect.poll(delayed.started).toBe(true);
    await page.keyboard.press("Escape");
    await expect(dialog).not.toBeVisible();
    await expect(opener).toBeFocused();
    const undo = page.getByRole("button", { name: "Undo", exact: true });
    await undo.focus();
    let latest: ActionPreview | undefined;
    if (outcome === "success") {
      // A second request must start without waiting for the retired first response.
      const response = page.waitForResponse(response => response.url().endsWith("/actions/plan"));
      await opener.click();
      latest = await (await response).json() as ActionPreview;
      await expect(dialog.getByRole("status")).toHaveText("1 field changes · 0 skipped rows · one undo step");
    }
    const oldFinished = page.waitForResponse(response => response.url().endsWith("/actions/plan"));
    delayed.finish();
    await (await oldFinished).finished();
    await settleUi(page);
    if (outcome === "failure") {
      await expect(undo).toBeFocused();
      await expect(page.getByRole("alert")).toHaveCount(0);
      await opener.click();
      await expect(dialog.getByRole("button", { name: "Apply", exact: true })).toBeEnabled();
    } else {
      await expect(dialog.getByRole("status")).toHaveText("1 field changes · 0 skipped rows · one undo step");
      const applying = page.waitForRequest(request => request.url().endsWith("/actions/apply"));
      await dialog.getByRole("button", { name: "Apply", exact: true }).click();
      expect((await applying).postDataJSON().planId).toBe(latest!.planId);
      await expect(dialog).not.toBeVisible();
      await loadJson(page);
      await expect(coinsField(page)).toHaveValue(originalCoins);
    }
  });
}
