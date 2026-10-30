import { test, expect } from "@playwright/test";
import { readFile, writeFile } from "node:fs/promises";
import { clear, seed, record, open, file, libraryPath, transferPath, unchangedSave, type RecordFixture } from "./presetTransferFixtures";

test("external addition requires explicit reload before fresh preview and survives the unrelated import", async ({ page, request }, info) => {
  await clear(request);
  try {
    await seed(request, [record(0)]);
    const { trigger, dialog } = await open(page); await trigger.click();
    const incoming = file([record(2)]);
    await dialog.getByLabel("Preset package").setInputFiles(incoming);
    await dialog.getByRole("button", { name: "Review resulting names" }).click();
    const durablePath = libraryPath();
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
