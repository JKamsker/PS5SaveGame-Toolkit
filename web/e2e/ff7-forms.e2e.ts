import { expect, test } from "@playwright/test";
import { actions, capture, expectFiles, fixture, generateFf7Fixtures, openFf7, recordApi, saved, snapshot, unchanged } from "./ff7-helpers";
import { contained, screenshot, tabTo } from "./unreal-helpers";

let directory: string;
test.use({ trace: "on", screenshot: "on" });
test.beforeAll(async ({ request }, testInfo) => { expect(request).toBeDefined(); directory = await generateFf7Fixtures(testInfo); });

for (const width of [375, 768, 1024, 1440]) {
  test(`real FF7 five typed action forms, scopes and library at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 1000 });
    const flush = recordApi(page, testInfo);
    const binary = await fixture(directory, "remake");
    const documentPath = await openFf7(page, binary);
    const panel = page.locator(".presets-panel");
    const action = panel.getByRole("combobox", { name: "Action", exact: true });
    await expect(action.locator("option")).toHaveCount(5);
    expect((await action.locator("option").evaluateAll(nodes => nodes.map(node => (node as HTMLOptionElement).value))).sort())
      .toEqual(Object.values(actions).sort());
    const doc = await (await page.request.get(documentPath)).json() as { presetCapability: { actions: { actionId: string; scopeKinds: string[] }[] } };
    for (const id of Object.values(actions)) {
      await action.selectOption(id);
      const scopes = await panel.getByRole("combobox", { name: "Scope", exact: true }).locator("option")
        .evaluateAll(nodes => nodes.map(node => (node as HTMLOptionElement).value));
      expect(scopes).toEqual(doc.presetCapability.actions.find(candidate => candidate.actionId === id)!.scopeKinds);
      if (id === actions.recover) await expect(panel.getByText("This action has no parameters.", { exact: true })).toBeVisible();
      else if (id === actions.restock) await expect(panel.getByRole("textbox", { name: "Target count", exact: true })).toBeVisible();
      else if (id === actions.level) await expect(panel.getByRole("textbox", { name: "Target level", exact: true })).toBeVisible();
      else {
        await expect(panel.getByText("Materia type", { exact: true })).toBeVisible();
        await expect(panel.getByRole("textbox", { name: id === actions.ap ? "Target AP" : "Target stars", exact: true })).toBeVisible();
      }
      const label = id === actions.restock ? "Target count" : id === actions.ap ? "Target AP"
        : id === actions.stars ? "Target stars" : id === actions.level ? "Target level" : undefined;
      const value = id === actions.restock ? "99" : id === actions.ap ? "1" : id === actions.stars ? "2" : "25";
      if (label) {
        const input = panel.getByRole("textbox", { name: label, exact: true });
        await input.fill(value);
        await input.press("Tab");
      }
      const before = await snapshot(page.request, documentPath);
      const name = `FF7 ${width} ${id}`;
      const preset = await capture(page, name);
      expect(preset.actions[0]!.actionId).toBe(id);
      expect(preset.actions[0]!.scopeKind).toBe("document");
      expect(preset.actions[0]!.parameters).toEqual(id === actions.restock ? { quantity: "99" }
        : id === actions.ap ? { type: String(binary.materiaType), ap: "1" }
          : id === actions.stars ? { type: String(binary.materiaType), stars: "2" }
            : id === actions.level ? { level: "25" } : {});
      expect(preset.compatibility.gameBuild ?? null).toBeNull();
      expect(preset.compatibility.referencePackIdentity).toBeTruthy();
      const after = await snapshot(page.request, documentPath);
      expect(after.state).toEqual(before.state);
      expectFiles(after.files, before.files);
    }
    const row = saved(page, `FF7 ${width} ${actions.restock}`);
    await row.getByText("Compatibility boundaries", { exact: true }).click();
    await expect(row).toContainText("build unknown");
    for (const boundary of ["Titles", "Plugin", "Build", "Variant", "Reference pack", "Schema", "Role", "Capability schema"])
      await expect(row.getByText(boundary, { exact: true })).toBeVisible();
    await panel.getByLabel("Filter presets", { exact: true }).fill(`FF7 ${width} ${actions.restock}`);
    await panel.getByLabel("Title", { exact: true }).fill(binary.titleId);
    await panel.getByLabel("Plugin", { exact: true }).fill("ff7r");
    await panel.getByRole("combobox", { name: "Compatibility", exact: true }).selectOption("ready");
    await expect(panel.getByRole("list", { name: "Saved toolkit presets" }).getByRole("listitem")).toHaveCount(1);
    await tabTo(page, row.getByRole("button", { name: "Rename", exact: true }));
    await page.keyboard.press("Enter");
    const longName = `FF7 ${width} ${"authored-long-name ".repeat(4)}`;
    const nameInput = panel.getByRole("textbox", { name: "Preset name", exact: true });
    await nameInput.fill(longName);
    await nameInput.press("Enter");
    await panel.getByLabel("Filter presets", { exact: true }).fill(longName);
    const renamed = saved(page, longName);
    await expect(renamed).toBeVisible();
    await contained(page, panel, width);
    await contained(page, renamed, width);
    await screenshot(page, testInfo, `ff7-forms-${width}`);
    const beforeDelete = await snapshot(page.request, documentPath);
    await renamed.getByRole("button", { name: "Delete", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: `Delete “${longName}”?` });
    await expect(dialog.getByRole("button", { name: "Cancel", exact: true })).toBeFocused();
    await contained(page, dialog, width);
    await page.keyboard.press("Escape");
    await expect(dialog).not.toBeVisible();
    await unchanged(page.request, documentPath, beforeDelete);
    await renamed.getByRole("button", { name: "Delete", exact: true }).click();
    await dialog.getByRole("button", { name: "Delete preset", exact: true }).click();
    await expect(renamed).toHaveCount(0);
    await flush();
  });
}
