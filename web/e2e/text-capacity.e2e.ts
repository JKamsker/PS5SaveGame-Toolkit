import { test, expect, type Page } from "@playwright/test";
import type { SaveDocument, FieldNode } from "../src/api/protocol";
import { importSave, openJson } from "./helpers";
import { actionPreview, resetAction } from "./action-helpers";

// Authored protocol fixture over a real GenericJson import/open. This proves browser behavior;
// native storage/UTF-8 enforcement is covered by Core session tests, not this injected view.
async function captionFixture(page: Page, maxUtf8Bytes: number | null = 4, canEdit = true) {
  await importSave(page);
  await openJson(page);
  const field: FieldNode = { type: "field", valueId: "caption", label: "Caption", value: "old",
    description: "Synthetic UTF-8 caption", spec: { kind: "text", maxUtf8Bytes } };
  let serverRevision: number | undefined;
  let serverCaption = "🎮";
  await page.route(/\/api\/workspaces\/[^/]+\/documents\/[^/?]+$/, async route => {
    const response = await route.fetch();
    const document = await response.json() as SaveDocument;
    document.canEdit = canEdit;
    document.state = { ...document.state, revision: serverRevision ??= document.state.revision };
    document.view = { type: "section", title: "Synthetic caption controls", children: [field,
      { type: "field", valueId: "readonly", label: "Readonly caption", value: serverCaption, spec: { ...field.spec, readOnly: true } },
      { type: "field", valueId: "nullable", label: "Nullable caption", value: "🎮",
        spec: { kind: "nullable", inner: { kind: "text", maxUtf8Bytes } } },
      { type: "field", valueId: "union", label: "Union caption", value: { kind: "union", type: "text", value: "🎮" },
        spec: { kind: "union", options: [{ id: "text", label: "Text", spec: { kind: "text", maxUtf8Bytes } }] } },
      { type: "table", columns: [{ key: "caption", label: "Table caption" }], rows: [
        { key: "row", cells: { caption: { ...field, valueId: "table-caption", label: "Table caption" } } },
      ] },
      { type: "action", actionId: "synthetic-caption", label: "Preview caption", supportsPlanning: true, parameters: [field] },
    ] };
    await route.fulfill({ response, json: document });
  });
  await page.reload();
  await expect(page.getByText("Synthetic caption controls", { exact: true })).toBeVisible();
  return {
    get revision() { return serverRevision!; },
    advanceRevision() { serverRevision!++; serverCaption = "new"; },
  };
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

async function installCaptionPlans(page: Page, fixture: Awaited<ReturnType<typeof captionFixture>>) {
  const revisions: number[] = [];
  await page.route("**/actions/plan", route => {
    const request = route.request().postDataJSON() as { parameters: { caption: string }; revision: number };
    expect(request.parameters.caption).toBe("🎮");
    expect(request.revision).toBe(fixture.revision);
    revisions.push(request.revision);
    return route.fulfill({ json: { planId: "synthetic-plan", revision: request.revision, scope: {},
      plan: { errors: [], warnings: [], skippedRows: [], changes: [
        { valueId: "caption", label: "Caption", oldValue: "old", newValue: "🎮", oldExists: true, newExists: true, derived: false },
      ] } } });
  });
  return revisions;
}

async function installStaleFlow(page: Page, failure: string, fixture: Awaited<ReturnType<typeof captionFixture>>) {
  const revisions = await installCaptionPlans(page, fixture);
  let applies = 0;
  await page.route("**/actions/apply", route => {
    applies++;
    fixture.advanceRevision();
    return route.fulfill({ status: 409, json: failure === "exception" ? { error: "Synthetic metadata changed", code: "stale" } : {
      ok: false, stale: true, errors: [{ valueId: "", message: "Synthetic metadata changed" }], changes: [], warnings: [], structureChanged: false,
      state: { revision: fixture.revision, canUndo: false, canRedo: false, isDirty: false },
    } });
  });
  return { revisions, get applies() { return applies; } };
}

for (const width of [375, 768, 1024, 1440]) {
  for (const failure of ["envelope", "exception"]) {
    test(`UTF-8 controls and stale ${failure} retain drafts at ${width}px (synthetic protocol fixture)`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width, height: 900 });
      const fixture = await captionFixture(page);
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

      const initialRevision = fixture.revision;
      const flow = await installStaleFlow(page, failure, fixture);
      const trigger = page.getByRole("button", { name: "Preview caption", exact: true });
      await trigger.click();
      const dialog = page.getByRole("dialog", { name: "Preview: Preview caption" });
      await dialog.getByRole("textbox", { name: "Caption" }).fill("🎮");
      await dialog.getByRole("button", { name: "Preview", exact: true }).click();
      await dialog.getByRole("button", { name: "Apply", exact: true }).click();
      await expect(dialog.getByRole("alert")).toContainText("This preview is stale");
      await expect(dialog.getByRole("alert")).toBeFocused();
      await expect(dialog.getByRole("textbox", { name: "Caption" })).toHaveValue("🎮");
      // Count background nodes too: native modal inertness must not hide duplicate live announcements.
      await expect(page.locator('[role="alert"]')).toHaveCount(1);
      await expect(page.locator(".toast")).toHaveCount(0);
      await expect(page.locator(".field").filter({ has: page.getByText("Readonly caption", { exact: true }) }).locator(".readonly-value")).toHaveText("new");
      await expectNoPageOverflow(page);
      await page.screenshot({ path: testInfo.outputPath(`stale-${width}.png`), fullPage: true });
      await dialog.getByRole("button", { name: "Preview again" }).click();
      await expect(dialog.getByRole("button", { name: "Apply", exact: true })).toBeEnabled();
      expect(flow.revisions).toEqual([initialRevision, initialRevision + 1]);
      await page.keyboard.press("Escape");
      await expect(dialog).not.toBeVisible();
      await expect(trigger).toBeFocused();
      expect(flow.applies).toBe(1);
    });
  }
}

for (const code of [undefined, "conflict"]) {
  test(`ordinary 409 stays non-stale with one dialog diagnostic (code=${code})`, async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 900 });
    const fixture = await captionFixture(page);
    const revisions = await installCaptionPlans(page, fixture);
    await page.route("**/actions/apply", route => route.fulfill({ status: 409,
      json: { error: "Action conflicts with current settings", ...(code === undefined ? {} : { code }) } }));
    const trigger = page.getByRole("button", { name: "Preview caption", exact: true });
    await trigger.click();
    const dialog = page.getByRole("dialog");
    await dialog.getByRole("textbox", { name: "Caption" }).fill("🎮");
    await dialog.getByRole("button", { name: "Preview", exact: true }).click();
    await dialog.getByRole("button", { name: "Apply", exact: true }).click();
    await expect(dialog.getByRole("alert")).toHaveText("Action conflicts with current settings");
    await expect(dialog.getByRole("alert")).toBeFocused();
    await expect(dialog.getByRole("button", { name: "Preview again" })).toHaveCount(0);
    await expect(dialog.getByRole("button", { name: "Preview", exact: true })).toBeEnabled();
    await expect(dialog.getByRole("textbox", { name: "Caption" })).toHaveValue("🎮");
    await expect(page.locator('[role="alert"]')).toHaveCount(1);
    await expect(page.locator(".toast")).toHaveCount(0);
    expect(revisions).toEqual([fixture.revision]);
    await page.keyboard.press("Escape");
    await expect(trigger).toBeFocused();
  });
}

test("explicit-null capacities remain legacy in document and planned-action controls", async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 900 });
  await captionFixture(page, null);
  await expect(page.locator(".text-capacity")).toHaveCount(0);
  await page.route("**/actions/plan", route => {
    const request = route.request().postDataJSON() as { parameters: { caption: string }; revision: number };
    expect(request.parameters.caption).toBe("🎮🎮");
    return route.fulfill({ json: { planId: "legacy-plan", revision: request.revision, scope: {},
      plan: { errors: [], warnings: [], skippedRows: [], changes: [] } } });
  });
  const caption = page.getByRole("textbox", { name: /^Caption$/ });
  await caption.fill("🎮🎮");
  await expect(caption).toHaveAttribute("aria-invalid", "false");
  await caption.press("Escape");
  const trigger = page.getByRole("button", { name: "Preview caption", exact: true });
  await trigger.click();
  const dialog = page.getByRole("dialog");
  await dialog.getByRole("textbox", { name: "Caption" }).fill("🎮🎮");
  await dialog.getByRole("button", { name: "Preview", exact: true }).click();
  await expect(dialog.getByRole("status")).toHaveText("No changes. No undo step will be created.");
  await expect(page.locator(".text-capacity")).toHaveCount(0);
  await dialog.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(trigger).toBeFocused();
});

test("document-disabled nullable and union captions retain inspection counters without editors", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 375, height: 900 });
  await captionFixture(page, 4, false);
  await expect(page.locator(".field").getByRole("textbox")).toHaveCount(0);
  await expect(page.locator(".field").getByRole("combobox")).toHaveCount(0);
  for (const label of ["Nullable caption", "Union caption"]) {
    const field = page.locator(".field").filter({ has: page.getByText(label, { exact: true }) });
    const hint = field.locator(".text-capacity");
    await expect(hint).toHaveText("4 / 4 UTF-8 bytes used · 0 bytes remaining");
    await hint.scrollIntoViewIfNeeded();
    await expect(hint).toBeInViewport();
  }
  await expectNoPageOverflow(page);
  await page.screenshot({ path: testInfo.outputPath("readonly-nested-375.png"), fullPage: true });
});

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
