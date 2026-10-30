import { expect, type Locator, type Page, type TestInfo } from "@playwright/test";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { fileURLToPath } from "node:url";
import { tmpdir } from "node:os";
import path from "node:path";
import type { ActionPreview } from "../src/api/actionReview";
import type { EditResult, SaveDocument } from "../src/api/protocol";
import { archive, expectFiles, exportFiles, home, upload, type SaveFiles } from "./helpers";

export interface GearboxFixture { files: SaveFiles; serial: string; expected: Record<string, SaveFiles> }
interface EncodedFixture { files: Record<string, string>; serial: string; expected: Record<string, Record<string, string>> }
const importAction = "Import serial into backpack / bank";
const widths = [375, 768, 1024, 1440] as const;

function decode(files: Record<string, string>): SaveFiles {
  return Object.fromEntries(Object.entries(files).map(([path, bytes]) => [path, Buffer.from(bytes, "base64")]));
}

export async function generateGearboxFixtures(): Promise<GearboxFixture[]> {
  const { stdout } = await promisify(execFile)("dotnet", ["run", "--project", "plugins/Gearbox/Gearbox.BrowserFixture",
    "--configuration", "Release", "--artifacts-path", path.join(tmpdir(), "gearbox-fixture-build")],
  { cwd: fileURLToPath(new URL("../..", import.meta.url)), timeout: 60_000, maxBuffer: 2 * 1024 * 1024 });
  const line = stdout.split(/\r?\n/).find(line => line.startsWith("GEARBOX_FIXTURE:"));
  expect(line).toBeTruthy();
  const encoded = JSON.parse(line!.slice("GEARBOX_FIXTURE:".length)) as EncodedFixture[];
  return encoded.map(fixture => ({ files: decode(fixture.files), serial: fixture.serial,
    expected: Object.fromEntries(Object.entries(fixture.expected).map(([action, files]) => [action, decode(files)])) }));
}

async function openGearbox(page: Page, files: SaveFiles, name = "1") {
  await home(page);
  await upload(page, archive(files), "authored-gearbox.zip");
  const rail = page.getByRole("navigation", { name: "Save directories" });
  await rail.getByRole("button").filter({ hasText: "EFFECTS" }).first().click();
  const opening = page.waitForResponse(response => response.url().endsWith("/open") && response.request().method() === "POST");
  await rail.locator("button.file").filter({ has: page.getByText(name, { exact: true }) }).click();
  const response = await opening;
  expect(response.ok()).toBe(true);
  const document = await response.json() as SaveDocument;
  expect(document.plugin.id).toBe("gearbox");
  return new URL(response.url()).pathname.replace(/\/files\/[^/]+\/open$/, `/documents/${document.id}`);
}

async function previewImport(page: Page, serial: string) {
  const trigger = page.getByRole("button", { name: importAction, exact: true });
  await trigger.click();
  const dialog = page.getByRole("dialog", { name: `Preview: ${importAction}`, exact: true });
  await dialog.getByRole("textbox", { name: "Serial code for this game", exact: true }).fill(serial);
  const planning = page.waitForResponse(response => response.url().endsWith("/actions/plan"));
  await dialog.getByRole("button", { name: "Preview", exact: true }).click();
  const response = await planning;
  expect(response.status()).toBe(200);
  const preview = await response.json() as ActionPreview;
  expect(preview.plan.errors).toEqual([]);
  expect(preview.plan.changes.map(change => change.valueId)).toEqual(["inventory_items/count", "import/serial"]);
  expect(preview.plan.changes[0]).toMatchObject({ oldValue: "2", newValue: "3", derived: true });
  expect(preview.plan.changes[0]!.effect).toBeUndefined();
  expect(preview.plan.changes[1]).toMatchObject({ oldValue: null, newValue: serial, oldExists: false, newExists: true,
    effect: { kind: "StructuralMemento", spec: { kind: "text", readOnly: true }, path: ["tab:Inventory", "table:inventory_items"] } });
  return { dialog, preview, trigger };
}

async function assertResponsiveEffects(page: Page, dialog: Locator, serial: string) {
  const region = dialog.getByRole("region", { name: "Action effects", exact: true });
  await expect(dialog.getByRole("status")).toHaveText("2 field changes · 0 skipped rows · one undo step");
  await expect(region.locator("tbody tr")).toHaveCount(1);
  // Production absence is represented by its existence bit, never an existing explicit null.
  await expect(region.locator("summary").filter({ hasText: /^\(absent\)$/ })).toBeVisible();
  await expect(region.getByText("null (explicit)", { exact: true })).toHaveCount(0);
  await expect(region).toContainText("Gearbox validated serial structural action");
  await expect(region).toContainText("tab:Inventory / table:inventory_items");
  await expect(region.locator("input, textarea, select")).toHaveCount(0);
  const payload = region.locator("details").nth(1);
  await payload.locator("summary").click();
  await expect(payload.locator("pre")).toHaveText(`text: ${serial}`);
  for (const width of widths) {
    await page.setViewportSize({ width, height: 800 });
    await region.focus();
    await expect(region).toBeFocused();
    await expect(region).toBeInViewport({ ratio: 0.5 });
    const box = await dialog.boundingBox();
    expect(box!.x).toBeGreaterThanOrEqual(0);
    expect(box!.x + box!.width).toBeLessThanOrEqual(width + 1);
    const overflow = await dialog.evaluate(element => element.scrollWidth - element.clientWidth);
    expect(overflow).toBeLessThanOrEqual(1);
    const apply = dialog.getByRole("button", { name: "Apply", exact: true });
    // Exercise native tab navigation through the real effect details to the committed action.
    const stops = await dialog.locator("summary,button:not(:disabled),[tabindex='0']").count();
    for (let tab = 0; tab <= stops; tab++) {
      await page.keyboard.press("Shift+Tab");
      if (await apply.evaluate(element => element === document.activeElement)) break;
    }
    await expect(apply).toBeFocused();
    await expect(apply).toBeInViewport();
    await apply.click({ trial: true });
  }
}

async function history(page: Page, operation: "Undo" | "Redo") {
  const response = page.waitForResponse(response => response.url().endsWith(`/${operation.toLowerCase()}`));
  await page.getByRole("button", { name: operation, exact: true }).click();
  expect((await response).ok()).toBe(true);
}

async function requestPlan(page: Page, document: string, actionId: string, rowKeys: string[], revision?: number) {
  const response = await page.request.post(`${document}/actions/plan`, { data: { actionId, parameters: {}, scope: { rowKeys }, revision } });
  return { response, preview: await response.json() as ActionPreview };
}

export async function productionGearboxImport(page: Page, testInfo: TestInfo, fixture: GearboxFixture) {
  const document = await openGearbox(page, fixture.files);
  await page.getByRole("tab", { name: "Inventory", exact: true }).click();
  const first = await previewImport(page, fixture.serial);
  await assertResponsiveEffects(page, first.dialog, fixture.serial);
  await first.dialog.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(first.trigger).toBeFocused();
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeDisabled();
  expectFiles(await exportFiles(page, testInfo, "cancelled-import.zip"), fixture.files);
  const second = await previewImport(page, fixture.serial);
  expect(second.preview.plan).toEqual(first.preview.plan);
  const applying = page.waitForResponse(response => response.url().endsWith("/actions/apply"));
  await second.dialog.getByRole("button", { name: "Apply", exact: true }).click();
  const applied = await (await applying).json() as EditResult;
  expect(applied.ok).toBe(true);
  expect(applied.changes).toEqual(second.preview.plan.changes.map(({ valueId, oldValue, newValue }) => ({ valueId, oldValue, newValue })));
  await expect(second.dialog).not.toBeVisible();
  const exported = await exportFiles(page, testInfo, "imported.zip");
  expectFiles(exported, fixture.expected.import!);
  const stale = await page.request.post(`${document}/actions/apply`, { data: { planId: second.preview.planId, revision: second.preview.revision } });
  expect(stale.status()).toBe(409);
  expectFiles(await exportFiles(page, testInfo, "stale-import.zip"), exported);
  await history(page, "Undo");
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeDisabled();
  expectFiles(await exportFiles(page, testInfo, "undo-import.zip"), fixture.files);
  await history(page, "Redo");
  expectFiles(await exportFiles(page, testInfo, "redo-import.zip"), exported);
  // Reimport the full exported dump and let the production parser re-open every changed byte.
  await openGearbox(page, exported);
  await page.getByRole("tab", { name: "Inventory", exact: true }).click();
  await expect(page.getByRole("table", { name: "Backpack", exact: true }).getByRole("row")).toHaveCount(4);
  expectFiles(await exportFiles(page, testInfo, "reopened-import.zip"), exported);
}

const transfers = [
  { action: "deposit", button: "Move selected backpack items to bank", table: "Backpack", source: "character/inventory_items/item-0",
    target: "profile/bank_inventory_list/character:item-0", counts: ["character/inventory_items/count", "profile/bank_inventory_list/count"], extra: ["character/slots/0/index", "character/slots/1/index"] },
  { action: "withdraw", button: "Move selected bank items to backpack", table: "Bank", source: "profile/bank_inventory_list/item-0",
    target: "character/inventory_items/profile:item-0", counts: ["profile/bank_inventory_list/count", "character/inventory_items/count"], extra: [] },
  { action: "claim", button: "Claim selected lost-loot items", table: "Lost loot", source: "profile/lost_loot_inventory_list/item-2",
    target: "character/inventory_items/profile:item-2", counts: ["profile/lost_loot_inventory_list/count", "character/inventory_items/count"], extra: [] },
];
type Transfer = (typeof transfers)[number];

async function previewTransfer(page: Page, fixture: GearboxFixture, transfer: Transfer) {
  // The UI uses the provider's actual selected table and composite row identity.
  await page.getByRole("tab", { name: "Inventory", exact: true }).last().click();
  const table = page.getByRole("table", { name: transfer.table, exact: true });
  await table.getByRole("checkbox").nth(1).check();
  const planning = page.waitForResponse(response => response.url().endsWith("/actions/plan"));
  await page.getByRole("button", { name: transfer.button, exact: true }).click();
  const planned = await planning;
  expect(planned.status()).toBe(200);
  const preview = await planned.json() as ActionPreview;
  expect(preview.plan.errors).toEqual([]);
  expect(preview.scope.rowKeys).toEqual([transfer.source]);
  expect(preview.plan.changes.map(change => change.valueId)).toEqual([transfer.source, transfer.target, ...transfer.counts, ...transfer.extra]);
  expect(preview.plan.changes.filter(change => change.effect)).toHaveLength(2);
  expect(preview.plan.changes[0]).toMatchObject({ oldValue: fixture.serial, newValue: null, newExists: false });
  expect(preview.plan.changes[1]).toMatchObject({ oldValue: null, newValue: fixture.serial, oldExists: false });
  const dialog = page.getByRole("dialog", { name: `Preview: ${transfer.button}`, exact: true });
  const effects = dialog.getByRole("region", { name: "Action effects", exact: true });
  await expect(effects.locator("tbody tr")).toHaveCount(2);
  await expect(effects.locator("summary").filter({ hasText: /^\(absent\)$/ })).toHaveCount(2);
  await expect(effects).toContainText("section:Shared profile / bank");
  await expect(effects).toContainText("section:Character progression / backpack");
  return { dialog, preview, table };
}

async function exerciseTransfer(page: Page, testInfo: TestInfo, fixture: GearboxFixture, document: string, transfer: Transfer) {
  const { dialog, preview, table } = await previewTransfer(page, fixture, transfer);
  const applying = page.waitForResponse(response => response.url().endsWith("/actions/apply"));
  await dialog.getByRole("button", { name: "Apply", exact: true }).click();
  const result = await (await applying).json() as EditResult;
  expect(result.ok).toBe(true);
  expect(result.changes).toEqual(preview.plan.changes.map(({ valueId, oldValue, newValue }) => ({ valueId, oldValue, newValue })));
  await expect(dialog).not.toBeVisible();
  const exported = await exportFiles(page, testInfo, `${transfer.action}.zip`);
  expectFiles(exported, fixture.expected[transfer.action]!);
  await history(page, "Undo");
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeDisabled();
  expectFiles(await exportFiles(page, testInfo, `undo-${transfer.action}.zip`), fixture.files);
  await history(page, "Redo");
  expectFiles(await exportFiles(page, testInfo, `redo-${transfer.action}.zip`), exported);
  await history(page, "Undo");
  const empty = await requestPlan(page, document, `transfer/${transfer.action}`, []);
  expect(empty.response.status()).toBe(200);
  expect(empty.preview.plan.changes).toEqual([]);
  const noOp = await page.request.post(`${document}/actions/apply`, { data: { planId: empty.preview.planId, revision: empty.preview.revision } });
  expect(noOp.status()).toBe(200);
  expect(await noOp.json()).toMatchObject({ ok: true, changes: [], state: { canUndo: false } });
  const rejected = await requestPlan(page, document, `transfer/${transfer.action}`, [transfer.source, transfer.source]);
  expect(rejected.response.status()).toBe(422);
  expect(rejected.preview.plan.changes).toEqual([]);
  expect(rejected.preview.plan.errors).not.toEqual([]);
  expectFiles(await exportFiles(page, testInfo, `rejected-${transfer.action}.zip`), fixture.files);

  await table.getByRole("checkbox").nth(1).uncheck();
}

export async function productionGearboxTransfers(page: Page, testInfo: TestInfo, fixture: GearboxFixture, action: "deposit" | "withdraw" | "claim") {
  await openGearbox(page, fixture.files);
  await page.getByRole("button", { name: "Choose related files", exact: true }).click();
  const opening = page.waitForResponse(response => response.url().endsWith("/document-groups"));
  await page.getByRole("button", { name: "Open group", exact: true }).click();
  const response = await opening;
  expect(response.ok()).toBe(true);
  const group = await response.json() as SaveDocument;
  expect(group.groupMembers?.map(member => member.role).sort()).toEqual(["character", "profile"]);
  for (const member of group.groupMembers!) {
    expect(member.relativePath).toBe(Object.keys(fixture.files).find(path => path.endsWith(member.role === "profile" ? "/profile" : "/1")));
  }
  const document = new URL(response.url()).pathname.replace(/\/document-groups$/, `/documents/${group.id}`);
  await page.getByRole("tab", { name: "Inventory", exact: true }).first().click();
  await exerciseTransfer(page, testInfo, fixture, document, transfers.find(transfer => transfer.action === action)!);
  await openGearbox(page, fixture.expected[action]!);
  await page.getByRole("tab", { name: "Inventory", exact: true }).click();
  await expect(page.getByRole("table", { name: "Backpack", exact: true }).getByRole("row")).toHaveCount(action === "deposit" ? 2 : 4);
  expectFiles(await exportFiles(page, testInfo, "reopened-transfer.zip"), fixture.expected[action]!);
}
