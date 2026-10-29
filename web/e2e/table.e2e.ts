import { test, expect } from "@playwright/test";
import { fixture, importSave, openJson, savePath } from "./helpers";

test("queryable search, column filters, sorting and paging cover unloaded rows", async ({ page }) => {
  await importSave(page, { [savePath]: fixture("queryable.json") });
  await openJson(page);
  await page.getByRole("tab", { name: "Search", exact: true }).click();
  const table = page.getByRole("table", { name: "Whole-tree search", exact: true });
  const rows = table.getByRole("row");
  const paths = table.getByRole("cell").filter({ hasText: /^\$\["score_\d{2}"\]#1$/ });
  const expectedPaths = Array.from({ length: 60 }, (_, index) => `$["score_${String(index).padStart(2, "0")}"]#1`);
  const pages = page.getByRole("navigation", { name: "Whole-tree search pages" });
  const count = page.getByText("64 of 64 rows", { exact: true });
  await expect(count).toBeVisible();
  await expect(rows).toHaveCount(51); // Header plus the first 50 of 64 JSON nodes.
  await expect(table).toHaveAttribute("aria-rowcount", "65");

  await page.getByRole("textbox", { name: "Filter JSON type", exact: true }).fill("number");
  await expect(page.getByText("60 of 64 rows", { exact: true })).toBeVisible();
  await page.getByRole("combobox", { name: "Sort rows" }).selectOption({ label: "Occurrence path" });
  await expect(paths).toHaveText(expectedPaths.slice(0, 50));

  await pages.getByRole("button", { name: "Next", exact: true }).click();
  await expect(pages.getByText("Page 2", { exact: true })).toBeVisible();
  await expect(rows).toHaveCount(11);
  await expect(paths).toHaveText(expectedPaths.slice(50));
  await expect(table).toHaveAttribute("aria-rowcount", "61");
  await expect(pages.getByRole("button", { name: "Next", exact: true })).toBeDisabled();
  await expect(pages.getByRole("button", { name: "Previous", exact: true })).toBeEnabled();
  await expect(page.getByText("60 of 64 rows", { exact: true })).toBeVisible();

  // Sorting from the second page must reset paging and operate on the full collection.
  await page.getByRole("button", { name: "Ascending", exact: true }).click();
  await expect(pages.getByText("Page 1", { exact: true })).toBeVisible();
  await expect(rows).toHaveCount(51);
  await expect(paths).toHaveText(expectedPaths.toReversed().slice(0, 50));

  const search = page.getByRole("searchbox", { name: "Search Whole-tree search" });
  await search.fill("score_0");
  await expect(page.getByText("10 of 64 rows", { exact: true })).toBeVisible();
  await expect(rows).toHaveCount(11);
  await expect(paths).toHaveText(expectedPaths.slice(0, 10).toReversed());
  await expect(pages.getByRole("button", { name: "Next", exact: true })).toBeDisabled();
  await expect(pages.getByRole("button", { name: "Previous", exact: true })).toBeDisabled();

  await search.fill("no-such-synthetic-value");
  await expect(page.getByText("0 of 64 rows", { exact: true })).toBeVisible();
  await expect(page.getByText("No rows match the current filters.", { exact: true })).toBeVisible();
  await expect(rows).toHaveCount(1);
  await search.clear();
  await expect(page.getByText("60 of 64 rows", { exact: true })).toBeVisible();
  await expect(paths).toHaveText(expectedPaths.toReversed().slice(0, 50));
});

// #40: an empty exact-match filter is still sent as { type: "" }; GenericJson
// matches it literally, hiding every row. Clearing the UI must remove the filter.
test("clearing a JSON-type filter restores all rows (#40)", async ({ page }) => {
  await importSave(page, { [savePath]: fixture("queryable.json") });
  await openJson(page);
  await page.getByRole("tab", { name: "Search", exact: true }).click();
  await expect(page.getByText("64 of 64 rows", { exact: true })).toBeVisible();
  await page.getByRole("textbox", { name: "Filter JSON type", exact: true }).fill("number");
  await expect(page.getByText("60 of 64 rows", { exact: true })).toBeVisible();
  await page.getByRole("textbox", { name: "Filter JSON type", exact: true }).clear();
  await expect(page.getByText("64 of 64 rows", { exact: true })).toBeVisible();
  await expect(page.getByRole("table", { name: "Whole-tree search", exact: true }).getByRole("row")).toHaveCount(51);
});
