import { test, expect } from "@playwright/test";
import type { SemanticChangeLog } from "../src/api/actionReview";
import type { EditResult, QueryablePage } from "../src/api/protocol";
import { expectFiles, exportFiles, fixture, importSave, openJson, savePath } from "./helpers";

test("F8 review jumps to an unloaded page without writes and individually reverts the real save (#40)", async ({ page }, testInfo) => {
  const original = fixture("queryable.json");
  await importSave(page, { [savePath]: original });
  await openJson(page);
  const firstPage = page.waitForResponse(response => new URL(response.url()).pathname.endsWith("/collections"));
  await page.getByRole("tab", { name: "Search", exact: true }).click();
  const collectionUrl = new URL((await firstPage).url());
  const table = page.getByRole("table", { name: "Whole-tree search", exact: true });
  await page.getByRole("navigation", { name: "Whole-tree search pages" }).getByRole("button", { name: "Next", exact: true }).click();
  const row = table.getByRole("row").filter({ hasText: '$["score_55"]#1' });
  await row.getByRole("button", { name: /^Details for/ }).click();
  const field = page.getByRole("textbox", { name: /^\$\["score_55"\]#1/ });
  await field.fill("555");
  const edit = page.waitForResponse(response => response.url().endsWith("/edits") && response.request().method() === "POST");
  await field.press("Enter");
  const outcome = await (await edit).json() as EditResult;
  expect(outcome.ok).toBe(true);
  const valueId = outcome.changes[0]!.valueId;

  const query = { offset: 50, limit: 50, includeEmpty: true };
  for (const [key, value] of Object.entries({ ...query, revision: outcome.state.revision! })) collectionUrl.searchParams.set(key, String(value));
  const response = await page.request.get(collectionUrl.toString());
  expect(response.ok()).toBe(true);
  const targetPage = await response.json() as QueryablePage;
  const targetRow = targetPage.rows.find(row => row.key === valueId)!;
  expect(targetRow).toBeTruthy();
  const cell = targetRow.cells.value!;
  expect(cell.type).toBe("section");
  if (cell.type !== "section") throw new Error("Expected GenericJson's scalar detail section.");
  const label = `Whole-tree search / ${targetRow.key} / $["score_55"]#1`;

  // GenericJson also exposes these identities eagerly, which takes precedence in Core's review.
  // Project its real change onto its real F8 page to exercise the additive navigation contract.
  // Queries, revisions, editing, individual revert, history and export all use the real backend.
  await page.route("**/changes", async route => {
    const response = await route.fetch();
    const log = await response.json() as SemanticChangeLog;
    await route.fulfill({ response, json: { ...log, changes: log.changes.map(change => change.valueId === valueId ? {
      ...change, label, path: ["tab:Search", `row:${targetRow.key}`, `section:${cell.title}`],
      target: { collectionId: targetPage.collectionId, rowKey: targetRow.key, query },
    } : change) } });
  });
  await page.getByRole("tab", { name: "Overview", exact: true }).click();
  await page.getByText("Changes against original", { exact: true }).click();
  const review = page.locator(".changes-panel");
  const changed = review.getByRole("row").filter({ hasText: label });
  await expect(changed.getByRole("cell").nth(0)).toHaveText("number: 155");
  await expect(changed.getByRole("cell").nth(1)).toHaveText("number: 555");
  const writes: string[] = [];
  page.on("request", request => { if (request.method() === "POST") writes.push(request.url()); });
  const jumping = page.waitForResponse(response => {
    const url = new URL(response.url());
    return url.pathname.endsWith("/collections") && url.searchParams.get("includeEmpty") === "true";
  });
  await changed.getByRole("button", { name: `Jump to ${label}`, exact: true }).click();
  const jumpUrl = new URL((await jumping).url());
  expect(jumpUrl.searchParams.get("collectionId")).toBe(targetPage.collectionId);
  expect(jumpUrl.searchParams.get("offset")).toBe("50");
  expect(jumpUrl.searchParams.get("revision")).toBe(String(outcome.state.revision));
  await expect(field).toBeFocused();
  expect(writes).toEqual([]);
  await expect(page.getByRole("tab", { name: "Search", exact: true })).toHaveAttribute("aria-selected", "true");

  const reverting = changed.getByRole("button", { name: `Revert ${label}`, exact: true });
  await reverting.focus();
  await page.keyboard.press("Enter");
  await expect(review.getByText("No semantic changes.", { exact: true })).toBeVisible();
  await expect(review.getByRole("button", { name: `Revert ${label}`, exact: true })).toHaveCount(0);
  await expect(review.locator("summary")).toBeFocused();
  await expect(review.getByRole("status", { name: "Changes review" })).toHaveText("0 of 0 changed fields");
  expectFiles(await exportFiles(page, testInfo, "reverted-queryable.zip"), { [savePath]: original });
});
