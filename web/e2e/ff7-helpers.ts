import { expect, type APIRequestContext, type Page, type TestInfo } from "@playwright/test";
import { execFile } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdir, readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { promisify } from "node:util";
import { unzipSync } from "fflate";
import type { DocumentState, SaveDocument, ToolkitPreset } from "../src/api/protocol";
import { archive, importSave, type SaveFiles } from "./helpers";

export const actions = {
  restock: "inventory/restock-existing", ap: "materia/set-ap-within-tier", stars: "materia/set-stars",
  recover: "character/recover-current-vitals", level: "character/set-level",
} as const;
export interface Ff7Fixture {
  titleId: string; gameplayName: string; commonName: string; gameplayPath: string; commonPath: string;
  itemType: number; materiaType: number; secondTierAp: number; fieldOffsets: Record<string, number>;
  files: SaveFiles;
}
export interface SemanticPreview {
  preset: ToolkitPreset;
  preview: { planId?: string; revision: number; stale: boolean;
    resolution: { instances: { requested: { stableId: string }; status: string;
      candidates: { stableId: string; path?: string }[] }[] };
    plan: { changes: { valueId: string; oldValue: string; newValue: string }[]; errors: unknown[]; skippedRows: unknown[] } };
}

export async function generateFf7Fixtures(testInfo: TestInfo): Promise<string> {
  const directory = testInfo.outputPath("authored-ff7");
  await mkdir(directory, { recursive: true });
  const { stdout, stderr } = await promisify(execFile)("dotnet", ["run", "--project",
    "tests/fixtures/SaveToolkit.Ff7BrowserFixture", "--configuration", "Release", "--", directory], {
    cwd: fileURLToPath(new URL("../..", import.meta.url)), timeout: 60_000, maxBuffer: 2 * 1024 * 1024,
  });
  await testInfo.attach("ff7-generator.log", { body: stdout + stderr, contentType: "text/plain" });
  return directory;
}

export async function fixture(directory: string, name: string): Promise<Ff7Fixture> {
  const metadata = JSON.parse(await readFile(path.join(directory, `${name}.json`), "utf8")) as Omit<Ff7Fixture, "files">;
  const files = unzipSync(await readFile(path.join(directory, `${name}.zip`)));
  return { ...metadata, files };
}

export async function openFf7(page: Page, binary: Ff7Fixture, file = binary.gameplayName): Promise<string> {
  await importSave(page, binary.files);
  const opening = page.waitForResponse(response => response.url().endsWith("/open") && response.request().method() === "POST");
  await page.getByRole("navigation", { name: "Save directories" }).getByRole("button")
    .filter({ has: page.getByText(file, { exact: true }) }).click();
  const response = await opening;
  expect(response.ok()).toBe(true);
  const document = await response.json() as SaveDocument;
  expect(document.plugin.id).toBe("ff7r");
  expect(document.titleId).toBe(binary.titleId);
  expect(document.presetCapability?.actions ?? []).toHaveLength(file === binary.commonName ? 0 : 5);
  if (file === binary.commonName) expect(document.canEdit).toBe(false);
  await expect(page.getByRole("heading", { name: document.title, exact: true })).toBeVisible();
  await page.locator(".presets-panel > summary").click();
  return new URL(response.url()).pathname.replace(/\/files\/[^/]+\/open$/, `/documents/${document.id}`);
}

export async function capture(page: Page, name: string): Promise<ToolkitPreset> {
  const panel = page.locator(".presets-panel");
  await panel.getByLabel("Name", { exact: true }).fill(name);
  const busy = await panel.locator(".section__body").evaluateHandle(element => {
    let observed = element.getAttribute("aria-busy") === "true";
    const observer = new MutationObserver(records => {
      observed ||= element.getAttribute("aria-busy") === "true" || records.some(record => record.oldValue === "true");
    });
    observer.observe(element, { attributes: true, attributeFilter: ["aria-busy"], attributeOldValue: true });
    return { stop: () => { observer.disconnect(); return observed; } };
  });
  const saving = page.waitForResponse(response => response.url().endsWith("/presets/capture"));
  await panel.getByRole("button", { name: "Save preset", exact: true }).click();
  const response = await saving;
  expect(response.status()).toBe(200);
  await expect(panel.getByRole("status").filter({ hasText: `Saved “${name}”.` })).toHaveCount(1);
  expect(await busy.evaluate(observation => observation.stop())).toBe(true);
  await busy.dispose();
  return await response.json() as ToolkitPreset;
}

export function saved(page: Page, name: string) {
  return page.getByRole("list", { name: "Saved toolkit presets" }).getByRole("listitem").filter({ has: page.getByText(name, { exact: true }) });
}

export async function preview(page: Page, name: string): Promise<SemanticPreview> {
  const loading = page.waitForResponse(response => /\/presets\/[^/]+\/preview$/.test(response.url()));
  await saved(page, name).getByRole("button", { name: "Preview", exact: true }).click();
  return await (await loading).json() as SemanticPreview;
}

export async function snapshot(request: APIRequestContext, documentPath: string) {
  const state = (await (await request.get(documentPath)).json() as SaveDocument).state;
  const library = await (await request.get("/api/presets")).body();
  const response = await request.post(documentPath.replace(/\/documents\/[^/]+$/, "/export"), { data: { scope: "Everything", mirrorBackups: false } });
  expect(response.ok()).toBe(true);
  return { state, library: library.toString(), files: unzipSync(await response.body()) };
}

export async function unchanged(request: APIRequestContext, documentPath: string, before: Awaited<ReturnType<typeof snapshot>>) {
  const after = await snapshot(request, documentPath);
  expect(after.state).toEqual(before.state);
  expect(after.library).toEqual(before.library);
  expectFiles(after.files, before.files);
}

/** Native Buffer comparison checks every member byte without constructing millions of matcher nodes. */
export function expectFiles(actual: SaveFiles, expected: SaveFiles) {
  expect(Object.keys(actual).sort()).toEqual(Object.keys(expected).sort());
  for (const [name, bytes] of Object.entries(expected))
    expect(Buffer.from(actual[name]!).equals(Buffer.from(bytes)), `Exact complete member bytes: ${name}`).toBe(true);
}

export async function state(request: APIRequestContext, documentPath: string): Promise<DocumentState> {
  return (await (await request.get(documentPath)).json() as SaveDocument).state;
}

export function patch(binary: Ff7Fixture, fields: Record<string, number>): SaveFiles {
  const bytes = Buffer.from(binary.files[binary.gameplayPath]!);
  for (const [id, value] of Object.entries(fields)) {
    const offset = binary.fieldOffsets[id];
    expect(offset, id).toBeDefined();
    if (id.endsWith("/level") || id.endsWith("/owner")) bytes.writeUInt8(value, offset!);
    else bytes.writeUInt32LE(value, offset!);
  }
  return { ...binary.files, [binary.gameplayPath]: bytes };
}

export async function manifest(testInfo: TestInfo, name: string, files: SaveFiles) {
  const members = Object.entries(files).map(([member, bytes]) => ({ member, length: bytes.length,
    sha256: createHash("sha256").update(bytes).digest("hex") }));
  await testInfo.attach(`${name}.json`, { body: JSON.stringify(members, null, 2), contentType: "application/json" });
}

/** Retain actual response bytes unchanged; never intercept or fabricate successful protocol data. */
export function recordApi(page: Page, testInfo: TestInfo) {
  const pending: Promise<void>[] = [];
  let sequence = 0;
  page.on("response", response => {
    if (!response.url().includes("/api/") || !response.headers()["content-type"]?.includes("json")) return;
    const id = ++sequence;
    pending.push((async () => {
      await testInfo.attach(`http-${id}-response.json`, { body: await response.body(), contentType: "application/json" });
      const request = response.request();
      await testInfo.attach(`http-${id}-metadata.txt`, { body: `${request.method()} ${request.url()}\n${response.status()}\n${request.postData() ?? ""}`, contentType: "text/plain" });
    })());
  });
  return async () => { await Promise.all(pending); };
}

export { archive };
