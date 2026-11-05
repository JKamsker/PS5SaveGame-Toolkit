import { test as base } from "@playwright/test";
import { clear } from "./presetTransferFixtures";

/** Browser contexts do not isolate the host-wide preset store. Keep cases repeatable. */
export const test = base.extend<{ presetLibrary: void }>({
  presetLibrary: [async ({ request }, use) => {
    await clear(request);
    try {
      await use();
    } finally {
      await clear(request);
    }
  }, { auto: true }],
});
