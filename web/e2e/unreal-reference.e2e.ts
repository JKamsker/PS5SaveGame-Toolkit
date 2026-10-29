import { expect, test } from "@playwright/test";
import type { EditResult, SaveDocument } from "../src/api/protocol";
import { expectFiles, exportFiles } from "./helpers";
import { brightnessName, collection, consoleWarning, generateUnrealFixtures, jediPath, openUnreal,
  scalar, showSearch, unrealBytes } from "./unreal-helpers";
import { downloadBrightness, editBrightness, proveScalarNoOp, proveUnchangedDraft, reviewBrightness } from "./unreal-reference-flow";

let fixtures: string;
test.beforeAll(async ({ browserName }, testInfo) => {
  expect(browserName).toBe("chromium");
  fixtures = await generateUnrealFixtures(testInfo);
});

test("real Unreal references share one scalar, review, no-op history, undo/redo and exact reimport", async ({ page }, testInfo) => {
  const original = await unrealBytes(fixtures, "settings.sav");
  const edited = await unrealBytes(fixtures, "settings-edited.sav");
  const files = { [jediPath]: original, "PPSA02199/sdimg_E2E/nested/untouched.dat": Buffer.from([0, 255, 17, 42]) };
  const documentPath = await openUnreal(page, files, "settings.sav");
  const initial = await (await page.request.get(documentPath)).json() as SaveDocument;
  expect(initial.state).toMatchObject({ canUndo: false, canRedo: false, isDirty: false });
  const raw = scalar(await collection(page, documentPath, "gvas/properties"), "property/0");
  const named = scalar(await collection(page, documentPath, "jedi/settings"), "property/0");
  expect(named).toEqual(raw);
  expect(raw).toMatchObject({ value: 1.25, confidence: "Verified", warning: consoleWarning, spec: { kind: "float", singlePrecision: true } });
  expect(raw.spec.readOnly).not.toBe(true);
  await testInfo.attach("canonical-before.json", { body: JSON.stringify({ initial, raw, named }, null, 2), contentType: "application/json" });
  expectFiles(await exportFiles(page, testInfo, "unchanged.zip"), files);
  await proveScalarNoOp(page, documentPath, 1.25, testInfo, "clean", files);
  await editBrightness(page);
  const field = page.getByRole("textbox", { name: brightnessName });
  for (const tab of ["Explore all properties", "Settings", "Search"]) {
    if (tab === "Search") await showSearch(page);
    else await page.getByRole("tab", { name: tab, exact: true }).click();
    await expect(page.getByRole("textbox", { name: brightnessName })).toHaveValue("1.75");
  }
  const rawAfter = scalar(await collection(page, documentPath, "gvas/properties"), "property/0");
  const namedAfter = scalar(await collection(page, documentPath, "jedi/settings"), "property/0");
  expect(rawAfter).toEqual({ ...raw, value: 1.75 });
  expect(namedAfter).toEqual(rawAfter);
  const log = await reviewBrightness(page, documentPath, testInfo);
  await testInfo.attach("canonical-after-and-review.json", { body: JSON.stringify({ rawAfter, namedAfter, log }, null, 2), contentType: "application/json" });

  await proveUnchangedDraft(page, documentPath);
  await proveScalarNoOp(page, documentPath, 1.75, testInfo, "dirty", { ...files, [jediPath]: edited });
  await page.getByRole("button", { name: "Undo", exact: true }).click();
  await expect(field).toHaveValue("1.25");
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeDisabled();
  await expect(page.getByRole("button", { name: "Redo", exact: true })).toBeEnabled();
  expectFiles(await exportFiles(page, testInfo, "undone.zip"), files);
  await proveScalarNoOp(page, documentPath, 1.25, testInfo, "after-undo", files);
  await page.getByRole("button", { name: "Redo", exact: true }).click();
  await expect(field).toHaveValue("1.75");
  await expect(page.getByRole("button", { name: "Redo", exact: true })).toBeDisabled();
  await downloadBrightness(page, testInfo, edited);
  const exported = await exportFiles(page, testInfo, "edited.zip");
  expectFiles(exported, { ...files, [jediPath]: edited });
  await openUnreal(page, exported, "settings.sav");
  await showSearch(page);
  await expect(page.getByRole("textbox", { name: brightnessName })).toHaveValue("1.75");
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeDisabled();
  expectFiles(await exportFiles(page, testInfo, "reimported.zip"), exported);
});

test("real Unreal mixed scalar batch creates one history entry with exact output", async ({ page }, testInfo) => {
  const files = { [jediPath]: await unrealBytes(fixtures, "settings.sav") };
  const documentPath = await openUnreal(page, files, "settings.sav");
  const before = await (await page.request.get(documentPath)).json() as SaveDocument;
  const response = await page.request.post(`${documentPath}/edits`, {
    data: { edits: [{ valueId: "property/0", value: 1.25 }, { valueId: "property/0", value: 1.75 }], revision: before.state.revision },
  });
  expect(response.ok()).toBe(true);
  const result = await response.json() as EditResult;
  expect(result.ok).toBe(true);
  expect(result.changes).toEqual([
    { valueId: "property/0", oldValue: 1.25, newValue: 1.25 },
    { valueId: "property/0", oldValue: 1.25, newValue: 1.75 },
  ]);
  expect(result.state.revision).toBe((before.state.revision ?? 0) + 1);
  const actual = await (await page.request.get(documentPath)).json() as SaveDocument;
  expect(actual.state).toEqual(result.state);
  await testInfo.attach("real-api-mixed-batch.json", { body: JSON.stringify({ before, result, actual }, null, 2), contentType: "application/json" });
  expect(scalar(await collection(page, documentPath, "jedi/settings"), "property/0").value).toBe(1.75);
  const expected = { [jediPath]: await unrealBytes(fixtures, "settings-edited.sav") };
  expectFiles(await exportFiles(page, testInfo, "mixed-edited.zip"), expected);
  const undone = await (await page.request.post(`${documentPath}/undo`, { data: {} })).json() as EditResult;
  expect(undone.ok).toBe(true);
  expect(undone.state).toMatchObject({ canUndo: false, canRedo: true, isDirty: false });
  expect(scalar(await collection(page, documentPath, "gvas/properties"), "property/0").value).toBe(1.25);
  expectFiles(await exportFiles(page, testInfo, "mixed-undone.zip"), files);
  const redone = await (await page.request.post(`${documentPath}/redo`, { data: {} })).json() as EditResult;
  expect(redone.ok).toBe(true);
  expect(redone.state).toMatchObject({ canUndo: true, canRedo: false, isDirty: true });
  expectFiles(await exportFiles(page, testInfo, "mixed-redone.zip"), expected);
});
