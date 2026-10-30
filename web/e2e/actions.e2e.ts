import { test, expect } from "@playwright/test";
import type { SaveDocument } from "../src/api/protocol";
import {
  coinsField, editCoins, editedCoins, expectArchiveBytes, expectFiles, exportFiles, importSave,
  fixture, loadJson, openJson, originalCoins, originalFiles, originalSave, savePath, withCoins,
} from "./helpers";
import { closePreview, editScalar, expectPreview, openTableReset, resetAction as action, searchRow, searchTable } from "./action-helpers";

const editedPlayer = "Synthetic edited player";

test("bulk preview and cancel preserve edits; apply restores exported values as one undo step", async ({ page }, testInfo) => {
  await importSave(page);
  await openJson(page);
  await loadJson(page);
  await editCoins(page);
  const player = page.getByRole("textbox", { name: /^player\s*meaning unknown/ });
  await player.fill(editedPlayer);
  const editing = page.waitForResponse(response => response.url().endsWith("/edits") && response.request().method() === "POST");
  // Generic JSON strings use a multiline editor and commit on blur.
  await player.press("Tab");
  expect((await editing).status()).toBe(200);
  await expect(player).toHaveAccessibleName(/modified/);
  const before = await exportFiles(page, testInfo, "before-preview.zip");
  await page.getByRole("tab", { name: "Overview", exact: true }).click();
  await page.getByRole("button", { name: action, exact: true }).click();
  const preview = page.getByRole("dialog", { name: `Preview: ${action}`, exact: true });
  await expect(preview.getByText("Whole document", { exact: true })).toBeVisible();
  await expect(preview.getByRole("status")).toHaveText("2 field changes · 0 skipped rows · one undo step");
  const table = preview.getByRole("table", { name: "Preview changes", exact: true });
  await expect(table.getByRole("row")).toHaveCount(3);
  const coins = table.getByRole("row").filter({ has: page.getByRole("rowheader", { name: /coins/ }) });
  await expect(coins.getByRole("cell").nth(0)).toHaveText(`number: ${editedCoins}`);
  await expect(coins.getByRole("cell").nth(1)).toHaveText(`number: ${originalCoins}`);
  await expect(preview.getByRole("button", { name: "Apply", exact: true })).toBeEnabled();
  await preview.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(preview).not.toBeVisible();
  const afterCancel = await exportFiles(page, testInfo, "after-cancel.zip");
  // Check all paths and bytes, including fields beyond the preview's first row.
  expectFiles(afterCancel, before);

  await page.getByRole("button", { name: action, exact: true }).click();
  await expect(preview.getByRole("status")).toHaveText("2 field changes · 0 skipped rows · one undo step");
  await preview.getByRole("button", { name: "Apply", exact: true }).click();
  await expect(preview).not.toBeVisible();
  expectArchiveBytes(await exportFiles(page, testInfo, "after-apply.zip"), originalSave);
  await page.getByRole("button", { name: "Undo", exact: true }).click();
  expectFiles(await exportFiles(page, testInfo, "undo-bulk.zip"), before);
  await loadJson(page);
  await expect(coinsField(page)).toHaveValue(editedCoins);
  await expect(player).toHaveValue(editedPlayer);
  await page.getByRole("button", { name: "Redo", exact: true }).click();
  await expect(coinsField(page)).toHaveValue(originalCoins);
  await expect(player).toHaveValue(JSON.parse(originalSave.toString("utf8")).player);
  expectArchiveBytes(await exportFiles(page, testInfo, "redo-bulk.zip"), originalSave);
});

test("F6 table action previews only the selected row; cancel, apply, undo and redo preserve exact bytes (#40)", async ({ page }, testInfo) => {
  await importSave(page);
  await openJson(page);
  await loadJson(page);
  await editCoins(page);
  const player = page.getByRole("textbox", { name: /^player\s*meaning unknown/ });
  await editScalar(page, player, editedPlayer, true);
  const playerOnly = Buffer.from(originalSave.toString("utf8").replace(/("player"\s*:\s*)"(?:\\.|[^"\\])*"/u,
    (_match, prefix: string) => prefix + JSON.stringify(editedPlayer)));
  const edited = withCoins(playerOnly, originalCoins, editedCoins);
  expectArchiveBytes(await exportFiles(page, testInfo, "selected-before.zip"), edited);
  await page.getByRole("tab", { name: "Search", exact: true }).click();
  await searchRow(page, "coins").getByRole("checkbox").check();

  // Deliberately open the action on another row. Scope comes from selection,
  // never the clicked row or the whole document (both fields were edited).
  await openTableReset(page, "player");
  const changes = [{ key: "coins", before: `number: ${editedCoins}`, after: `number: ${originalCoins}` }];
  await expectPreview(page, "1 selected row", changes);
  await closePreview(page, "Cancel");
  expectArchiveBytes(await exportFiles(page, testInfo, "selected-cancel.zip"), edited);
  await loadJson(page);
  await expect(coinsField(page)).toHaveValue(editedCoins);
  await expect(player).toHaveValue(editedPlayer);
  await page.getByRole("tab", { name: "Search", exact: true }).click();
  // Switching tabs remounts the table and clears its selection.
  await searchRow(page, "coins").getByRole("checkbox").check();

  await openTableReset(page, "player");
  await expectPreview(page, "1 selected row", changes);
  await closePreview(page, "Apply");
  expectArchiveBytes(await exportFiles(page, testInfo, "selected-apply.zip"), playerOnly);
  await loadJson(page);
  await expect(coinsField(page)).toHaveValue(originalCoins);
  await expect(player).toHaveValue(editedPlayer);
  await page.getByRole("button", { name: "Undo", exact: true }).click();
  await expect(coinsField(page)).toHaveValue(editedCoins);
  await expect(player).toHaveValue(editedPlayer);
  expectArchiveBytes(await exportFiles(page, testInfo, "selected-undo.zip"), edited);
  await page.getByRole("button", { name: "Redo", exact: true }).click();
  await expect(coinsField(page)).toHaveValue(originalCoins);
  await expect(player).toHaveValue(editedPlayer);
  expectArchiveBytes(await exportFiles(page, testInfo, "selected-redo.zip"), playerOnly);
});

test("F6 query scope restores all filtered rows across unloaded pages and leaves nonmatches unchanged (#40)", async ({ page }, testInfo) => {
  const original = fixture("queryable.json");
  const files = { ...originalFiles, [savePath]: original };
  await importSave(page, files);
  await openJson(page);
  await page.getByRole("tab", { name: "JSON", exact: true }).click();
  await page.getByRole("button", { name: "Load Entries", exact: true }).click();
  const first = page.getByRole("textbox", { name: /^score_00\s*meaning unknown/ });
  const unloaded = page.getByRole("textbox", { name: /^score_55\s*meaning unknown/ });
  const nonmatch = page.getByRole("textbox", { name: /^note\s*meaning unknown/ });
  await editScalar(page, first, "900");
  await editScalar(page, unloaded, "955");
  const editedNote = "Edited outside the numeric query";
  await editScalar(page, nonmatch, editedNote, true);
  const noteOnly = Buffer.from(original.toString("utf8").replace('"Synthetic query fixture"', JSON.stringify(editedNote)));
  const edited = Buffer.from(noteOnly.toString("utf8").replace('"score_00": 100', '"score_00": 900').replace('"score_55": 155', '"score_55": 955'));
  expectFiles(await exportFiles(page, testInfo, "query-before.zip"), { ...files, [savePath]: edited });
  await page.getByRole("tab", { name: "Search", exact: true }).click();
  await page.getByRole("textbox", { name: "Filter JSON type", exact: true }).fill("number");
  await page.getByRole("searchbox", { name: "Search Whole-tree search" }).fill("score_");
  await expect(page.getByText("60 of 64 rows", { exact: true })).toBeVisible();
  await expect(searchTable(page).getByRole("row")).toHaveCount(51);
  await expect(searchRow(page, "score_55")).toHaveCount(0);
  await page.getByRole("button", { name: "Select all 60 filtered rows", exact: true }).click();
  await expect(page.getByText("All filtered rows selected; edits apply to this query.", { exact: true })).toBeVisible();
  await openTableReset(page, "score_00");
  const changes = [
    { key: "score_00", before: "number: 900", after: "number: 100" },
    { key: "score_55", before: "number: 955", after: "number: 155" },
  ];
  await expectPreview(page, "60 selected rows", changes);
  await closePreview(page, "Cancel");
  expectFiles(await exportFiles(page, testInfo, "query-cancel.zip"), { ...files, [savePath]: edited });
  await openTableReset(page, "score_00");
  await expectPreview(page, "60 selected rows", changes);
  await closePreview(page, "Apply");
  expectFiles(await exportFiles(page, testInfo, "query-apply.zip"), { ...files, [savePath]: noteOnly });
  await page.getByRole("tab", { name: "JSON", exact: true }).click();
  await page.getByRole("button", { name: "Load Entries", exact: true }).click();
  await expect(first).toHaveValue("100");
  await expect(unloaded).toHaveValue("155");
  await expect(nonmatch).toHaveValue(editedNote);
  await page.getByRole("button", { name: "Undo", exact: true }).click();
  await expect(first).toHaveValue("900");
  await expect(unloaded).toHaveValue("955");
  await expect(nonmatch).toHaveValue(editedNote);
  expectFiles(await exportFiles(page, testInfo, "query-undo.zip"), { ...files, [savePath]: edited });
  await page.getByRole("button", { name: "Redo", exact: true }).click();
  await expect(first).toHaveValue("100");
  await expect(unloaded).toHaveValue("155");
  await expect(nonmatch).toHaveValue(editedNote);
  expectFiles(await exportFiles(page, testInfo, "query-redo.zip"), { ...files, [savePath]: noteOnly });
});

test("F6 empty table selection means no rows; the action outside the table retains document scope (#40)", async ({ page }, testInfo) => {
  await importSave(page);
  await openJson(page);
  await loadJson(page);
  await editCoins(page);
  const edited = withCoins(originalSave, originalCoins, editedCoins);
  await page.getByRole("tab", { name: "Search", exact: true }).click();
  await expect(searchTable(page).getByRole("checkbox", { checked: true })).toHaveCount(0);
  const location = new URL(page.url());
  const documentUrl = `/api/workspaces/${location.pathname.split("/").at(-1)}/documents/${location.searchParams.get("doc")}`;
  const beforeResponse = await page.request.get(documentUrl);
  expect(beforeResponse.ok()).toBe(true);
  const before = await beforeResponse.json() as SaveDocument;
  expect(before.state.revision).toEqual(expect.any(Number));
  expect(before.state.canUndo).toBe(true);
  let applies = 0;
  page.on("request", request => { if (request.url().endsWith("/actions/apply")) applies++; });
  await openTableReset(page, "coins");
  await expectPreview(page, "0 selected rows", []);
  await closePreview(page, "Cancel");
  expect(applies).toBe(0);
  const afterResponse = await page.request.get(documentUrl);
  expect(afterResponse.ok()).toBe(true);
  const after = await afterResponse.json() as SaveDocument;
  expect(after.state).toEqual(before.state);
  expectArchiveBytes(await exportFiles(page, testInfo, "empty-selection.zip"), edited);
  await loadJson(page);
  await expect(coinsField(page)).toHaveValue(editedCoins);
  await page.getByRole("tab", { name: "Overview", exact: true }).click();
  await page.getByRole("button", { name: action, exact: true }).click();
  await expectPreview(page, "Whole document", [{ key: "coins", before: `number: ${editedCoins}`, after: `number: ${originalCoins}` }]);
  await closePreview(page, "Apply");
  expect(applies).toBe(1);
  expectArchiveBytes(await exportFiles(page, testInfo, "document-scope.zip"), originalSave);
  await loadJson(page);
  await expect(coinsField(page)).toHaveValue(originalCoins);
});
