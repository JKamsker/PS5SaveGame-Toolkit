// @vitest-environment node
import { expect, it } from "vitest";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { fileURLToPath } from "node:url";
import path from "node:path";
import {
  allFeaturePatterns,
  complementSuite,
  featureSuites,
  filesForSuite,
  readSuiteManifest,
  validateFeatureInventory,
} from "./e2e-suite-manifest.mjs";

const webRoot = fileURLToPath(new URL("..", import.meta.url));

it("records the published feature inventories and one canonical complement", () => {
  const manifest = readSuiteManifest();
  expect(complementSuite(manifest).id).toBe("canonical");
  expect(featureSuites(manifest).map(suite => suite.id)).toEqual(["unreal", "text-capacity", "ff7"]);
  expect(featureSuites(manifest).map(suite => suite.patterns)).toEqual([
    ["**/unreal-*.e2e.ts"],
    ["**/text-capacity.e2e.ts"],
    ["**/ff7-*.e2e.ts"],
  ]);
  expect(allFeaturePatterns(manifest)).toEqual([
    "**/unreal-*.e2e.ts",
    "**/text-capacity.e2e.ts",
    "**/ff7-*.e2e.ts",
  ]);
});

it("distinguishes present, absent and incomplete feature inventories", () => {
  const inventory = validateFeatureInventory(readSuiteManifest(), path.join(webRoot, "e2e"));
  expect(inventory.find(entry => entry.suite.id === "unreal")).toMatchObject({ state: "present" });
  expect(inventory.find(entry => entry.suite.id === "text-capacity")).toMatchObject({ state: "absent" });
  expect(inventory.find(entry => entry.suite.id === "ff7")).toMatchObject({ state: "absent" });
});

it("matches only declared feature files, allowing synthetic ownership proofs", () => {
  const suite = { patterns: ["**/text-capacity*.e2e.ts"] };
  expect(filesForSuite(suite, ["text-capacity.e2e.ts", "nested/text-capacity-extra.e2e.ts", "smoke.e2e.ts"]))
    .toEqual(["nested/text-capacity-extra.e2e.ts", "text-capacity.e2e.ts"]);
});

it("rejects a partially installed declared feature inventory", () => {
  const directory = mkdtempSync(path.join(tmpdir(), "savetoolkit-e2e-inventory-"));
  try {
    writeFileSync(path.join(directory, "text-capacity.e2e.ts"), "// synthetic manifest proof\n");
    const manifest = { version: 1, testDirectory: "e2e", suites: [
      { id: "canonical", role: "complement", patterns: [], files: [] },
      { id: "capacity", role: "feature", patterns: ["**/text-capacity*.e2e.ts"],
        files: ["text-capacity.e2e.ts", "text-capacity-extra.e2e.ts"] },
    ] };
    expect(() => validateFeatureInventory(manifest, directory)).toThrow(/incomplete feature-file inventory/);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});
