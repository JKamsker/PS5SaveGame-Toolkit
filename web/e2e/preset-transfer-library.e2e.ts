import { test, expect } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { apiRequest } from "./apiRequest";
import { home } from "./helpers";
import { clear, record, seed, open, file, unchangedSave, type RecordFixture } from "./presetTransferFixtures";

// Avoid recording a full DOM snapshot for each of 256 choices; retain calls, network, sources and failure screenshots.
// Ordinary transfer, stale recovery and narrow focus scenarios retain full DOM traces.
test.use({ trace: { mode: "retain-on-failure", snapshots: false, screenshots: true, sources: true, attachments: true } });

test("full 256-record library exports and explicitly replaces maximum Unicode IDs through the browser", async ({ page }, info) => {
  test.setTimeout(60_000);
  // Each test gets a fresh browser context, so the library starts empty; clear proves it through its file.
  await home(page); await clear(page);
  const records = Array.from({ length: 256 }, (_, index) => record(index));
  expect(records.every(item => item.id.length === 128)).toBe(true);
  await seed(page, records);
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
  for (let index = 0; index < records.length; index++) {
    await dialog.locator(`input[name="import-${index}"]`).nth(1).focus();
    await page.keyboard.press("Space");
  }
  await expect(dialog.getByRole("radio", { checked: true })).toHaveCount(256);
  await dialog.getByRole("button", { name: "Review resulting names" }).click();
  await expect(dialog.getByText(/^Replaces “Preset/)).toHaveCount(256);
  await dialog.getByRole("button", { name: "Confirm import", exact: true }).click(); await expect(trigger).toBeFocused();
  const stored = await apiRequest(page).get("/api/presets").then(response => response.json()) as RecordFixture[];
  expect(stored).toHaveLength(256); expect(stored.every(item => item.actions[0]!.parameters.flag)).toBe(true);
  await unchangedSave(page, info);
});
