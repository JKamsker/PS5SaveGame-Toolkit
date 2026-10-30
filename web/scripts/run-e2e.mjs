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

export function identities(report) {
  if (!report || !Array.isArray(report.suites)) throw new Error("E2E discovery returned malformed JSON.");
  const result = [];
  function visit(suites) {
    for (const suite of suites ?? []) {
      if (!Array.isArray(suite.specs) && suite.specs !== undefined)
        throw new Error("E2E discovery returned a malformed suite.");
      for (const spec of suite.specs ?? []) {
        if (typeof spec.id !== "string" || !Array.isArray(spec.tests))
          throw new Error("E2E discovery returned a malformed test specification.");
        for (const test of spec.tests) {
          if (!test || typeof test.projectName !== "string")
            throw new Error("E2E discovery returned a malformed test identity.");
          result.push(`${spec.id}:${test.projectName}`);
        }
      }
      visit(suite.suites);
    }
  }
  visit(report.suites);
  return result.sort();
}

function normalizeIds(label, ids) {
  if (!Array.isArray(ids) || ids.some(id => typeof id !== "string" || !id.length))
    throw new Error(`E2E ${label} discovery returned malformed IDs.`);
  return [...ids].sort();
}

export function assertPartition(name, all, parts) {
  const complete = normalizeIds(name, all);
  if (!complete.length) throw new Error(`E2E ${name} selection is empty.`);
  const combined = parts.flatMap(part => normalizeIds(`${name} suite '${part.id}'`, part.ids)).sort();
  if (new Set(combined).size !== combined.length || JSON.stringify(complete) !== JSON.stringify(combined))
    throw new Error(`E2E ${name} suites must partition every selected test exactly once.`);
}

export function suiteRecord(inventory, availableIDs, selectedIDs) {
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

export function composeDiscovery({ manifest, inventories, args: discoveryArgs, discover }) {
  const complement = complementSuite(manifest);
  const features = featureSuites(manifest);
  const inventoryById = new Map(inventories.map(inventory => [inventory.suite.id, inventory]));
  const suiteIds = [complement.id, ...features.map(feature => feature.id)];
  const availableAll = normalizeIds("available", discover("", []));
  const selectedAll = normalizeIds("filtered", discover("", discoveryArgs));
  const available = new Map([[complement.id, normalizeIds(complement.id, discover(complement.id, []))]]);
  const selected = new Map([[complement.id, normalizeIds(complement.id, discover(complement.id, discoveryArgs))]]);

  for (const feature of features) {
    const inventory = inventoryById.get(feature.id);
    if (!inventory) throw new Error(`E2E suite '${feature.id}' has no validated inventory record.`);
    if (inventory.state === "absent") {
      available.set(feature.id, []);
      selected.set(feature.id, []);
      continue;
    }
    const availableIDs = normalizeIds(feature.id, discover(feature.id, []));
    if (!availableIDs.length) throw new Error(`E2E suite '${feature.id}' is present but has no discoverable tests.`);
    available.set(feature.id, availableIDs);
    selected.set(feature.id, normalizeIds(feature.id, discover(feature.id, discoveryArgs)));
  }

  const availableParts = suiteIds.map(id => ({ id, ids: available.get(id) ?? [] }));
  const selectedParts = suiteIds.map(id => ({ id, ids: selected.get(id) ?? [] }));
  assertPartition("available", availableAll, availableParts);
  assertPartition("filtered", selectedAll, selectedParts);

  const records = Object.fromEntries(suiteIds.map(id => [id,
    suiteRecord(inventoryById.get(id), available.get(id) ?? [], selected.get(id) ?? [])]));
  return {
    discovery: {
      manifestVersion: manifest.version,
      featurePatterns: allFeaturePatterns(manifest),
      all: {
        availableIDs: availableAll,
        filteredSelectedIDs: selectedAll,
        availableCount: availableAll.length,
        filteredSelectedCount: selectedAll.length,
      },
      suites: records,
    },
    selectedSuites: selectedParts.filter(part => part.ids.length),
  };
}

export function orchestrateSuites({ manifest, inventories, args: discoveryArgs, discover, launch, reportError,
  beforeExecute, execute = true }) {
  const result = composeDiscovery({ manifest, inventories, args: discoveryArgs, discover });
  beforeExecute?.(result);
  if (!execute) return { ...result, exitCode: 0 };
  const exitCode = runSelectedSuites(result.selectedSuites.map(part => [part.id, part.ids]), launch, reportError);
  return { ...result, exitCode };
}

function environment(invocation, baseScratch, suite, discovery = false) {
  const root = path.join(invocation, suite || "all");
  const env = { ...process.env, SAVETOOLKIT_E2E_SCRATCH: root,
    SAVETOOLKIT_E2E_SUITE: suite || undefined, PLAYWRIGHT_HTML_OUTPUT_DIR: path.join(root, "playwright-report"),
    PLAYWRIGHT_JUNIT_OUTPUT_FILE: path.join(root, "e2e.xml") };
  // Each actual invocation allocates its own free port and temporary server directories.
  delete env.SAVETOOLKIT_E2E_URL;
  delete env.SAVETOOLKIT_E2E_RUN_DIRECTORY;
  if (process.platform === "win32") env.PLAYWRIGHT_BROWSERS_PATH ??= path.join(baseScratch, "browsers");
  if (discovery) env.SAVETOOLKIT_E2E_RUN_DIRECTORY = path.join(root, "discovery-run");
  return env;
}

function discover(suite, discoveryArgs, invocation, baseScratch) {
  const result = spawnSync(process.execPath,
    [cli, "test", ...discoveryArgs, "--list", "--pass-with-no-tests", "--reporter=json"],
    { env: environment(invocation, baseScratch, suite, true), encoding: "utf8", maxBuffer: 16 * 1024 * 1024 });
  if (result.error || result.status !== 0) throw new Error(result.error?.message || result.stderr || result.stdout);
  return identities(JSON.parse(result.stdout));
}

export function run() {
  mkdirSync(scratch, { recursive: true });
  const invocation = mkdtempSync(path.join(scratch, "invocation-"));
  try {
    const manifest = readSuiteManifest();
    const inventories = validateFeatureInventory(manifest, testDirectory);
    const result = orchestrateSuites({
      manifest,
      inventories,
      args,
      discover: (suite, discoveryArgs) => discover(suite, discoveryArgs, invocation, scratch),
      launch: suite => spawnSync(process.execPath, [cli, "test", ...args],
        { env: environment(invocation, scratch, suite), stdio: "inherit" }),
      reportError: message => console.error(message),
      beforeExecute: result => {
        writeFileSync(path.join(invocation, "discovery.json"), JSON.stringify(result.discovery, null, 2));
        console.log(`E2E discovery: ${result.discovery.all.filteredSelectedCount} selected across ${result.selectedSuites.length} suites. Reports: ${invocation}`);
      },
      execute: !args.includes("--list"),
    });
    process.exitCode = result.exitCode;
  } catch (error) {
    console.error(`E2E failed: ${error instanceof Error ? error.message : String(error)}`);
    process.exitCode = 1;
  }
}

const invokedScript = process.argv[1] ? path.normalize(process.argv[1]) : "";
if (path.basename(invokedScript) === "run-e2e.mjs" && path.basename(path.dirname(invokedScript)) === "scripts") run();
