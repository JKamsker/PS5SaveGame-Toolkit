import { expect, type Page, type TestInfo } from "@playwright/test";
import { execFile } from "node:child_process";
import { mkdir, readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { promisify } from "node:util";
import type { SaveDocument } from "../src/api/protocol";
import { archive, downloadedFiles, home, type SaveFiles, upload } from "./helpers";
import { unzipSync } from "fflate";

const repositoryRoot = fileURLToPath(new URL("../..", import.meta.url));
export const captionPrimaryPath = "PPSA02225/sdimg_CAPTION/dis.dat";
export const captionSfoPath = "PPSA02225/sdimg_CAPTION/sce_sys/param.sfo";
export const captionCompanionPath = "PPSA02225/sdimg_CAPTION/other.bin";
export const captionValueId = "metadata/sce_sys/param.sfo/SUBTITLE";
export const captionLabel = "Save caption · sce_sys/param.sfo SUBTITLE";
export const captionWarning = "Not validated on console. The game may reject or overwrite the edited save.";
export const editedCaption = "🌙";

export interface SuckerPunchFixture {
  readonly titleId: string;
  readonly primaryRelativePath: string;
  readonly sfoRelativePath: string;
  readonly companionRelativePath: string;
  readonly caption: string;
  readonly maxUtf8Bytes: number;
  readonly files: SaveFiles;
}

export async function generateSuckerPunchFixtures(testInfo: TestInfo): Promise<SuckerPunchFixture> {
  const directory = testInfo.outputPath("synthetic-suckerpunch");
  await mkdir(directory, { recursive: true });
  const { stdout, stderr } = await promisify(execFile)("dotnet", ["run", "--project",
    "tests/fixtures/SaveToolkit.SuckerPunchBrowserFixture", "--configuration", "Release", "--", directory],
  { cwd: repositoryRoot, timeout: 60_000, maxBuffer: 2 * 1024 * 1024 });
  await testInfo.attach("suckerpunch-fixture-generator.log", { body: stdout + stderr, contentType: "text/plain" });
  const manifest = JSON.parse(await readFile(path.join(directory, "manifest.json"), "utf8")) as Omit<SuckerPunchFixture, "files">;
  const files: SaveFiles = {
    [manifest.primaryRelativePath]: await readFile(path.join(directory, "gameplay.bin")),
    [manifest.sfoRelativePath]: await readFile(path.join(directory, "param.sfo")),
    [manifest.companionRelativePath]: await readFile(path.join(directory, "companion.bin")),
  };
  return { ...manifest, files };
}

/** Imports through the real multipart endpoint and opens the production SuckerPunch plugin. */
export async function openSuckerPunch(page: Page, files: SaveFiles, fileName = "dis.dat",
  expectedHeading: RegExp | null = /PPSA02225 · Moon/): Promise<string> {
  await home(page);
  await upload(page, archive(files), "suckerpunch-caption.zip");
  await expect(page.getByRole("heading", { name: "Pick a save file" })).toBeVisible();
  const rail = page.getByRole("navigation", { name: "Save directories" });
  const slot = rail.getByRole("button").filter({ hasText: "CAPTION" }).first();
  await expect(slot).toBeVisible();
  await slot.click();
  const opening = page.waitForResponse(response => response.url().endsWith("/open") && response.request().method() === "POST");
  await rail.locator("button.file").filter({ hasText: fileName }).click();
  const response = await opening;
  expect(response.ok()).toBe(true);
  const document = await response.json() as SaveDocument;
  expect(document.plugin.id).toBe("suckerpunch");
  if (expectedHeading !== null) await expect(page.getByRole("heading", { name: expectedHeading })).toBeVisible();
  return new URL(response.url()).pathname.replace(/\/files\/[^/]+\/open$/, `/documents/${document.id}`);
}

export function expectedGlyphSfo(source: Uint8Array): Buffer {
  return expectedCaptionSfo(source, editedCaption);
}

export function expectedCaptionSfo(source: Uint8Array, value: string): Buffer {
  const original = Buffer.from("Moon\0", "utf8");
  const replacement = Buffer.from(`${value}\0`, "utf8");
  expect(replacement.length).toBe(original.length);
  const offset = Buffer.from(source).indexOf(original);
  expect(offset).toBeGreaterThanOrEqual(0);
  expect(Buffer.from(source).indexOf(original, offset + 1)).toBe(-1);
  const expected = Buffer.from(source);
  replacement.copy(expected, offset);
  return expected;
}

export function isCaptionDocumentRequest(url: string, documentPath: string, suffix: string): boolean {
  return new URL(url).pathname.endsWith(`${documentPath}${suffix}`);
}

/** Downloads a whole-dump export through the real workspace endpoint without opening a second modal. */
export async function exportCaptionApi(page: Page, documentPath: string): Promise<SaveFiles> {
  const workspacePath = documentPath.slice(0, documentPath.indexOf("/documents/"));
  const response = await page.request.post(`${workspacePath}/export`, {
    data: { scope: "Everything", mirrorBackups: false },
  });
  expect(response.ok()).toBe(true);
  return unzipSync(await response.body());
}

export async function exportCaptionScope(page: Page, testInfo: TestInfo, label: RegExp, name: string): Promise<SaveFiles> {
  await page.getByRole("button", { name: /^Export/ }).click();
  const dialog = page.getByRole("dialog", { name: "Export edited saves" });
  await dialog.getByRole("radio", { name: label }).check();
  await expect(dialog.getByRole("button", { name: "Download zip", exact: true })).toBeEnabled();
  const downloading = page.waitForEvent("download");
  await dialog.getByRole("button", { name: "Download zip", exact: true }).click();
  const files = await downloadedFiles(await downloading, testInfo, name);
  await expect(dialog).not.toBeVisible();
  return files;
}

export async function documentSnapshot(page: Page, documentPath: string): Promise<SaveDocument> {
  const response = await page.request.get(documentPath);
  expect(response.ok()).toBe(true);
  return await response.json() as SaveDocument;
}
