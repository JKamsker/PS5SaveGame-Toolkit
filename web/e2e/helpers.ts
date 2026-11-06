import { expect, type Download, type Page, type TestInfo } from "@playwright/test";
import { readFileSync } from "node:fs";
import { readFile } from "node:fs/promises";
import { zipSync, unzipSync } from "fflate";
import type { SaveDocument, Workspace } from "../src/api/protocol";
import { installApiHooks, waitForApiResponse } from "./apiRoutes";
import { apiRequest } from "./apiRequest";

export type SaveFiles = Record<string, Uint8Array>;
export const savePath = "PPSA00000/sdimg_E2E/save.json";
export const profilePath = "PPSA00000/sdimg_E2E/profile.json";
export const originalCoins = "12345";
export const editedCoins = "54321";
export const originalSave = fixture("save.json");
export const originalFiles: SaveFiles = {
  [savePath]: originalSave,
  "PPSA00000/sdimg_E2E/nested/companion.txt": fixture("companion.txt"),
  "PPSA00000/sdimg_E2E/empty.dat": new Uint8Array(),
};

export function fixture(name: string) {
  return readFileSync(new URL(`./fixtures/${name}`, import.meta.url));
}

export function archive(files: SaveFiles = originalFiles) {
  return Buffer.from(zipSync(files, { mtime: new Date("2020-01-01T00:00:00Z") }));
}

export async function home(page: Page) {
  // The /api runs in the page's .NET worker; observe it before the app starts.
  await installApiHooks(page);
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "Open a PS5 save dump" })).toBeVisible();
  await runtimeReady(page);
}

/** Waits until the page's runtime has downloaded and started, so later expectations time real work only. */
export async function runtimeReady(page: Page) {
  await expect(page.getByRole("progressbar", { name: "Editor runtime download" })).toHaveCount(0, { timeout: 30_000 });
}

/**
 * Opens an app deep link inside the running page (client-side, like following an in-app link). A full
 * page.goto would start a new runtime: workspaces only live in the current page's in-browser runtime.
 */
export async function followLink(page: Page, url: string) {
  await page.evaluate(target => {
    window.history.pushState(null, "", target);
    window.dispatchEvent(new PopStateEvent("popstate", { state: null }));
  }, url);
}

/**
 * Imports a dump and opens one of its files through the page's runtime, without the UI, and returns the
 * document's deep link (follow it with followLink: a full page load would start a new, empty runtime).
 */
export async function createDocumentLink(page: Page, buffer = archive(), name = "save.json", pluginId = "format.json") {
  const api = apiRequest(page);
  const imported = await api.post("/api/workspaces", { multipart: { files: { name: "synthetic-save.zip", mimeType: "application/zip", buffer } } });
  expect(imported.ok()).toBe(true);
  const workspace = await imported.json() as Workspace;
  const files = workspace.titles.flatMap(title => title.directories.flatMap(directory => directory.files)).filter(file => file.path === name);
  expect(files).toHaveLength(1);
  const opened = await api.post(`/api/workspaces/${workspace.id}/files/${files[0]!.id}/open`, { data: { pluginId } });
  expect(opened.ok()).toBe(true);
  return `/w/${workspace.id}?doc=${(await opened.json() as SaveDocument).id}`;
}

export async function upload(page: Page, buffer = archive(), name = "synthetic-save.zip") {
  const importing = waitForApiResponse(page, response => response.url().endsWith("/api/workspaces") && response.request().method() === "POST");
  const choosing = page.waitForEvent("filechooser");
  await page.getByRole("button", { name: "Open zip", exact: true }).click();
  await (await choosing).setFiles({ name, mimeType: "application/zip", buffer });
  await importing;
}

export async function importSave(page: Page, files: SaveFiles = originalFiles) {
  await home(page);
  await upload(page, archive(files));
  await expect(page.getByRole("heading", { name: "Pick a save file" })).toBeVisible();
  await page.getByRole("navigation", { name: "Save directories" }).getByRole("button", { name: / E2E / }).click();
}

export async function openJson(page: Page, name = "save.json") {
  const directories = page.getByRole("navigation", { name: "Save directories" });
  await directories.getByRole("button").filter({ has: page.getByText(name, { exact: true }) }).click();
  await expect(page.getByRole("heading", { name, exact: true })).toBeVisible();
  await page.getByRole("combobox", { name: "Open with" }).selectOption({ label: "Open with Generic JSON" });
  await expect(page.getByRole("tab", { name: "JSON", exact: true })).toBeVisible();
}

export async function loadJson(page: Page) {
  await page.getByRole("tab", { name: "JSON", exact: true }).click();
  await page.getByRole("button", { name: "Load Entries", exact: true }).click();
  await expect(coinsField(page)).toBeVisible();
}

export function coinsField(page: Page) {
  return page.getByRole("textbox", { name: /^coins\s*meaning unknown/ });
}

export async function editCoins(page: Page, value = editedCoins) {
  await coinsField(page).fill(value);
  await coinsField(page).press("Enter");
  // Wait for the server mutation, rather than just the local input draft.
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeEnabled();
  await expect(coinsField(page)).toHaveAccessibleName(/modified/);
  await expect(coinsField(page)).toHaveValue(value);
}

export async function downloadedFiles(download: Download, testInfo: TestInfo, name = "export.zip") {
  expect(download.suggestedFilename()).toMatch(/\.zip$/i);
  const destination = testInfo.outputPath(name);
  await download.saveAs(destination);
  expect(await download.failure()).toBeNull();
  return unzipSync(await readFile(destination));
}

export async function exportFiles(page: Page, testInfo: TestInfo, name = "export.zip") {
  await page.getByRole("button", { name: /^Export(?:$| \d)/ }).click();
  const dialog = page.getByRole("dialog", { name: "Export edited saves" });
  await dialog.getByRole("radio", { name: /Whole dump/ }).check();
  await expect(dialog.getByRole("button", { name: "Download zip", exact: true })).toBeEnabled();
  const downloading = page.waitForEvent("download");
  await dialog.getByRole("button", { name: "Download zip", exact: true }).click();
  const files = await downloadedFiles(await downloading, testInfo, name);
  await expect(dialog).not.toBeVisible();
  return files;
}

export function withCoins(original: Uint8Array, before: string, after: string) {
  return Buffer.from(Buffer.from(original).toString("utf8")
    .replace(new RegExp(`("coins"\\s*:\\s*)${before}`), (_match, prefix: string) => prefix + after));
}

export function expectFiles(actual: SaveFiles, expected: SaveFiles) {
  // Timestamps/compression can change; compare every uncompressed payload byte and path.
  expect(Object.keys(actual).sort()).toEqual(Object.keys(expected).sort());
  for (const [name, bytes] of Object.entries(expected)) {
    expect(Buffer.from(actual[name]!), name).toEqual(Buffer.from(bytes));
  }
}

export function expectArchiveBytes(files: SaveFiles, expectedSave: Uint8Array) {
  expectFiles(files, { ...originalFiles, [savePath]: expectedSave });
}
