import { expect, type Locator, type Page } from "@playwright/test";

export const resetAction = "Reset existing values to imported values";

export function actionPreview(page: Page) {
  return page.getByRole("dialog", { name: `Preview: ${resetAction}`, exact: true });
}

export function searchTable(page: Page) {
  return page.getByRole("table", { name: "Whole-tree search", exact: true });
}

export function searchRow(page: Page, key: string) {
  return searchTable(page).getByRole("row").filter({
    has: page.getByRole("cell", { name: `$["${key}"]#1`, exact: true }),
  });
}

export async function openTableReset(page: Page, key: string) {
  // Click the real action inside a table cell: moving it outside the selection
  // provider must fail these tests even if the plugin's planner remains correct.
  await searchRow(page, key).getByRole("button", { name: `${resetAction}: $["${key}"]#1`, exact: true }).click();
}

export async function editScalar(page: Page, field: Locator, value: string, multiline = false) {
  await field.fill(value);
  const editing = page.waitForResponse(response => response.url().endsWith("/edits") && response.request().method() === "POST");
  await field.press(multiline ? "Tab" : "Enter");
  expect((await editing).status()).toBe(200);
  await expect(field).toHaveAccessibleName(/modified/);
  await expect(field).toHaveValue(value);
}

export async function expectPreview(page: Page, scope: string, changes: { key: string; before: string; after: string }[]) {
  const preview = actionPreview(page);
  await expect(preview.getByText(scope, { exact: true })).toBeVisible();
  await expect(preview.getByRole("status")).toHaveText(changes.length === 0
    ? "No changes. No undo step will be created."
    : `${changes.length} field changes · 0 skipped rows · one undo step`);
  const table = preview.getByRole("table", { name: "Preview changes", exact: true });
  await expect(table.getByRole("row")).toHaveCount(changes.length + 1);
  for (const change of changes) {
    const row = table.getByRole("row").filter({ has: page.getByRole("rowheader").filter({ hasText: `$["${change.key}"]#1` }) });
    await expect(row.getByRole("cell").nth(0)).toHaveText(change.before);
    await expect(row.getByRole("cell").nth(1)).toHaveText(change.after);
  }
  const apply = preview.getByRole("button", { name: "Apply", exact: true });
  if (changes.length === 0) await expect(apply).toBeDisabled();
  else await expect(apply).toBeEnabled();
}

export async function closePreview(page: Page, button: "Cancel" | "Apply") {
  const preview = actionPreview(page);
  await preview.getByRole("button", { name: button, exact: true }).click();
  await expect(preview).not.toBeVisible();
}
