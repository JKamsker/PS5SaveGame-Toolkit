import { test, expect } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { importSave, openJson, loadJson, originalSave } from "./helpers";

test("real preset import/export is exact, duplicate-aware, stale-safe and leaves save history untouched", async ({ page, request }, testInfo) => {
  await importSave(page); await openJson(page); await loadJson(page);
  await page.getByText("Toolkit presets", { exact: true }).click();
  const transfer = page.getByRole("button", { name: "Import presets", exact: true });
  await transfer.click();
  const dialog = page.getByRole("dialog", { name: "Import toolkit presets" });
  const file = transferFile();
  await dialog.getByLabel("Preset package").setInputFiles(file);
  await expect(dialog.getByText(/Stored for later/)).toBeVisible();
  await dialog.getByRole("button", { name: "Review resulting names" }).click();
  await dialog.getByRole("button", { name: "Confirm import", exact: true }).click();
  await expect(transfer).toBeFocused();
  await page.getByRole("button", { name: "Export presets", exact: true }).click();
  const exporting = page.getByRole("dialog", { name: "Export toolkit presets" });
  await exporting.getByRole("checkbox", { name: /Select all/ }).check();
  const downloaded = page.waitForEvent("download");
  await exporting.getByRole("button", { name: "Download 1 preset" }).click();
  const download = await downloaded;
  const destination = testInfo.outputPath("package.json"); await download.saveAs(destination);
  const raw = await readFile(destination, "utf8");
  expect(raw).toContain("900719925474099312345678901");
  expect(raw).toContain("1.2300e");
  expect(download.suggestedFilename()).toBe("Toolkit presets.toolkit-presets.json");
  await transfer.click(); await dialog.getByLabel("Preset package").setInputFiles(file);
  await expect(dialog.getByRole("button", { name: "Review resulting names" })).toBeDisabled();
  await dialog.getByRole("radio", { name: /Keep both/ }).check();
  await dialog.getByRole("button", { name: "Review resulting names" }).click();
  await expect(dialog.getByText("装備 · exact 🧰 (2)", { exact: true })).toBeVisible();
  const records = await request.get("/api/presets").then(r => r.json()) as { id: string }[];
  expect((await request.patch(`/api/presets/${records[0]!.id}`, { data: { name: "Concurrent rename" } })).ok()).toBe(true);
  await dialog.getByRole("button", { name: "Confirm import", exact: true }).click();
  await expect(dialog.getByRole("alert")).toContainText("library changed");
  await dialog.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(transfer).toBeFocused();
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeDisabled();
  // Native browser download route proves the imported synthetic save itself is unchanged.
  const saveDownload = page.waitForEvent("download");
  await page.getByRole("link", { name: /Download file/ }).click();
  const saved = await saveDownload; const savePath = testInfo.outputPath("save.json"); await saved.saveAs(savePath);
  expect(await readFile(savePath)).toEqual(originalSave);
});

for (const width of [375, 768, 1024, 1440]) test(`preset transfer handles errors, long names and Escape at ${width}px`, async ({ page }, testInfo) => {
  await page.setViewportSize({ width, height: 900 });
  await importSave(page); await openJson(page); await loadJson(page);
  await page.getByText("Toolkit presets", { exact: true }).click();
  const trigger = page.getByRole("button", { name: "Import presets", exact: true }); await trigger.click();
  const dialog = page.getByRole("dialog", { name: "Import toolkit presets" });
  await expect(dialog.getByRole("heading", { name: "Import toolkit presets" })).toBeFocused();
  await dialog.getByLabel("Preset package").setInputFiles({ name: "bad.json", mimeType: "application/json", buffer: Buffer.from("{") });
  await expect(dialog.getByRole("alert")).toContainText("malformed");
  await dialog.getByLabel("Preset package").setInputFiles(transferFile("長い名前🧰".repeat(12)));
  await expect(dialog.getByText(/Stored for later/)).toBeVisible();
  expect(await dialog.evaluate(node => node.scrollWidth <= node.clientWidth)).toBe(true);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath(`transfer-${width}.png`) });
  await page.keyboard.press("Escape"); await expect(dialog).not.toBeVisible(); await expect(trigger).toBeFocused();
});

function transferFile(name = "装備 · exact 🧰") {
  const preset = {
    schemaVersion: "1", id: "synthetic-portable", name, createdAt: "2026-10-29T00:00:00Z", updatedAt: "2026-10-29T00:01:00Z",
    compatibility: { pluginId: "unknown-for-later", pluginVersion: "1", titleIds: ["OTHER"], gameBuild: null, formatVariant: null,
      referencePackIdentity: null, requiresExplicitInstanceResolution: true, presetKind: "toolkit.action-parameters", schemaFingerprint: null, fileRole: null, capabilitySchemaVersion: null },
    actions: [{ actionId: "semantic-action", parameters: { exact: { kind: "exactNumber", text: "1.2300e+120" }, huge: "REPLACE_NUMBER" }, instances: [], scopeKind: "document" }],
  };
  const packageBytes = Buffer.from(JSON.stringify({ schemaVersion: "1", provenance: { producer: "Synthetic fixture", exportedAt: "2026-10-29T00:02:00Z" }, presets: [preset] })
    .replace('"REPLACE_NUMBER"', "900719925474099312345678901"));
  return { name: "portable.json", mimeType: "application/json", buffer: packageBytes };
}
