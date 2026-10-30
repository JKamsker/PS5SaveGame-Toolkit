import { spawnSync } from "node:child_process";
import { mkdirSync, mkdtempSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { runSelectedSuites } from "./e2e-suite-runner.mjs";
import {
  allFeaturePatterns,
  complementSuite,
  featureSuites,
  readSuiteManifest,
  validateFeatureInventory,
} from "./e2e-suite-manifest.mjs";

const cli = createRequire(import.meta.url).resolve("@playwright/test/cli");
const args = process.argv.slice(2);
const webRoot = fileURLToPath(new URL("..", import.meta.url));
const testDirectory = path.join(webRoot, "e2e");
const scratch = path.resolve(process.env.SAVETOOLKIT_E2E_SCRATCH ?? (process.platform === "win32"
  ? "D:/Temp/saveeditor/test-scratch/e2e-smoke"
  : path.join(tmpdir(), "saveeditor/test-scratch/e2e-smoke")));
mkdirSync(scratch, { recursive: true });
const invocation = mkdtempSync(path.join(scratch, "invocation-"));

function environment(suite, discovery = false) {
  const root = path.join(invocation, suite || "all");
  const env = { ...process.env, SAVETOOLKIT_E2E_SCRATCH: root,
    SAVETOOLKIT_E2E_SUITE: suite || undefined, PLAYWRIGHT_HTML_OUTPUT_DIR: path.join(root, "playwright-report"),
    PLAYWRIGHT_JUNIT_OUTPUT_FILE: path.join(root, "e2e.xml") };
  // Each actual invocation allocates its own free port and temporary server directories.
  delete env.SAVETOOLKIT_E2E_URL;
  delete env.SAVETOOLKIT_E2E_RUN_DIRECTORY;
  if (process.platform === "win32") env.PLAYWRIGHT_BROWSERS_PATH ??= path.join(scratch, "browsers");
  if (discovery) env.SAVETOOLKIT_E2E_RUN_DIRECTORY = path.join(root, "discovery-run");
  return env;
}

function identities(report) {
  const result = [];
  function visit(suites) {
    for (const suite of suites ?? []) {
      for (const spec of suite.specs ?? [])
        for (const test of spec.tests ?? []) result.push(`${spec.id}:${test.projectName}`);
      visit(suite.suites);
    }
  }
  visit(report.suites);
  return result.sort();
}

function discover(suite, discoveryArgs) {
  const result = spawnSync(process.execPath,
    [cli, "test", ...discoveryArgs, "--list", "--pass-with-no-tests", "--reporter=json"],
    { env: environment(suite, true), encoding: "utf8", maxBuffer: 16 * 1024 * 1024 });
  if (result.error || result.status !== 0) throw new Error(result.error?.message || result.stderr || result.stdout);
  return identities(JSON.parse(result.stdout));
}

function assertPartition(name, all, parts) {
  if (!all.length) throw new Error(`E2E ${name} selection is empty.`);
  const combined = parts.flatMap(part => part.ids).sort();
  if (new Set(combined).size !== combined.length || JSON.stringify(all) !== JSON.stringify(combined))
    throw new Error(`E2E ${name} suites must partition every selected test exactly once.`);
}

function suiteRecord(inventory, availableIDs, selectedIDs) {
  const state = inventory?.state === "absent"
    ? "absent"
    : selectedIDs.length ? "selected" : "filtered-out";
  return {
    state,
    declaredFiles: inventory?.declared ?? [],
    matchedFiles: inventory?.matched ?? [],
    availableIDs,
    selectedIDs,
    availableCount: availableIDs.length,
    selectedCount: selectedIDs.length,
  };
}

try {
  const manifest = readSuiteManifest();
  const inventories = validateFeatureInventory(manifest, testDirectory);
  const complement = complementSuite(manifest);
  const features = featureSuites(manifest);
  const inventoryById = new Map(inventories.map(inventory => [inventory.suite.id, inventory]));
  const suiteIds = [complement.id, ...features.map(feature => feature.id)];
  const availableAll = discover("", []);
  const selectedAll = discover("", args);
  const available = new Map([[complement.id, discover(complement.id, [])]]);
  const selected = new Map([[complement.id, discover(complement.id, args)]]);

  for (const feature of features) {
    const inventory = inventoryById.get(feature.id);
    if (inventory.state === "absent") {
      available.set(feature.id, []);
      selected.set(feature.id, []);
      continue;
    }
    const availableIDs = discover(feature.id, []);
    if (!availableIDs.length) throw new Error(`E2E suite '${feature.id}' is present but has no discoverable tests.`);
    available.set(feature.id, availableIDs);
    selected.set(feature.id, discover(feature.id, args));
  }

  const availableParts = suiteIds.map(id => ({ id, ids: available.get(id) ?? [] }));
  const selectedParts = suiteIds.map(id => ({ id, ids: selected.get(id) ?? [] }));
  assertPartition("available", availableAll, availableParts);
  assertPartition("filtered", selectedAll, selectedParts);

  const records = Object.fromEntries(suiteIds.map(id => [id,
    suiteRecord(inventoryById.get(id), available.get(id) ?? [], selected.get(id) ?? [])]));
  const discovery = {
    manifestVersion: manifest.version,
    featurePatterns: allFeaturePatterns(manifest),
    all: {
      availableIDs: availableAll,
      filteredSelectedIDs: selectedAll,
      availableCount: availableAll.length,
      filteredSelectedCount: selectedAll.length,
    },
    suites: records,
  };
  writeFileSync(path.join(invocation, "discovery.json"), JSON.stringify(discovery, null, 2));
  const selectedSuites = selectedParts.filter(part => part.ids.length);
  console.log(`E2E discovery: ${selectedAll.length} selected across ${selectedSuites.length} suites. Reports: ${invocation}`);
  if (!args.includes("--list")) {
    process.exitCode = runSelectedSuites(selectedSuites.map(part => [part.id, part.ids]),
      suite => spawnSync(process.execPath, [cli, "test", ...args], { env: environment(suite), stdio: "inherit" }),
      message => console.error(message));
  }
} catch (error) {
  console.error(`E2E failed: ${error.message}`);
  process.exitCode = 1;
}
