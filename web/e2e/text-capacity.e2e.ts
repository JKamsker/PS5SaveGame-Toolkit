import { test, expect, type Page } from "@playwright/test";
import type { SaveDocument, FieldNode } from "../src/api/protocol";
import { importSave, openJson } from "./helpers";
import { actionPreview, resetAction } from "./action-helpers";

// Authored protocol fixture over a real GenericJson import/open. This proves browser behavior;
// native storage/UTF-8 enforcement is covered by Core session tests, not this injected view.
async function captionFixture(page: Page) {
  await importSave(page);
  await openJson(page);
  const field: FieldNode = { type: "field", valueId: "caption", label: "Caption", value: "old",
    description: "Synthetic UTF-8 caption", spec: { kind: "text", maxUtf8Bytes: 4 } };
  await page.route(/\/api\/workspaces\/[^/]+\/documents\/[^/?]+$/, async route => {
    const response = await route.fetch();
    const document = await response.json() as SaveDocument;
    document.view = { type: "section", title: "Synthetic caption controls", children: [field,
      { type: "field", valueId: "readonly", label: "Readonly caption", value: "🎮", spec: { ...field.spec, readOnly: true } },
      { type: "table", columns: [{ key: "caption", label: "Table caption" }], rows: [
        { key: "row", cells: { caption: { ...field, valueId: "table-caption", label: "Table caption" } } },
      ] },
      { type: "action", actionId: "synthetic-caption", label: "Preview caption", supportsPlanning: true, parameters: [field] },
    ] };
    await route.fulfill({ response, json: document });
  });
  await page.reload();
  await expect(page.getByText("Synthetic caption controls", { exact: true })).toBeVisible();
}

async function expectNoPageOverflow(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  const dialog = page.getByRole("dialog");
  if (await dialog.isVisible()) {
    const bounds = await dialog.boundingBox();
    expect(bounds!.x).toBeGreaterThanOrEqual(0);
    expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(await page.evaluate(() => innerWidth));
  }
}

for (const width of [375, 768, 1024, 1440]) {
  test(`UTF-8 controls and stale action retain drafts at ${width}px (synthetic protocol fixture)`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await captionFixture(page);
    const caption = page.getByRole("textbox", { name: /^Caption$/ });
    await caption.fill("🎮a");
    await expect(caption).toHaveAttribute("aria-invalid", "true");
    await expect(page.getByText("5 / 4 UTF-8 bytes used · 1 byte over capacity")).toBeVisible();
    await caption.press("Escape");
    await expect(caption).toHaveValue("old");
    await page.getByRole("button", { name: "Preview caption", exact: true }).scrollIntoViewIfNeeded();
    const tableCounter = page.locator(".table__cell .text-capacity");
    await expect(tableCounter.getByText("3/4 B · 1 left", { exact: true })).toBeVisible();
    expect(await tableCounter.evaluate(element => element.getBoundingClientRect().bottom <= element.closest(".table__cell")!.getBoundingClientRect().bottom)).toBe(true);
    await expectNoPageOverflow(page);
    await page.screenshot({ path: testInfo.outputPath(`controls-${width}.png`), fullPage: true });

    await page.route("**/actions/plan", route => {
      const request = route.request().postDataJSON() as { parameters: { caption: string }; revision: number };
      expect(request.parameters.caption).toBe("🎮");
      return route.fulfill({ json: { planId: "synthetic-plan", revision: request.revision, scope: {},
        plan: { errors: [], warnings: [], skippedRows: [], changes: [
          { valueId: "caption", label: "Caption", oldValue: "old", newValue: "🎮", oldExists: true, newExists: true, derived: false },
        ] } } });
    });
    let applies = 0;
    await page.route("**/actions/apply", route => {
      applies++;
      return route.fulfill({ status: 409, json: { error: "Synthetic metadata changed", code: "stale" } });
    });
    const trigger = page.getByRole("button", { name: "Preview caption", exact: true });
    await trigger.click();
    const dialog = page.getByRole("dialog", { name: "Preview: Preview caption" });
    await dialog.getByRole("textbox", { name: "Caption" }).fill("🎮");
    await dialog.getByRole("button", { name: "Preview", exact: true }).click();
    await dialog.getByRole("button", { name: "Apply", exact: true }).click();
    await expect(dialog.getByRole("alert")).toContainText("This preview is stale");
    await expect(dialog.getByRole("alert")).toBeFocused();
    await expect(dialog.getByRole("textbox", { name: "Caption" })).toHaveValue("🎮");
    await expectNoPageOverflow(page);
    await page.screenshot({ path: testInfo.outputPath(`stale-${width}.png`), fullPage: true });
    await dialog.getByRole("button", { name: "Preview again" }).click();
    await expect(dialog.getByRole("button", { name: "Apply", exact: true })).toBeEnabled();
    await page.keyboard.press("Escape");
    await expect(dialog).not.toBeVisible();
    await expect(trigger).toBeFocused();
    expect(applies).toBe(1);
  });
}

test("a real GenericJson no-op preview announces no changes and never applies", async ({ page }) => {
  await importSave(page);
  await openJson(page);
  let applies = 0;
  page.on("request", request => { if (request.url().endsWith("/actions/apply")) applies++; });
  const trigger = page.getByRole("button", { name: resetAction, exact: true });
  await trigger.click();
  const dialog = actionPreview(page);
  await expect(dialog.getByRole("status")).toHaveText("No changes. No undo step will be created.");
  await expect(dialog.getByRole("button", { name: "Apply", exact: true })).toBeDisabled();
  await dialog.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(trigger).toBeFocused();
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeDisabled();
  expect(applies).toBe(0);
});
