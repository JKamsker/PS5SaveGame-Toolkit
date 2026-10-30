import { expect, type APIRequestContext, type Page, type TestInfo } from "@playwright/test";
import { readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { importSave, openJson, loadJson, originalSave } from "./helpers";

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

export function libraryPath() {
  const scratch = path.resolve(process.env.SAVETOOLKIT_E2E_SCRATCH!);
  const durablePath = path.resolve(process.env.SAVETOOLKIT_E2E_RUN_DIRECTORY!, "presets", "presets.json");
  const relative = path.relative(scratch, durablePath);
  expect(relative !== "" && !path.isAbsolute(relative) && !relative.startsWith(`..${path.sep}`)).toBe(true);
  return durablePath;
}

export async function clear(request: APIRequestContext) {
  // Fixture cleanup uses this run's synthetic host file and bounded reload instead of 256 unrelated CRUD requests.
  expect((await request.get("/api/presets")).ok()).toBe(true); // Initialize the lazily resolved host store on a fresh server.
  await writeFile(libraryPath(), "[]");
  expect((await request.post("/api/presets/reload")).ok()).toBe(true);
  expect(await request.get("/api/presets").then(response => response.json()) as unknown[]).toHaveLength(0);
}

export async function seed(request: APIRequestContext, records: RecordFixture[]) {
  const response = await request.post(`${transferPath}/preview`, { data: file(records).buffer, headers: { "content-type": "application/json" } });
  expect(response.ok()).toBe(true);
  const preview = await response.json() as { token: string };
  const reviewed = await request.post(`${transferPath}/resolve`, { data: { token: preview.token,
    choices: records.map((_, index) => ({ index, decision: "keepBoth" })) } });
  expect(reviewed.ok()).toBe(true);
  const ready = await reviewed.json() as { token: string };
  expect((await request.post(`${transferPath}/commit`, { data: { token: ready.token } })).ok()).toBe(true);
  expect((await request.delete(`${transferPath}/previews/${preview.token}`)).status()).toBe(204);
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
  await page.getByRole("link", { name: /Download file/ }).click();
  const download = await downloading; const destination = info.outputPath("unchanged-save.json");
  await download.saveAs(destination); expect(await readFile(destination)).toEqual(originalSave);
}
