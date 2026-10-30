import { expect, type Page, type TestInfo } from "@playwright/test";
import { actions, expectFiles, manifest, snapshot, state, type Ff7Fixture } from "./ff7-helpers";
import { exportFiles, type SaveFiles } from "./helpers";

export async function selectRows(page: Page, tab: "Inventory" | "Characters" | "Materia", indices: number[]) {
  await page.getByRole("tab", { name: tab, exact: true }).click();
  const load = page.getByRole("button", { name: `Load ${tab}`, exact: true });
  if (await load.isVisible()) await load.click();
  const table = page.getByRole("table", { name: tab === "Characters" ? "Character sheets" : tab, exact: true });
  await expect(table.getByRole("row").nth(1)).toBeVisible();
  for (const index of indices) await table.getByRole("row").nth(index + 1).getByRole("checkbox").check();
}

export async function selectedAction(page: Page, action: string) {
  const panel = page.locator(".presets-panel");
  await panel.getByRole("combobox", { name: "Action", exact: true }).selectOption(action);
  await panel.getByRole("combobox", { name: "Scope", exact: true }).selectOption("selected");
  await expect(panel.getByRole("button", { name: "Save preset", exact: true })).toBeEnabled();
  if (action === actions.stars) {
    const stars = panel.getByRole("textbox", { name: "Target stars", exact: true });
    await stars.fill("2");
    await stars.press("Tab");
  }
}

export async function applyAndHistory(page: Page, documentPath: string, original: SaveFiles, expected: SaveFiles, testInfo: TestInfo) {
  const applying = page.waitForResponse(response => /\/presets\/[^/]+\/apply$/.test(response.url()));
  await page.locator(".presets-preview").getByRole("button", { name: "Apply preset", exact: true }).click();
  expect((await applying).status()).toBe(200);
  await expect(page.locator(".presets-preview")).not.toBeVisible();
  expect(await state(page.request, documentPath)).toMatchObject({ revision: 1, canUndo: true, canRedo: false });
  expectFiles((await snapshot(page.request, documentPath)).files, expected);
  await page.getByRole("button", { name: "Undo", exact: true }).click();
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeDisabled();
  expect(await state(page.request, documentPath)).toMatchObject({ revision: 2, canUndo: false, canRedo: true });
  expectFiles((await snapshot(page.request, documentPath)).files, original);
  await page.getByRole("button", { name: "Redo", exact: true }).click();
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeEnabled();
  expect(await state(page.request, documentPath)).toMatchObject({ revision: 3, canUndo: true, canRedo: false });
  const downloaded = await exportFiles(page, testInfo, "ff7-selected-whole.zip");
  expectFiles(downloaded, expected);
  await manifest(testInfo, "ff7-selected-whole-members", downloaded);
  return downloaded;
}

export function withFiles(binary: Ff7Fixture, files: SaveFiles): Ff7Fixture { return { ...binary, files }; }
