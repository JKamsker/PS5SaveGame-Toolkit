import { expect, test } from "@playwright/test";
import type { EditResult, ViewNode } from "../src/api/protocol";
import { expectFiles, exportFiles } from "./helpers";
import { brightnessName, collection, consoleWarning, contained, generateUnrealFixtures, jediPath,
  openUnreal, scalar, screenshot, tabTo, unrealBytes, wideSummary, wideTail } from "./unreal-helpers";

let fixtures: string;
test.beforeAll(async ({ browserName }, testInfo) => {
  expect(browserName).toBe("chromium");
  fixtures = await generateUnrealFixtures(testInfo);
});

for (const width of [390, 768, 1024, 1440]) {
  test(`real Jedi controls and full warnings remain reachable and contained at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    const original = await unrealBytes(fixtures, "settings.sav");
    await openUnreal(page, { [jediPath]: original }, "settings.sav");
    const overview = page.getByRole("tab", { name: "Overview", exact: true });
    await tabTo(page, overview);
    await page.keyboard.press("End");
    const settings = page.getByRole("tab", { name: "Settings", exact: true });
    await expect(settings).toBeFocused();
    await expect(settings).toHaveAttribute("aria-selected", "true");
    const search = page.getByRole("searchbox", { name: "Search Display, input and saved overrides", exact: true });
    await tabTo(page, search);
    await search.fill("HDRBrightness");
    await expect(search).toBeFocused();
    const table = page.getByRole("table", { name: "Display, input and saved overrides", exact: true });
    const details = table.getByRole("button", { name: /^Details for/ });
    await tabTo(page, details);
    await page.keyboard.press("Enter");
    await expect(details).toHaveAttribute("aria-expanded", "true");
    const expanded = page.locator(".row-details");
    const field = expanded.getByRole("textbox", { name: brightnessName });
    await tabTo(page, field);
    await expect(field).toHaveValue("1.25");
    await expect(field).toHaveAccessibleName(/verified/);
    await expect(field).toHaveAccessibleDescription(new RegExp("Not validated on console"));
    const warning = expanded.getByText(consoleWarning, { exact: true });
    await expect(warning).toBeVisible();
    await contained(page, field, width);
    await contained(page, warning, width);
    expect(await warning.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    expect(await warning.evaluate(element => element.scrollHeight <= element.clientHeight)).toBe(true);
    await screenshot(page, testInfo, `jedi-settings-${width}`);
    await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeDisabled();
    expectFiles(await exportFiles(page, testInfo, `unchanged-${width}.zip`), { [jediPath]: original });
  });
}

test("real Sackboy projections retain inferred warnings, container summaries and a protected checksum", async ({ page }, testInfo) => {
  const savePath = "PPSA01288/sdimg_E2E/sackboy.sav";
  const original = await unrealBytes(fixtures, "sackboy.sav");
  const documentPath = await openUnreal(page, { [savePath]: original }, "sackboy.sav");
  const raw = await collection(page, documentPath, "gvas/properties");
  const progression = await collection(page, documentPath, "gvas/group/Progression");
  const currency = scalar(raw, "property/0/0");
  expect(scalar(progression, "property/0/0")).toEqual(currency);
  expect(currency).toMatchObject({ value: "17", confidence: "Inferred" });
  expect(currency.warning).toContain("Checksum coverage is unproven");
  expect(scalar(raw, "property/2").spec.readOnly).toBe(true);
  expect(raw.rows.find(row => row.key === "property/0")?.cells.value).toMatchObject({ type: "text", text: expect.stringContaining("children") });
  expect(raw.rows.find(row => row.key === "property/1")?.cells.value).toMatchObject({ type: "text", text: expect.stringContaining("FutureProperty") });
  await page.getByRole("tab", { name: "Explore all properties", exact: true }).click();
  const table = page.getByRole("table", { name: "All nested properties", exact: true });
  const checksum = table.getByRole("row").filter({ hasText: /Checksum · UInt32Property/ });
  await expect(checksum.locator(".readonly-value")).toHaveText("305419896");
  await expect(checksum.getByRole("textbox")).toHaveCount(0);
  await page.getByRole("tab", { name: "Progression", exact: true }).click();
  const namedTable = page.getByRole("table", { name: "Progression", exact: true });
  const currencyRow = namedTable.getByRole("row").filter({ has: page.getByRole("textbox", { name: /^CollectabellsStored/ }) });
  await expect(currencyRow.getByRole("img", { name: "Confidence: inferred", exact: true })).toBeVisible();
  await currencyRow.getByRole("button", { name: /^Details for/ }).click();
  await expect(page.locator(".row-details .field__warning")).toHaveText(currency.warning!);
  await page.locator(".row-details .field__warning").scrollIntoViewIfNeeded();
  await screenshot(page, testInfo, "sackboy-inferred-currency");
  const rejected = await page.request.post(`${documentPath}/edits`, { data: { edits: [{ valueId: "property/2", value: "0" }] } });
  expect(rejected.status()).toBe(422);
  const result = await rejected.json() as EditResult;
  expect(result.ok).toBe(false);
  expect(result.state).toMatchObject({ canUndo: false, isDirty: false });
  await testInfo.attach("sackboy-actual-host.json", { body: JSON.stringify({ raw, progression, rejected: result }, null, 2), contentType: "application/json" });
  expectFiles(await exportFiles(page, testInfo, "sackboy-unchanged.zip"), { [savePath]: original });
});

test("real Jedi actor observations, container and CRC/length remain read-only", async ({ page }, testInfo) => {
  const savePath = "PPSA02199/sdimg_E2E/actors.sav";
  const original = await unrealBytes(fixtures, "actors.sav");
  const documentPath = await openUnreal(page, { [savePath]: original }, "actors.sav");
  const raw = await collection(page, documentPath, "gvas/properties");
  expect(scalar(raw, "property/1").spec.readOnly).toBe(true);
  expect(scalar(raw, "property/2").spec.readOnly).toBe(true);
  expect(raw.rows.find(row => row.key === "property/0")?.cells.value?.type).toBe("text");
  const actors = await collection(page, documentPath, "jedi/actor-tags");
  expect(actors.rows).toHaveLength(1);
  expect(actors.rows[0]!.cells.value).toMatchObject({ type: "text", text: "42" });
  await page.getByRole("tab", { name: "Actors", exact: true }).click();
  const table = page.getByRole("table", { name: "Decoded actor properties", exact: true });
  await expect(table.getByRole("textbox")).toHaveCount(0);
  await expect(table.getByRole("row").filter({ hasText: "ActorRecords" })).toContainText("42");
  await expect(page.getByText(/Observed strings are not verified actor identities/)).toBeVisible();
  await table.getByRole("row").filter({ hasText: "ActorRecords" }).scrollIntoViewIfNeeded();
  await expect(table.getByRole("row").filter({ hasText: "ActorRecords" })).toBeVisible();
  await screenshot(page, testInfo, "jedi-readonly-actors");
  await testInfo.attach("actors-actual-host.json", { body: JSON.stringify({ raw, actors }, null, 2), contentType: "application/json" });
  expectFiles(await exportFiles(page, testInfo, "actors-unchanged.zip"), { [savePath]: original });
});

test("real title long-text fallback searches full values and returns 200 bounded normalized rows", async ({ page }, testInfo) => {
  const savePath = "PPSA02199/sdimg_E2E/wide.sav";
  const original = await unrealBytes(fixtures, "wide.sav");
  const documentPath = await openUnreal(page, { [savePath]: original }, "wide.sav");
  const evidence = [];
  for (const id of ["gvas/properties", "jedi/settings"]) {
    const result = await collection(page, documentPath, id, wideTail);
    expect(result.rows).toHaveLength(200);
    expect(result.filteredCount).toBe(200);
    for (const row of result.rows) expect(row.cells.value).toMatchObject({ type: "text", text: wideSummary });
    evidence.push(result);
  }
  // Lazy Search also materializes title fallback text, without inventing writable references.
  const search = await page.request.get(`${documentPath}/nodes`, { params: { id: "search" } });
  expect(search.ok()).toBe(true);
  const nodes = await search.json() as ViewNode[];
  const eager = nodes.find(node => node.type === "table");
  expect(eager?.type).toBe("table");
  if (eager?.type !== "table") throw new Error("Expected the actual Unreal Search table");
  expect(eager.rows).toHaveLength(100);
  for (const row of eager.rows) expect(row.cells.value).toMatchObject({ type: "text", text: wideSummary });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole("tab", { name: "Settings", exact: true }).click();
  const filter = page.getByRole("searchbox", { name: "Search Display, input and saved overrides", exact: true });
  await filter.fill(wideTail);
  await expect(page.getByText("200 of 200 rows", { exact: true })).toBeVisible();
  const table = page.getByRole("table", { name: "Display, input and saved overrides", exact: true });
  await expect(table.getByRole("textbox")).toHaveCount(0);
  await table.getByRole("button", { name: /^Details for/ }).first().click();
  const summary = page.locator(".row-details").getByText(wideSummary, { exact: true });
  await expect(summary).toBeVisible();
  await summary.scrollIntoViewIfNeeded();
  await contained(page, summary, 390);
  expect(await summary.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
  expect(await summary.evaluate(element => element.scrollHeight <= element.clientHeight)).toBe(true);
  await screenshot(page, testInfo, "jedi-long-text-390");
  await testInfo.attach("long-text-actual-host.json", { body: JSON.stringify(evidence, null, 2), contentType: "application/json" });
  expectFiles(await exportFiles(page, testInfo, "wide-unchanged.zip"), { [savePath]: original });
});

test("real Unreal rows recover after a transport failure without manufactured reference DTOs", async ({ page }, testInfo) => {
  const original = await unrealBytes(fixtures, "settings.sav");
  await openUnreal(page, { [jediPath]: original }, "settings.sav");
  let failReads = true;
  let failures = 0;
  await page.route("**/collections?**", async route => {
    if (failReads) { failures++; await route.abort("failed"); }
    else await route.continue();
  });
  await page.getByRole("tab", { name: "Settings", exact: true }).click();
  const retry = page.getByRole("button", { name: "Retry rows", exact: true });
  await expect(retry).toBeVisible({ timeout: 10_000 });
  expect(failures).toBe(3); // Initial GET and the existing two bounded automatic retries.
  await expect(page.getByRole("alert")).toContainText("server");
  await expect(page.getByRole("textbox", { name: brightnessName })).toHaveCount(0);
  failReads = false;
  await retry.focus();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("textbox", { name: brightnessName })).toHaveValue("1.25");
  await expect(page.getByRole("heading", { name: "Display, input and saved overrides", exact: true })).toBeFocused();
  await expect(page.getByRole("alert")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeDisabled();
  expectFiles(await exportFiles(page, testInfo, "retried-unchanged.zip"), { [jediPath]: original });
});
