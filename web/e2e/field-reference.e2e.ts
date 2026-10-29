import { expect, test } from "@playwright/test";
import type { ViewNode } from "../src/api/protocol";
import { coinsField, exportFiles, expectArchiveBytes, importSave, loadJson, openJson, originalSave, originalCoins, withCoins } from "./helpers";

test("canonical presentations share keyboard edits and undo at 390px without changing untouched export bytes", async ({ page }, testInfo) => {
  // The framework owns normalization; add its ordinary-field output to a real GenericJson response.
  // Core fixtures separately exercise actual FieldReferenceNodes across eager/lazy/query boundaries.
  await page.route(/\/api\/workspaces\/[^/]+\/documents\/[^/]+\/nodes\?/, async route => {
    const response = await route.fetch();
    const nodes = await response.json() as ViewNode[];
    function project(node: ViewNode): ViewNode[] {
      if (node.type === "field" && node.label === "coins") return [node,
        { ...node, label: "Named coin balance" },
        { type: "fieldReference", targetValueId: "missing", label: "Removed balance", description: "Original field is unavailable." }];
      if (node.type === "section" && !node.lazyId) return [{ ...node, children: node.children.flatMap(project) }];
      return [node];
    }
    await route.fulfill({ response, json: nodes.flatMap(project) });
  });
  await page.setViewportSize({ width: 390, height: 844 });
  await importSave(page);
  await openJson(page);
  await loadJson(page);
  const canonical = coinsField(page);
  const named = page.getByRole("textbox", { name: /^Named coin balance\s*meaning unknown/ });
  await expect(named).toHaveValue(originalCoins);
  expect(await canonical.getAttribute("id")).not.toEqual(await named.getAttribute("id"));
  await expect(page.getByText(/Its canonical target may be unloaded, missing, or removed/)).toBeVisible();
  expectArchiveBytes(await exportFiles(page, testInfo, "untouched-projection.zip"), originalSave);
  await named.focus();
  await named.fill("99");
  await page.keyboard.press("Enter");
  await expect(canonical).toHaveValue("99");
  await expect(named).toBeFocused();
  await expect(named).toHaveAccessibleName(/modified/);
  await expect(named).toHaveAccessibleDescription(/Not validated on console/);
  await expect(named).toHaveAccessibleDescription(/Source UTF-8 token/);
  expectArchiveBytes(await exportFiles(page, testInfo, "edited-projection.zip"), withCoins(originalSave, originalCoins, "99"));
  await page.getByRole("button", { name: "Undo", exact: true }).click();
  await expect(named).toHaveValue(originalCoins);
  await expect(canonical).toHaveValue(originalCoins);
  await page.getByRole("button", { name: "Redo", exact: true }).click();
  await expect(named).toHaveValue("99");
  const fieldBounds = await named.boundingBox();
  expect(fieldBounds).not.toBeNull();
  expect(fieldBounds!.x).toBeGreaterThanOrEqual(0);
  expect(fieldBounds!.x + fieldBounds!.width).toBeLessThanOrEqual(390);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
});
