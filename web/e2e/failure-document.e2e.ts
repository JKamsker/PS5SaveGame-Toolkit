import { failures, test, expect, focusedControl, networkFailure } from "./failure-harness";
import { archive, coinsField, editCoins, editedCoins, home, importSave, leavePage, loadJson, openJson, originalCoins, runtimeReady, upload } from "./helpers";
import { apiSettled, onApi, routeApi } from "./apiRoutes";

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

test("a runtime transport failure during an edit is actionable and the same draft commits on retry, even offline", async ({ page, context, faults }) => {
  await importSave(page);
  await openJson(page);
  await loadJson(page);
  // The runtime runs in the page, so going offline cannot fail an edit; inject the transport failure instead.
  const blocked = await faults.block("**/edits", networkFailure, "POST");
  await coinsField(page).fill(editedCoins);
  await coinsField(page).press("Enter");
  await expect(page.getByRole("alert").filter({ hasText: networkFailure.message }).first()).toBeVisible();
  await expect(coinsField(page)).toHaveValue(editedCoins);
  await expect(coinsField(page)).toBeFocused();
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeDisabled();
  expect(blocked.count()).toBe(1);
  blocked.release();
  await context.setOffline(true);
  await coinsField(page).press("Enter");
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeEnabled();
  await expect(coinsField(page)).toHaveAccessibleName(/modified/);
  await context.setOffline(false);
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

// web/src/api/http.ts runtimeCrashedMessage: what a request learns once the runtime stopped.
const runtimeCrashed = "The in-browser editor runtime stopped unexpectedly, and the workspaces in this tab were lost with it. "
  + "Reload the page and import your saves again.";

test("a runtime crash during an edit fails the pending request, explains the loss and offers a reload", async ({ page, faults }) => {
  await importSave(page);
  await openJson(page);
  await loadJson(page);
  // The stopped runtime answers nothing: its pending requests fail, and it logs why it stopped.
  faults.expectNetworkFailures(/\/edits$/);
  faults.expectConsoleErrors(/injected runtime crash|exited with code|ExitStatus|program terminated/i);
  const failed: string[] = [];
  onApi(page, "requestfailed", request => failed.push(`${request.method()} ${new URL(request.url()).pathname}`));
  await routeApi(page, "**/edits", route => route.crashRuntime(), { times: 1 });
  await coinsField(page).fill(editedCoins);
  await coinsField(page).press("Enter");
  // The pending edit is rejected at once (not left to its 60 s deadline), with the crash explained.
  const editFailure = page.getByRole("alert").filter({ hasText: runtimeCrashed }).first();
  await expect(editFailure).toBeVisible();
  const runtimeAlert = page.getByRole("alert").filter({ hasText: "The editor runtime stopped unexpectedly." });
  await expect(runtimeAlert).toContainText("lost with it");
  // The detail is the worker's own report of the stop (web/src/runtime/dotnetBoot.ts onExit), not a fabrication.
  await expect(runtimeAlert).toContainText("The .NET runtime exited with code 1.");
  await apiSettled(page);
  expect(failed.filter(request => request.startsWith("POST ") && request.endsWith("/edits"))).toHaveLength(1);
  await expect(coinsField(page)).toHaveValue(editedCoins);
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeDisabled();
  // Later requests fail immediately with the same explanation instead of reaching the dead worker.
  await coinsField(page).press("Enter");
  await expect(editFailure).toBeVisible();
  // Reload starts a new runtime; the workspace of the crashed one is gone.
  faults.expectNetworkFailures(/\/api\/workspaces\/[^/]+$/);
  await leavePage(page, () => runtimeAlert.getByRole("button", { name: "Reload", exact: true }).click());
  await expect(page.getByRole("heading", { name: "This workspace is gone", exact: true })).toBeVisible({ timeout: 30_000 });
  await runtimeReady(page);
  await expect(page.getByRole("alert").filter({ hasText: "The editor runtime stopped unexpectedly." })).toHaveCount(0);
});
