import { test, expect, type APIRequestContext, type Page, type TestInfo } from "@playwright/test";
import { readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { importSave, openJson, loadJson, originalSave } from "./helpers";

const transferPath = "/api/presets/transfer";
const stamp = "2026-10-29T00:00:00Z";

function record(index: number, flag = false) {
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

type RecordFixture = ReturnType<typeof record>;
function file(records: RecordFixture[]) {
  // These fixtures contain only strings/booleans and small count/index values.
  return { name: "synthetic-presets.json", mimeType: "application/json", buffer: Buffer.from(JSON.stringify({
    schemaVersion: "1", provenance: { producer: "Synthetic browser fixture", exportedAt: stamp }, presets: records,
  })) };
}

async function clear(request: APIRequestContext) {
  const records = await request.get("/api/presets").then(response => response.json()) as { id: string }[];
  for (const preset of records) expect((await request.delete(`/api/presets/${encodeURIComponent(preset.id)}`)).ok()).toBe(true);
}

async function seed(request: APIRequestContext, records: RecordFixture[]) {
  const response = await request.post(`${transferPath}/preview`, { data: file(records).buffer, headers: { "content-type": "application/json" } });
  expect(response.ok()).toBe(true);
  const preview = await response.json() as { token: string };
  const reviewed = await request.post(`${transferPath}/resolve`, { data: { token: preview.token,
    choices: records.map((_, index) => ({ index, decision: "keepBoth" })) } });
  expect(reviewed.ok()).toBe(true);
  const ready = await reviewed.json() as { token: string };
  expect((await request.post(`${transferPath}/commit`, { data: { token: ready.token } })).ok()).toBe(true);
}

async function open(page: Page) {
  await importSave(page); await openJson(page); await loadJson(page);
  await page.getByText("Toolkit presets", { exact: true }).click();
  const trigger = page.getByRole("button", { name: "Import presets", exact: true });
  return { trigger, dialog: page.getByRole("dialog", { name: "Import toolkit presets" }) };
}

async function unchangedSave(page: Page, info: TestInfo) {
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeDisabled();
  const downloading = page.waitForEvent("download");
  await page.getByRole("link", { name: /Download file/ }).click();
  const download = await downloading; const destination = info.outputPath("unchanged-save.json");
  await download.saveAs(destination); expect(await readFile(destination)).toEqual(originalSave);
}

test("full 256-record library exports and explicitly replaces maximum Unicode IDs through the browser", async ({ page, request }, info) => {
  test.setTimeout(60_000);
  await clear(request);
  try {
    const records = Array.from({ length: 256 }, (_, index) => record(index));
    expect(records.every(item => item.id.length === 128)).toBe(true);
    await seed(request, records);
    const { trigger, dialog } = await open(page);
    await page.getByRole("button", { name: "Export presets", exact: true }).click();
    const exporting = page.getByRole("dialog", { name: "Export toolkit presets" });
    await exporting.getByRole("checkbox", { name: /Select all/ }).check();
    const downloading = page.waitForEvent("download");
    await exporting.getByRole("button", { name: "Download 256 presets" }).click();
    const download = await downloading; const destination = info.outputPath("full-library.json"); await download.saveAs(destination);
    expect(download.suggestedFilename()).toBe("Toolkit presets.toolkit-presets.json");
    const bytes = await readFile(destination); expect(bytes.length).toBeLessThanOrEqual(512 * 1024);
    expect((JSON.parse(bytes.toString()) as { presets: RecordFixture[] }).presets.map(item => item.id).sort())
      .toEqual(records.map(item => item.id).sort());
    await trigger.click(); await dialog.getByLabel("Preset package").setInputFiles(file(records.map((_, index) => record(index, true))));
    const replacements = dialog.getByRole("radio", { name: /^Replace existing/ }); await expect(replacements).toHaveCount(256);
    await expect(dialog.getByRole("button", { name: "Review resulting names" })).toBeDisabled();
    // Exercise every explicit choice with native keyboard activation, including rows outside the viewport.
    for (const replacement of await replacements.all()) { await replacement.focus(); await page.keyboard.press("Space"); }
    await expect(dialog.getByRole("radio", { checked: true })).toHaveCount(256);
    await dialog.getByRole("button", { name: "Review resulting names" }).click();
    await expect(dialog.getByText(/^Replaces “Preset/)).toHaveCount(256);
    await dialog.getByRole("button", { name: "Confirm import", exact: true }).click(); await expect(trigger).toBeFocused();
    const stored = await request.get("/api/presets").then(response => response.json()) as RecordFixture[];
    expect(stored).toHaveLength(256); expect(stored.every(item => item.actions[0]!.parameters.flag)).toBe(true);
    await unchangedSave(page, info);
  } finally { await clear(request); }
});

test("external addition requires explicit reload before fresh preview and survives the unrelated import", async ({ page, request }, info) => {
  await clear(request);
  try {
    await seed(request, [record(0)]);
    const { trigger, dialog } = await open(page); await trigger.click();
    const incoming = file([record(2)]);
    await dialog.getByLabel("Preset package").setInputFiles(incoming);
    await dialog.getByRole("button", { name: "Review resulting names" }).click();
    const scratch = path.resolve(process.env.SAVETOOLKIT_E2E_SCRATCH!);
    const durablePath = path.resolve(process.env.SAVETOOLKIT_E2E_RUN_DIRECTORY!, "presets", "presets.json");
    const relative = path.relative(scratch, durablePath);
    expect(relative !== "" && !path.isAbsolute(relative) && !relative.startsWith(`..${path.sep}`)).toBe(true);
    const records = JSON.parse(await readFile(durablePath, "utf8")) as RecordFixture[];
    records.push(record(1)); await writeFile(durablePath, JSON.stringify(records));
    await dialog.getByRole("button", { name: "Confirm import", exact: true }).click();
    await expect(dialog.getByRole("alert")).toContainText("changed on disk");
    const rejected = await request.post(`${transferPath}/preview`, { data: incoming.buffer, headers: { "content-type": "application/json" } });
    expect(rejected.status()).toBe(409);
    expect((await rejected.json() as { code: string }).code).toBe("presetLibraryReloadRequired");
    await dialog.getByRole("button", { name: "Cancel", exact: true }).click(); await expect(trigger).toBeFocused();
    await trigger.click(); await dialog.getByLabel("Preset package").setInputFiles(incoming);
    await expect(dialog.getByRole("alert")).toContainText("changed on disk");
    expect(JSON.parse(await readFile(durablePath, "utf8")) as RecordFixture[]).toHaveLength(2);
    await dialog.getByRole("button", { name: "Reload library and preview again" }).click();
    await dialog.getByRole("button", { name: "Review resulting names" }).click();
    await dialog.getByRole("button", { name: "Confirm import", exact: true }).click(); await expect(trigger).toBeFocused();
    const stored = await request.get("/api/presets").then(response => response.json()) as RecordFixture[];
    expect(stored.map(item => item.id).sort()).toEqual([record(0).id, record(1).id, record(2).id].sort());
    expect((JSON.parse(await readFile(durablePath, "utf8")) as RecordFixture[]).map(item => item.id).sort()).toEqual(stored.map(item => item.id).sort());
    await unchangedSave(page, info);
  } finally { await clear(request); }
});
