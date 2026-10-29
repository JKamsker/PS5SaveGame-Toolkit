import { expect, test, type Page } from "@playwright/test";
import type { ActionNode, ViewNode } from "../src/api/protocol";
import { editCoins, exportFiles, expectArchiveBytes, fixture, importSave, loadJson, openJson,
  originalCoins, originalFiles, originalSave, savePath, withCoins, editedCoins } from "./helpers";
import { editScalar, searchRow, resetAction } from "./action-helpers";

const boundLabel = "Restore search selection";

// No plugin opts into the new API yet. Add a synthetic sibling declaration to the real
// GenericJson view response, keeping the actual collection, planner, edits and exports.
async function bindSearchAction(page: Page) {
  await page.route(/\/api\/workspaces\/[^/]+\/(?:files\/[^/]+\/open|documents\/[^/]+)$/, async route => {
    const response = await route.fetch();
    const body = await response.json() as { view?: ViewNode };
    if (body.view) {
      let reset: ActionNode | undefined;
      function find(node: ViewNode) {
        if (node.type === "action" && node.label === resetAction) reset = node;
        if (node.type === "section") node.children.forEach(find);
        if (node.type === "tabs") node.tabs.forEach(tab => tab.children.forEach(find));
      }
      function bind(nodes: ViewNode[]): ViewNode[] {
        return nodes.flatMap<ViewNode>(node => {
          if (node.type === "section") return [{ ...node, children: bind(node.children) }];
          if (node.type === "tabs") return [{ ...node, tabs: node.tabs.map(tab => ({ ...tab, children: bind(tab.children) })) }];
          return node.type === "queryableTable" && node.title === "Whole-tree search" && reset
            ? [{ ...reset, label: boundLabel, selectionTableId: node.collectionId }, node] : [node];
        });
      }
      find(body.view);
      body.view = bind([body.view])[0]!;
    }
    await route.fulfill({ response, json: body });
  });
}

async function previewBound(page: Page) {
  const request = page.waitForRequest(request => request.url().endsWith("/actions/plan"));
  await page.getByRole("button", { name: boundLabel, exact: true }).click();
  const dialog = page.getByRole("dialog", { name: `Preview: ${boundLabel}`, exact: true });
  await expect(dialog.getByRole("button", { name: "Apply", exact: true })).toBeEnabled();
  return { dialog, scope: (await request).postDataJSON().scope };
}

test("bound sibling uses explicit selection and document fallback with real GenericJson planning", async ({ page }, testInfo) => {
  await bindSearchAction(page);
  await importSave(page);
  await openJson(page);
  await loadJson(page);
  await editCoins(page);
  await page.getByRole("tab", { name: "Search", exact: true }).click();
  await searchRow(page, "coins").getByRole("checkbox").check();
  await expect(page.getByRole("button", { name: boundLabel })).toHaveAccessibleDescription(/Whole-tree search/);
  const selected = await previewBound(page);
  expect(selected.scope).toEqual({ rowKeys: ["value/1"] });
  await expect(selected.dialog.getByText("1 selected row", { exact: true })).toBeVisible();
  await selected.dialog.getByRole("button", { name: "Cancel", exact: true }).click();
  expectArchiveBytes(await exportFiles(page, testInfo, "bound-cancel.zip"), withCoins(originalSave, originalCoins, editedCoins));
  await page.getByRole("button", { name: "Clear selection", exact: true }).click();
  const whole = await previewBound(page);
  expect(whole.scope).toEqual({});
  await expect(whole.dialog.getByText("Whole document", { exact: true })).toBeVisible();
  await whole.dialog.getByRole("button", { name: "Apply", exact: true }).click();
  await expect(whole.dialog).not.toBeVisible();
  expectArchiveBytes(await exportFiles(page, testInfo, "bound-document.zip"), originalSave);
});

test("bound sibling preserves query exclusions across unloaded pages, apply and undo", async ({ page }, testInfo) => {
  await bindSearchAction(page);
  const original = fixture("queryable.json");
  await importSave(page, { ...originalFiles, [savePath]: original });
  await openJson(page);
  await page.getByRole("tab", { name: "JSON", exact: true }).click();
  await page.getByRole("button", { name: "Load Entries", exact: true }).click();
  await editScalar(page, page.getByRole("textbox", { name: /^score_00\s*meaning unknown/ }), "900");
  await editScalar(page, page.getByRole("textbox", { name: /^score_55\s*meaning unknown/ }), "955");
  await page.getByRole("tab", { name: "Search", exact: true }).click();
  await page.getByRole("searchbox", { name: "Search Whole-tree search" }).fill("score_");
  await page.getByRole("button", { name: "Select all 60 filtered rows", exact: true }).click();
  await searchRow(page, "score_00").getByRole("checkbox").uncheck();
  const selected = await previewBound(page);
  expect(selected.scope.queryScope.query.text).toBe("score_");
  expect(selected.scope.queryScope.excludedRowKeys).toEqual(["value/1"]);
  await expect(selected.dialog.getByText("59 selected rows", { exact: true })).toBeVisible();
  await selected.dialog.getByRole("button", { name: "Apply", exact: true }).click();
  await expect(selected.dialog).not.toBeVisible();
  const firstOnly = Buffer.from(original.toString("utf8").replace('"score_00": 100', '"score_00": 900'));
  expectArchiveBytes(await exportFiles(page, testInfo, "bound-query.zip"), firstOnly);
  await page.getByRole("button", { name: "Undo", exact: true }).click();
  expectArchiveBytes(await exportFiles(page, testInfo, "bound-query-undo.zip"),
    Buffer.from(firstOnly.toString("utf8").replace('"score_55": 155', '"score_55": 955')));
});
