import { expect, type Locator, type Page, type TestInfo } from "@playwright/test";
import { execFile } from "node:child_process";
import { mkdir, readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { promisify } from "node:util";
import type { FieldNode, QueryablePage, SaveDocument } from "../src/api/protocol";
import { importSave, type SaveFiles } from "./helpers";

const repositoryRoot = fileURLToPath(new URL("../..", import.meta.url));
export const jediPath = "PPSA02199/sdimg_E2E/settings.sav";
export const brightnessName = /^HDRBrightness · FloatProperty/;
export const consoleWarning = "Not validated on console. The game may reject or overwrite the edited save. Serialized property name and type are verified. Game constraints and effects are unverified; keep a backup.";
export const wideTail = "unique-synthetic-tail";
export const wideSummary = "雪".repeat(192) + "…";

export async function generateUnrealFixtures(testInfo: TestInfo): Promise<string> {
  const directory = testInfo.outputPath("synthetic-gvas");
  await mkdir(directory, { recursive: true });
  const { stdout, stderr } = await promisify(execFile)("dotnet", ["run", "--project",
    "tests/fixtures/SaveToolkit.UnrealBrowserFixture", "--configuration", "Release", "--", directory],
  { cwd: repositoryRoot, timeout: 60_000, maxBuffer: 2 * 1024 * 1024 });
  await testInfo.attach("fixture-generator.log", { body: stdout + stderr, contentType: "text/plain" });
  return directory;
}

export async function unrealBytes(directory: string, name: string): Promise<Buffer> {
  return readFile(path.join(directory, name));
}

export async function openUnreal(page: Page, files: SaveFiles, name: string): Promise<string> {
  await importSave(page, files);
  const opening = page.waitForResponse(response => response.url().endsWith("/open") && response.request().method() === "POST");
  await page.getByRole("navigation", { name: "Save directories" }).getByRole("button")
    .filter({ has: page.getByText(name, { exact: true }) }).click();
  const response = await opening;
  expect(response.ok()).toBe(true);
  const document = await response.json() as SaveDocument;
  expect(document.plugin.id).toBe("engine.unreal-gvas");
  await expect(page.getByRole("heading", { name: document.title, exact: true })).toBeVisible();
  await expect(page.getByRole("combobox", { name: "Open with" })).toHaveValue("engine.unreal-gvas");
  return new URL(response.url()).pathname.replace(/\/files\/[^/]+\/open$/, `/documents/${document.id}`);
}

export async function collection(page: Page, documentPath: string, id: string, q?: string): Promise<QueryablePage> {
  const response = await page.request.get(`${documentPath}/collections`, { params: { collectionId: id, limit: 200, ...(q ? { q } : {}) } });
  expect(response.ok()).toBe(true);
  expect((await response.body()).length).toBeLessThan(2 * 1024 * 1024);
  return await response.json() as QueryablePage;
}

export function scalar(page: QueryablePage, valueId: string): FieldNode {
  const cell = page.rows.find(row => row.key === valueId)?.cells.value;
  expect(cell?.type).toBe("field");
  if (cell?.type !== "field") throw new Error(`Expected normalized canonical field ${valueId}`);
  return cell;
}

export async function showSearch(page: Page): Promise<void> {
  await page.getByRole("tab", { name: "Search", exact: true }).click();
  const load = page.getByRole("button", { name: "Load Top-level primitive properties", exact: true });
  if (await load.isVisible()) await load.click();
}

export async function tabTo(page: Page, target: Locator): Promise<void> {
  await expect(target).toBeVisible();
  for (let stop = 0; stop < 100; stop++) {
    if (await target.evaluate(element => element === document.activeElement)) return;
    await page.keyboard.press("Tab");
  }
  await expect(target, "Control must be reachable by Tab").toBeFocused();
}

export async function screenshot(page: Page, testInfo: TestInfo, name: string): Promise<void> {
  await page.mouse.move(0, 0);
  const output = testInfo.outputPath(`${name}.png`);
  await page.screenshot({ path: output, fullPage: true });
  await testInfo.attach(name, { path: output, contentType: "image/png" });
}

export async function contained(page: Page, target: Locator, width: number): Promise<void> {
  const bounds = await target.boundingBox();
  expect(bounds).not.toBeNull();
  expect(bounds!.x).toBeGreaterThanOrEqual(0);
  expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
}
