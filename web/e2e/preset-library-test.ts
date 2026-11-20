import { test as base } from "@playwright/test";
import { installApiHooks } from "./apiRoutes";

/**
 * Presets live in the runtime's IndexedDB-backed store, which every test's fresh browser context
 * isolates, so each case starts from an empty library and leaves nothing behind. The fixture installs
 * the API hooks before navigation so specs can inspect the library through the runtime.
 */
export const test = base.extend<{ presetLibrary: void }>({
  presetLibrary: [async ({ page }, use) => {
    await installApiHooks(page);
    await use();
  }, { auto: true }],
});
