import { execFile } from "node:child_process";
import { readFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { promisify } from "node:util";
import type { Locator, Page, TestInfo } from "@playwright/test";
import type { SaveDocument } from "../src/api/protocol";
import { expect, test } from "./failure-harness";
import { exportFiles, importSave } from "./helpers";
import { apiSettled, waitForApiResponse } from "./apiRoutes";
import { apiRequest } from "./apiRequest";

/**
 * Edits whose export re-encodes LZ4 blocks: K4os' HC encoder trapped the WebAssembly runtime
 * ("memory access out of bounds") on exactly these downloads, exports and actions, and its fast level
 * wrote its 32-bit engine's bytes there (valid LZ4, but not the native bytes for blocks over 64 KiB).
 * The authored saves and the bytes the native plugins write after the same edit come from
 * tests/fixtures/SaveToolkit.CodecBrowserFixture; the browser must reproduce them byte for byte.
 */
interface CodecFixture { name: string; plugin: string; path: string; valueId: string; value: string; original: Buffer; edited: Buffer }

let fixtures: CodecFixture[];

test.beforeAll(async ({ browserName }, testInfo) => {
  expect(browserName).toBe("chromium");
  // A cold build of the generator into its own --artifacts-path can exceed the hook default.
  testInfo.setTimeout(150_000);
  const { stdout } = await promisify(execFile)("dotnet", ["run", "--project", "tests/fixtures/SaveToolkit.CodecBrowserFixture",
    "--configuration", "Release", "--artifacts-path", path.join(tmpdir(), "codec-fixture-build")], {
    cwd: fileURLToPath(new URL("../..", import.meta.url)), timeout: 120_000, maxBuffer: 4 * 1024 * 1024,
  });
  const line = stdout.split(/\r?\n/).find(text => text.startsWith("CODEC_FIXTURE:"));
  expect(line).toBeTruthy();
  type Encoded = Omit<CodecFixture, "original" | "edited"> & { original: string; edited: string };
  fixtures = (JSON.parse(line!.slice("CODEC_FIXTURE:".length)) as Encoded[]).map(fixture => ({
    ...fixture, original: Buffer.from(fixture.original, "base64"), edited: Buffer.from(fixture.edited, "base64"),
  }));
});

const fixture = (name: string) => fixtures.find(entry => entry.name === name)!;

async function open(page: Page, save: CodecFixture) {
  await importSave(page, { [save.path]: save.original });
  const opening = waitForApiResponse(page, response => response.url().endsWith("/open") && response.request().method() === "POST");
  const name = save.path.split("/").pop()!;
  await page.getByRole("navigation", { name: "Save directories" }).getByRole("button")
    .filter({ has: page.getByText(name, { exact: true }) }).click();
  const response = await opening;
  expect(response.ok()).toBe(true);
  const document = await response.json() as SaveDocument;
  expect(document.plugin.id).toBe(save.plugin);
  return new URL(response.url()).pathname.replace(/\/files\/[^/]+\/open$/, `/documents/${document.id}`);
}

async function edit(page: Page, field: Locator, value: string, timeout?: number) {
  await field.fill(value);
  await field.press("Enter");
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeEnabled({ timeout });
  await expect(field).toHaveValue(value);
}

async function downloadFile(page: Page, testInfo: TestInfo, name: string) {
  const downloading = page.waitForEvent("download");
  await page.getByRole("button", { name: "Download file", exact: true }).click();
  const download = await downloading;
  const destination = testInfo.outputPath(name);
  await download.saveAs(destination);
  expect(await download.failure()).toBeNull();
  return readFile(destination);
}

test("Witcher 3 edit, download, export and action re-encode LZ4 HC exactly like the native plugin", async ({ page }, testInfo) => {
  const save = fixture("witcher3");
  const documentPath = await open(page, save);
  await page.getByRole("tab", { name: "Player", exact: true }).click();
  await edit(page, page.getByRole("textbox", { name: /^Character level/ }), save.value);
  // GET …/download: the edited chunk is LZ4 L12_MAX-encoded in the runtime.
  expect((await downloadFile(page, testInfo, "Manual.sav")).equals(save.edited)).toBe(true);
  // POST …/export: the same bytes inside the dump.
  const exported = await exportFiles(page, testInfo);
  expect(Object.keys(exported)).toEqual([save.path]);
  expect(Buffer.from(exported[save.path]!).equals(save.edited)).toBe(true);
  // An action on the edited document serializes it as well; the runtime keeps answering.
  await apiSettled(page);
  const state = await (await apiRequest(page).get(documentPath)).json() as SaveDocument;
  const action = await apiRequest(page).post(`${documentPath}/actions`,
    { data: { actionId: "catalog/inspect", parameters: {}, revision: state.state.revision } });
  expect(action.ok()).toBe(true);
  expect((await apiRequest(page).get(documentPath)).ok()).toBe(true);
  await expect(page.getByRole("alert").filter({ hasText: "The editor runtime stopped unexpectedly." })).toHaveCount(0);
});

test("Baldur's Gate 3 package edit re-encodes LZ4 HC exactly like the native plugin", async ({ page }, testInfo) => {
  const save = fixture("bg3");
  await open(page, save);
  await page.getByRole("tab", { name: "Party", exact: true }).click();
  // The package entry and its file table are rewritten as LZ4 L10_OPT blocks.
  await edit(page, page.getByRole("textbox", { name: "Level (Generic)", exact: true }), save.value);
  expect((await downloadFile(page, testInfo, "AutoSave_0.lsv")).equals(save.edited)).toBe(true);
  const exported = await exportFiles(page, testInfo);
  expect(Buffer.from(exported[save.path]!).equals(save.edited)).toBe(true);
  await expect(page.getByRole("alert").filter({ hasText: "The editor runtime stopped unexpectedly." })).toHaveCount(0);
});

test("Baldur's Gate 3 fast-level entry over 64 KiB re-encodes exactly like the native plugin", async ({ page }, testInfo) => {
  const save = fixture("bg3-fast");
  await open(page, save);
  await page.getByRole("tab", { name: "Party", exact: true }).click();
  // SaveInfo.json (flags 0x12, about 120 KB) is rewritten as an LZ4 fast-level block.
  await edit(page, page.getByRole("textbox", { name: "Level (Generic)", exact: true }), save.value);
  expect((await downloadFile(page, testInfo, "AutoSave_1.lsv")).equals(save.edited)).toBe(true);
  const exported = await exportFiles(page, testInfo);
  expect(Buffer.from(exported[save.path]!).equals(save.edited)).toBe(true);
  await expect(page.getByRole("alert").filter({ hasText: "The editor runtime stopped unexpectedly." })).toHaveCount(0);
});

test("Cyberpunk 2077 fact edit re-encodes its 256 KiB chunk exactly like the native plugin", async ({ page }, testInfo) => {
  // 40,000 facts: the interpreter needs longer than the default budgets to rebuild the views after the edit.
  test.setTimeout(90_000);
  const save = fixture("cyberpunk2077");
  await open(page, save);
  await page.getByRole("tab", { name: "Facts", exact: true }).click();
  // Fact 0 is the first row; its value lies in the first CSAV chunk (256 KiB, LZ4 fast level).
  const facts = page.getByRole("table", { name: "Saved facts (raw experimental edits)", exact: true });
  await edit(page, facts.getByRole("textbox", { name: /^Raw saved fact value/ }).first(), save.value, 30_000);
  expect((await downloadFile(page, testInfo, "sav.dat")).equals(save.edited)).toBe(true);
  const exported = await exportFiles(page, testInfo);
  expect(Object.keys(exported)).toEqual([save.path]);
  expect(Buffer.from(exported[save.path]!).equals(save.edited)).toBe(true);
  await expect(page.getByRole("alert").filter({ hasText: "The editor runtime stopped unexpectedly." })).toHaveCount(0);
});
