import { test, expect } from "@playwright/test";
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { unzipSync } from "fflate";
import { archive, home, upload } from "./helpers";

// Optional real-sample regression: samples stay external and read-only. Only the
// selected entry is uploaded from memory; no extracted save is written by this test.
test("Rebirth coalesces reference labels across four pages and action parameters (#40)", async ({ page }) => {
  test.skip(!process.env.SAVETOOLKIT_ARTIFACTS, "Requires external Rebirth sample.");
  test.setTimeout(120_000);
  const entry = "PPSA08664/sdimg_ff7rebirth/ff7rebirth000";
  const samplePath = path.join(process.env.SAVETOOLKIT_ARTIFACTS!, "PPSA08664_saves_dec.zip");
  test.skip(!existsSync(samplePath), "Rebirth archive is not available.");
  const original = readFileSync(samplePath);
  const selected = unzipSync(original, { filter: file => file.name === entry });
  expect(Object.keys(selected)).toEqual([entry]);
  const counts: { single: number; batch: number; pairs: number }[] = [];
  let phase = { single: 0, batch: 0, pairs: 0 };
  const pending = new Set<unknown>();
  const referenceFailures: number[] = [];
  let lastRequest = Date.now();
  page.on("request", request => {
    if (!new URL(request.url()).pathname.startsWith("/api/")) return;
    pending.add(request);
    lastRequest = Date.now();
    if (request.url().includes("/reference-fields/record?")) phase.single++;
    if (request.url().endsWith("/reference-fields/records")) {
      phase.batch++;
      phase.pairs += request.postDataJSON().pairs.length;
    }
  });
  page.on("requestfinished", request => pending.delete(request));
  page.on("requestfailed", request => pending.delete(request));
  page.on("response", response => {
    if (response.url().includes("/reference-fields/") && response.status() >= 400) referenceFailures.push(response.status());
  });
  const settle = async () => {
    await expect.poll(() => pending.size === 0 && Date.now() - lastRequest >= 300, { timeout: 60_000 }).toBe(true);
  };
  await home(page);
  await upload(page, archive(selected), "rebirth.zip");
  const rail = page.getByRole("navigation", { name: "Save directories" });
  await rail.getByRole("button", { name: /ff7rebirth Gameplay/ }).first().click();
  await rail.getByRole("button", { name: /^ff7rebirth000 / }).click();
  await expect(page.getByRole("tab", { name: "Inventory", exact: true })).toBeVisible();
  await settle();
  const finishPhase = async () => { await settle(); counts.push(phase); phase = { single: 0, batch: 0, pairs: 0 }; };
  await page.getByRole("tab", { name: "Inventory", exact: true }).click();
  await expect(page.getByRole("table", { name: "Inventory", exact: true })).toHaveAttribute("aria-busy", "false");
  await finishPhase();
  await expect(page.locator(".reference-trigger").first()).not.toHaveAttribute("title", /^Unknown ID/);
  await page.getByRole("button", { name: "Next", exact: true }).click();
  await expect(page.getByText("Page 2", { exact: true })).toBeVisible();
  await finishPhase();
  await page.getByRole("searchbox", { name: "Search Inventory" }).fill("potion");
  await expect(page.getByText("10 of", { exact: false })).toBeVisible();
  await finishPhase();
  await page.getByRole("tab", { name: "Materia", exact: true }).click();
  await expect(page.getByRole("table", { name: "Materia", exact: true })).toHaveAttribute("aria-busy", "false");
  await finishPhase();
  console.log(JSON.stringify({ referenceLookupCounts: counts }));
  expect(referenceFailures).toEqual([]);
  // The baseline switch is for measuring the unchanged client before implementation.
  if (process.env.SAVETOOLKIT_REFERENCE_BASELINE === "1") {
    expect(counts.map(count => count.single)).toEqual([50, 50, 10, 52]);
  } else {
    expect(counts.map(count => count.single)).toEqual([0, 0, 0, 0]);
    expect(counts.map(count => count.batch)).toEqual([1, 1, 1, 1]);
    expect(counts.map(count => count.pairs)).toEqual([50, 50, 10, 52]);
  }
  await page.getByRole("tab", { name: "Overview", exact: true }).click();
  page.once("dialog", dialog => dialog.accept());
  await page.getByRole("button", { name: "Close dump", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Open a PS5 save dump" })).toBeVisible();
});
