import { expect, type Page, type TestInfo } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { importSave, openJson, loadJson, originalSave } from "./helpers";
import { apiRequest } from "./apiRequest";
import { presetLibraryPath, runtimeFiles } from "./runtimeFiles";

export const transferPath = "/api/presets/transfer";
const stamp = "2026-10-29T00:00:00Z";

export function record(index: number, flag = false) {
  const prefix = `${index.toString().padStart(3, "0")}"\\🧰é`;
  return {
    schemaVersion: "1", id: prefix + "界".repeat(128 - prefix.length), name: `Preset ${index.toString().padStart(3, "0")}`,
    createdAt: stamp, updatedAt: stamp,
    compatibility: { pluginId: "synthetic-later", pluginVersion: "1", titleIds: ["OTHER"], gameBuild: null,
      formatVariant: null, referencePackIdentity: null, requiresExplicitInstanceResolution: true,
      presetKind: "toolkit.action-parameters", schemaFingerprint: null, fileRole: null, capabilitySchemaVersion: null },
    actions: [{ actionId: "flag", parameters: { flag }, instances: [], scopeKind: "document" }],
  };
}

export type RecordFixture = ReturnType<typeof record>;
export function file(records: RecordFixture[]) {
  // These fixtures contain only strings/booleans and small count/index values.
  return { name: "synthetic-presets.json", mimeType: "application/json", buffer: Buffer.from(JSON.stringify({
    schemaVersion: "1", provenance: { producer: "Synthetic browser fixture", exportedAt: stamp }, presets: records,
  })) };
}

/** The durable library file plays the old server's presets.json: tests replace it to model an external change. */
export async function libraryFile(page: Page) {
  const files = await runtimeFiles(page);
  return { read: () => files.read(presetLibraryPath), write: (text: string) => files.change({ kind: "write", path: presetLibraryPath, text }) };
}

/** Empties the booted page's library through its file and a bounded reload instead of many unrelated CRUD requests. */
export async function clear(page: Page) {
  const request = apiRequest(page);
  expect((await request.get("/api/presets")).ok()).toBe(true); // Initialize the lazily resolved store.
  await (await libraryFile(page)).write("[]");
  expect((await request.post("/api/presets/reload")).ok()).toBe(true);
  expect(await request.get("/api/presets").then(response => response.json()) as unknown[]).toHaveLength(0);
}

export async function seed(page: Page, records: RecordFixture[]) {
  const request = apiRequest(page);
  const response = await request.post(`${transferPath}/preview`, { data: file(records).buffer, headers: { "content-type": "application/json" } });
  expect(response.ok()).toBe(true);
  const preview = await response.json() as { token: string };
  const reviewed = await request.post(`${transferPath}/resolve`, { data: { token: preview.token,
    choices: records.map((_, index) => ({ index, decision: "keepBoth" })) } });
  expect(reviewed.ok()).toBe(true);
  const ready = await reviewed.json() as { token: string };
  expect((await request.post(`${transferPath}/commit`, { data: { token: ready.token } })).ok()).toBe(true);
  expect((await request.delete(`${transferPath}/previews/${preview.token}`)).status()).toBe(204);
  // The runtime persists each mutation before it answers the next request, so the library survives navigation.
  expect(await request.get("/api/presets").then(response => response.json()) as unknown[]).toHaveLength(records.length);
}

export async function open(page: Page) {
  await importSave(page); await openJson(page); await loadJson(page);
  await page.getByText("Toolkit presets", { exact: true }).click();
  const trigger = page.getByRole("button", { name: "Import presets", exact: true });
  return { trigger, dialog: page.getByRole("dialog", { name: "Import toolkit presets" }) };
}

export async function unchangedSave(page: Page, info: TestInfo) {
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeDisabled();
  const downloading = page.waitForEvent("download");
  await page.getByRole("button", { name: /Download file/ }).click();
  const download = await downloading; const destination = info.outputPath("unchanged-save.json");
  await download.saveAs(destination); expect(await readFile(destination)).toEqual(originalSave);
}
