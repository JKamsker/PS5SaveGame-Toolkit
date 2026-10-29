import { failures, test, expect, focusedControl, networkFailure } from "./failure-harness";
import { archive, coinsField, editCoins, editedCoins, home, importSave, loadJson, openJson, originalCoins, upload } from "./helpers";

for (const failure of failures) {
  test(`import ${failure.name} retains upload for an explicit retry`, async ({ page, faults }) => {
    await home(page);
    const blocked = await faults.block("**/api/workspaces", failure, "POST");
    await upload(page);
    await expect(page.getByRole("alert")).toHaveText(failure.message);
    const retry = page.getByRole("button", { name: "Retry import", exact: true });
    await expect(retry).toBeEnabled();
    await expect(page.getByRole("button", { name: "Open zip", exact: true })).toBeEnabled();
    await focusedControl(page);
    blocked.release();
    await retry.click();
    await expect(page.getByRole("heading", { name: "Pick a save file" })).toBeVisible();
    await expect(page.getByRole("alert")).toHaveCount(0);
    await focusedControl(page);
  });

  test(`open ${failure.name} keeps the file picker usable and retries`, async ({ page, faults }) => {
    await importSave(page);
    const blocked = await faults.block("**/files/*/open", failure);
    const file = page.getByRole("navigation", { name: "Save directories" }).getByRole("button")
      .filter({ has: page.getByText("save.json", { exact: true }) });
    await file.click();
    await expect(page.getByRole("alert")).toHaveText(failure.message);
    await expect(page.getByRole("heading", { name: "Pick a save file" })).toBeVisible();
    await expect(file).toBeEnabled();
    await focusedControl(page);
    blocked.release();
    await file.click();
    await expect(page.getByRole("heading", { name: "save.json", exact: true })).toBeVisible();
    await openJson(page);
    await loadJson(page);
    await expect(coinsField(page)).toHaveValue(originalCoins);
  });

  test(`edit ${failure.name} preserves the draft without a phantom undo entry`, async ({ page, faults }) => {
    await importSave(page);
    await openJson(page);
    await loadJson(page);
    const blocked = await faults.block("**/edits", failure);
    await coinsField(page).fill(editedCoins);
    await coinsField(page).press("Enter");
    await expect(page.getByRole("alert").filter({ hasText: failure.message }).first()).toBeVisible();
    await expect(coinsField(page)).toHaveValue(editedCoins);
    await expect(coinsField(page)).toBeFocused();
    await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeDisabled();
    await expect(coinsField(page)).not.toHaveAccessibleName(/modified/);
    blocked.release();
    await coinsField(page).press("Enter");
    await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeEnabled();
    await expect(coinsField(page)).toHaveAccessibleName(/modified/);
    await expect(coinsField(page)).not.toHaveAttribute("aria-invalid", "true");
    await page.getByRole("button", { name: "Undo", exact: true }).click();
    await expect(coinsField(page)).toHaveValue(originalCoins);
  });

  test(`undo and redo ${failure.name} preserve current values and history until retry`, async ({ page, faults }) => {
    await importSave(page);
    await openJson(page);
    await loadJson(page);
    await editCoins(page);
    for (const [command, before, after] of [["Undo", editedCoins, originalCoins], ["Redo", originalCoins, editedCoins]] as const) {
      const blocked = await faults.block(`**/${command.toLowerCase()}`, failure);
      const button = page.getByRole("button", { name: command, exact: true });
      await button.click();
      await expect.poll(blocked.count).toBe(1);
      await expect(page.getByRole("alert").filter({ hasText: failure.message }).first()).toBeVisible();
      await expect(coinsField(page)).toHaveValue(before);
      await expect(button).toBeEnabled();
      await focusedControl(page);
      blocked.release();
      await button.click();
      await expect(coinsField(page)).toHaveValue(after);
    }
  });
}

test("offline edit is actionable and the same draft commits after reconnection", async ({ page, context, faults }) => {
  await importSave(page);
  await openJson(page);
  await loadJson(page);
  faults.expectNetworkFailures(/\/edits$/);
  await context.setOffline(true);
  await coinsField(page).fill(editedCoins);
  await coinsField(page).press("Enter");
  await expect(page.getByRole("alert").filter({ hasText: networkFailure.message }).first()).toBeVisible();
  await expect(coinsField(page)).toHaveValue(editedCoins);
  await expect(coinsField(page)).toBeFocused();
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeDisabled();
  await context.setOffline(false);
  await coinsField(page).press("Enter");
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeEnabled();
  await expect(coinsField(page)).toHaveAccessibleName(/modified/);
});

test("network failure during import can retry the retained upload", async ({ page, faults }) => {
  await home(page);
  const blocked = await faults.block("**/api/workspaces", networkFailure, "POST");
  // A failed XHR has no response event, so drive the file chooser directly.
  const choosing = page.waitForEvent("filechooser");
  await page.getByRole("button", { name: "Open zip", exact: true }).click();
  await (await choosing).setFiles({ name: "offline.zip", mimeType: "application/zip", buffer: archive() });
  await expect(page.getByRole("alert")).toHaveText(networkFailure.message);
  blocked.release();
  await page.getByRole("button", { name: "Retry import", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Pick a save file" })).toBeVisible();
});
