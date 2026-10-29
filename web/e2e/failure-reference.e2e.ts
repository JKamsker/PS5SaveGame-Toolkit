import type { Page } from "@playwright/test";
import type { FieldNode, ReferenceRecordPair, SaveDocument } from "../src/api/protocol";
import { failures, test, expect } from "./failure-harness";
import { importSave, openJson } from "./helpers";

/** GenericJson has no catalog fields. Project a read-only synthetic field into its real
 * document view, exercising the built renderer, batch client and picker without samples. */
async function syntheticReference(page: Page, compact = false) {
  await page.route(/\/api\/workspaces\/[^/]+\/(?:files\/[^/]+\/open|documents\/[^/]+)$/, async route => {
    const response = await route.fetch();
    const body = await response.json() as Partial<SaveDocument>;
    const field: FieldNode = { type: "field", valueId: "synthetic-reference", label: "Synthetic item", value: "1001",
      spec: { kind: "reference", catalogId: "synthetic-items", readOnly: true } };
    if (body.view) body.view = { type: "section", title: "Synthetic reference fixture", children: [body.view, compact
      ? { type: "table", title: "Synthetic references", columns: [{ key: "item", label: "Item", width: "Narrow" }, { key: "note", label: "Notes", width: "Wide" }],
        rows: [{ key: "synthetic-item", cells: { item: field, note: { type: "text", text: "Synthetic reference row" } } }] }
      : field,
    ] };
    await route.fulfill({ response, json: body });
  });
  const candidate = { rawId: "1001", label: "Potion", eligibility: { available: true, eligible: true, selectable: true, reasons: [] } };
  await page.route("**/reference-fields/records", async route => {
    const body = route.request().postDataJSON() as { revision: number; pairs: ReferenceRecordPair[] };
    await route.fulfill({ json: { revision: body.revision, results: body.pairs.map(pair => ({ ...pair, status: 200, candidate })) } });
  });
  await page.route("**/reference-fields/candidates?*", route => route.fulfill({ json: {
    catalogId: "synthetic-items", revision: 0, items: [candidate], total: 1, offset: 0, limit: 50, categories: [],
  } }));
}

for (const failure of failures) {
  test(`reference batch ${failure.name} exposes a retry while preserving the raw ID and picker`, async ({ page, faults }) => {
    await syntheticReference(page);
    const blocked = await faults.block("**/reference-fields/records", failure);
    await importSave(page);
    await openJson(page);
    const trigger = page.locator(".reference-trigger");
    await expect(trigger).toHaveText("1001");
    await expect(page.getByRole("alert")).toContainText(failure.message);
    await trigger.click();
    const picker = page.getByRole("dialog", { name: "Browse items", exact: true });
    await expect(picker.getByRole("option", { name: /Potion/ })).toBeVisible();
    await expect(picker.getByText("Current ID:", { exact: false })).toContainText("1001");
    await picker.getByRole("button", { name: "Close", exact: true }).click();
    await expect(trigger).toBeFocused();
    blocked.release();
    await page.getByRole("button", { name: "Retry reference label", exact: true }).click();
    await expect(trigger).toHaveText("Potion (1001)");
    await expect(trigger).toBeFocused();
    await expect(page.getByRole("alert")).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeDisabled();
    expect(blocked.count()).toBeGreaterThan(0);
  });
}

test("compact reference cells keep the failed label retry visible and clickable", async ({ page, faults }) => {
  await syntheticReference(page, true);
  const blocked = await faults.block("**/reference-fields/records", failures[0]);
  await importSave(page);
  await openJson(page);
  const table = page.getByRole("table", { name: "Synthetic references", exact: true });
  await expect(table.getByRole("alert")).toContainText(failures[0].message);
  await table.scrollIntoViewIfNeeded();
  const retry = table.getByRole("button", { name: "Retry reference label", exact: true });
  await expect(retry).toBeInViewport({ ratio: 1 });
  blocked.release();
  await retry.click();
  await expect(table.locator(".reference-trigger")).toHaveText("Potion (1001)");
  await expect(table.locator(".reference-trigger")).toBeFocused();
});
