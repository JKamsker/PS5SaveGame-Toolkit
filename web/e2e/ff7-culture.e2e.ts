import { test } from "./preset-library-test";
import { expect, type Worker } from "@playwright/test";
import { fixture, generateFf7Fixtures, openFf7 } from "./ff7-helpers";

// The .NET runtime takes its default culture from the browser language. A German browser must
// still see the Api's culture-independent text: FF7's "Bytes" stat is ToString("N0").
// Headless Chromium applies the context locale to Intl but keeps navigator.language "en-US" inside
// workers, so the runtime worker's script is prefixed to report the German language like a real browser.
const locale = "de-DE";
const languagePrelude = `for (const [key, value] of [["language", ${JSON.stringify(locale)}], ["languages", [${JSON.stringify(locale)}]]])
  Object.defineProperty(self.navigator, key, { get: () => value });
`;
let directory: string;
test.use({ locale });
test.describe.configure({ timeout: 60_000 });
test.beforeAll(async ({ request }, testInfo) => { expect(request).toBeDefined(); directory = await generateFf7Fixtures(testInfo); });

test("a German browser gets invariant number formatting from the runtime", async ({ page }) => {
  const workers: Worker[] = [];
  page.on("worker", worker => workers.push(worker));
  await page.route(/\/assets\/worker-[^/]*\.js$/, async route => {
    const response = await route.fetch();
    await route.fulfill({ response, body: languagePrelude + await response.text() });
  });
  const remake = await fixture(directory, "remake");
  await openFf7(page, remake);

  // Precondition: the runtime's worker really runs with the German browser language, so this
  // test cannot pass merely because the browser fell back to English.
  const runtime = await workerLanguages(workers);
  expect(runtime).toEqual({ language: locale, grouped: "1.056" });

  const bytes = remake.files[remake.gameplayPath]?.length ?? 0;
  expect(bytes).toBeGreaterThan(999);
  const invariant = new Intl.NumberFormat("en-US", { useGrouping: true }).format(bytes);
  const stat = page.locator(".stats__item").filter({ has: page.locator(".stats__label", { hasText: /^Bytes$/ }) });
  await expect(stat.locator(".stats__value")).toHaveText(invariant);
});

async function workerLanguages(workers: Worker[]): Promise<{ language: string; grouped: string }> {
  for (const worker of workers) {
    const probe = await worker.evaluate(() => ({
      runtime: (globalThis as { name?: string }).name === "savetoolkit-runtime",
      language: navigator.language,
      grouped: new Intl.NumberFormat().format(1056),
    })).catch(() => null);
    if (probe?.runtime) return { language: probe.language, grouped: probe.grouped };
  }
  throw new Error(`No dedicated runtime worker was observed (${workers.length} workers).`);
}
