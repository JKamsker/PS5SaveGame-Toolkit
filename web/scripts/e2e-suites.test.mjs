// @vitest-environment node
import { expect, it, vi } from "vitest";
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  allFeaturePatterns,
  complementSuite,
  featureSuites,
  filesForSuite,
  readSuiteManifest,
  validateFeatureInventory,
} from "./e2e-suite-manifest.mjs";
import { composeDiscovery, identities, orchestrateSuites } from "./run-e2e.mjs";

const webRoot = fileURLToPath(new URL("..", import.meta.url));

function fixtureManifest() {
  return { version: 2, testDirectory: "e2e", suites: [
    { id: "canonical", role: "complement", patterns: [], files: ["canonical.e2e.ts"] },
    { id: "capacity", role: "feature", patterns: ["**/text-capacity.e2e.ts"], files: ["text-capacity.e2e.ts"] },
    { id: "ff7", role: "feature", patterns: ["**/ff7-*.e2e.ts"], files: [
      "ff7-forms.e2e.ts", "ff7-history.e2e.ts", "ff7-selection.e2e.ts", "ff7-states.e2e.ts",
    ] },
  ] };
}

function inventoryFor(manifest, states = new Map()) {
  return featureSuites(manifest).map(suite => ({ suite, state: states.get(suite.id) ?? "present",
    declared: [...suite.files], matched: [...suite.files] }));
}

function writeFixtureFiles(directory, files) {
  for (const file of files) {
    const fullPath = path.join(directory, file);
    writeFileSync(fullPath, "// synthetic manifest proof\n");
  }
}

function allFixtureFiles() {
  return fixtureManifest().suites.flatMap(suite => suite.files);
}

it("records published ownership without requiring future feature files to be absent", () => {
  const manifest = readSuiteManifest();
  expect(complementSuite(manifest).id).toBe("canonical");
  expect(complementSuite(manifest).files).toEqual([
    "actions.e2e.ts", "bundle.e2e.ts", "failure-document.e2e.ts", "failure-lifetimes.e2e.ts",
    "failure-reference.e2e.ts", "failure-review-export.e2e.ts", "field-reference.e2e.ts", "import.e2e.ts",
    "keyboard.e2e.ts", "map-accessibility.e2e.ts", "reference-batch.e2e.ts", "review.e2e.ts",
    "selection-binding.e2e.ts", "smoke.e2e.ts", "table.e2e.ts", "workspace.e2e.ts",
  ]);
  expect(featureSuites(manifest).map(suite => suite.id)).toEqual([
    "unreal", "text-capacity", "ff7", "transfer", "caption", "action-effects",
  ]);
  expect(featureSuites(manifest).map(suite => suite.patterns)).toEqual([
    ["**/unreal-*.e2e.ts"],
    ["**/text-capacity.e2e.ts"],
    ["**/ff7-*.e2e.ts"],
    ["**/preset-transfer.e2e.ts", "**/preset-transfer-recovery.e2e.ts", "**/preset-transfer-library.e2e.ts"],
    ["**/suckerpunch-caption-real.e2e.ts", "**/suckerpunch-caption-text-boundaries.e2e.ts"],
    ["**/action-effects.e2e.ts"],
  ]);
  expect(allFeaturePatterns(manifest)).toEqual([
    "**/unreal-*.e2e.ts",
    "**/text-capacity.e2e.ts",
    "**/ff7-*.e2e.ts",
    "**/preset-transfer.e2e.ts",
    "**/preset-transfer-recovery.e2e.ts",
    "**/preset-transfer-library.e2e.ts",
    "**/suckerpunch-caption-real.e2e.ts",
    "**/suckerpunch-caption-text-boundaries.e2e.ts",
    "**/action-effects.e2e.ts",
  ]);

  const inventory = validateFeatureInventory(manifest, path.join(webRoot, "e2e"));
  expect(inventory.map(entry => entry.suite.id)).toEqual([
    "canonical", "unreal", "text-capacity", "ff7", "transfer", "caption", "action-effects",
  ]);
  expect(inventory[0]).toMatchObject({ state: "present", declared: complementSuite(manifest).files });
  for (const entry of inventory.slice(1)) {
    expect(["absent", "present"]).toContain(entry.state);
    if (entry.state === "present") expect(entry.matched).toEqual(entry.declared);
    else expect(entry.matched).toEqual([]);
  }
  expect(inventory.find(entry => entry.suite.id === "unreal")).toMatchObject({ state: "present" });
});

it("proves absent, complete capacity/FF7 and partial inventories in synthetic directories", () => {
  const manifest = fixtureManifest();
  const root = mkdtempSync(path.join(tmpdir(), "savetoolkit-e2e-inventory-"));
  try {
    writeFixtureFiles(root, ["canonical.e2e.ts"]);
    expect(validateFeatureInventory(manifest, root).map(entry => entry.state)).toEqual(["present", "absent", "absent"]);

    writeFixtureFiles(root, allFixtureFiles());
    expect(validateFeatureInventory(manifest, root).map(entry => entry.state)).toEqual(["present", "present", "present"]);

    rmSync(path.join(root, "ff7-history.e2e.ts"));
    expect(() => validateFeatureInventory(manifest, root)).toThrow(/incomplete feature-file inventory/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

it("translates **/ as zero-or-more whole directory components", () => {
  expect(filesForSuite({ patterns: ["**/text-capacity.e2e.ts"] }, [
    "text-capacity.e2e.ts", "nested/text-capacity.e2e.ts",
    "other-text-capacity.e2e.ts", "nested/other-text-capacity.e2e.ts",
  ])).toEqual(["nested/text-capacity.e2e.ts", "text-capacity.e2e.ts"]);
  expect(filesForSuite({ patterns: ["**/unreal-*.e2e.ts"] }, [
    "unreal-reference.e2e.ts", "nested/unreal-reference.e2e.ts",
    "not-unreal-reference.e2e.ts", "nested/not-unreal-reference.e2e.ts",
  ])).toEqual(["nested/unreal-reference.e2e.ts", "unreal-reference.e2e.ts"]);
  expect(() => filesForSuite({ patterns: ["e2e/**/unreal-*.e2e.ts"] }, [])).toThrow(/expected a \*\*\//);
});

it("rejects an actual undeclared feature file without prefix false positives", () => {
  const manifest = fixtureManifest();
  const root = mkdtempSync(path.join(tmpdir(), "savetoolkit-e2e-ownership-"));
  try {
    writeFixtureFiles(root, ["ff7-forms.e2e.ts", "ff7-history.e2e.ts", "ff7-selection.e2e.ts",
      "ff7-states.e2e.ts", "ff7-extra.e2e.ts"]);
    expect(() => validateFeatureInventory(manifest, root)).toThrow(/undeclared \.e2e\.ts files/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

it("proves the published action-effects partition is explicit when present", () => {
  const manifest = readSuiteManifest();
  const canonical = complementSuite(manifest);
  const actionEffects = featureSuites(manifest).find(suite => suite.id === "action-effects");
  const root = mkdtempSync(path.join(tmpdir(), "savetoolkit-e2e-action-effects-"));
  try {
    writeFixtureFiles(root, canonical.files);
    expect(validateFeatureInventory(manifest, root).find(entry => entry.suite.id === actionEffects.id))
      .toMatchObject({ state: "absent", declared: ["action-effects.e2e.ts"], matched: [] });

    writeFixtureFiles(root, actionEffects.files);
    expect(validateFeatureInventory(manifest, root).find(entry => entry.suite.id === actionEffects.id))
      .toMatchObject({ state: "present", declared: actionEffects.files, matched: actionEffects.files });
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

it.each([
  ["unknown file", root => writeFixtureFiles(root, ["canonical.e2e.ts", "new.e2e.ts"]), /undeclared \.e2e\.ts files/],
  ["missing canonical allowlist file", root => {}, /canonical file allowlist is missing/],
  ["overlapping pattern/file", root => writeFixtureFiles(root, ["canonical.e2e.ts", "text-capacity.e2e.ts"]), /overlapping or mismatched/],
])("rejects schema2 %s before discovery", (_name, setup, message) => {
  const manifest = fixtureManifest();
  if (_name === "overlapping pattern/file") manifest.suites[2].patterns = ["**/text-capacity.e2e.ts"];
  const root = mkdtempSync(path.join(tmpdir(), "savetoolkit-e2e-schema2-"));
  try {
    setup(root);
    expect(() => validateFeatureInventory(manifest, root)).toThrow(message);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

it("rejects duplicate declared ownership before discovery", () => {
  const manifest = fixtureManifest();
  manifest.suites[1].files[0] = "canonical.e2e.ts";
  const root = mkdtempSync(path.join(tmpdir(), "savetoolkit-e2e-overlap-"));
  try {
    writeFixtureFiles(root, ["canonical.e2e.ts"]);
    expect(() => validateFeatureInventory(manifest, root)).toThrow(/suite files overlap/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

it("rejects unsupported ownership patterns before discovery", () => {
  const manifest = fixtureManifest();
  manifest.suites[1].patterns = ["e2e/**/unreal-*.e2e.ts"];
  const root = mkdtempSync(path.join(tmpdir(), "savetoolkit-e2e-pattern-"));
  try {
    writeFixtureFiles(root, ["canonical.e2e.ts"]);
    expect(() => validateFeatureInventory(manifest, root)).toThrow(/expected a \*\*\//);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

function discoveryFixture() {
  const manifest = fixtureManifest();
  const inventories = inventoryFor(manifest);
  const ids = { canonical: ["synthetic-canonical"], capacity: ["synthetic-capacity"], ff7: ["synthetic-ff7"] };
  const discover = vi.fn((suite, args) => {
    const filtered = args.length > 0;
    if (!suite) return filtered ? ids.capacity : Object.values(ids).flat();
    return filtered && suite !== "capacity" ? [] : ids[suite];
  });
  return { manifest, inventories, ids, discover };
}

it("rejects malformed discovery before any actual launch", () => {
  const { manifest, inventories } = discoveryFixture();
  const launch = vi.fn();
  expect(() => identities({ suites: [{ specs: [{ id: "synthetic", tests: [{}] }] }] }))
    .toThrow(/malformed test identity/);
  expect(() => orchestrateSuites({ manifest, inventories, args: [], discover: () => ({ malformed: true }),
    launch, reportError: vi.fn() })).toThrow(/malformed IDs/);
  expect(launch).not.toHaveBeenCalled();
});

it("rejects a present suite with zero unfiltered IDs before launch", () => {
  const { manifest, inventories } = discoveryFixture();
  const launch = vi.fn();
  const discover = (suite, args) => suite === "capacity" && args.length === 0 ? [] : suite
    ? ["synthetic-" + suite] : ["synthetic-canonical", "synthetic-ff7"];
  expect(() => orchestrateSuites({ manifest, inventories, args: [], discover, launch, reportError: vi.fn() }))
    .toThrow(/present but has no discoverable tests/);
  expect(launch).not.toHaveBeenCalled();
});

it("rejects globally filtered-empty selection before launch", () => {
  const { manifest, inventories } = discoveryFixture();
  const launch = vi.fn();
  const discover = (suite, args) => args.length ? [] : suite ? [suite] : ["canonical", "capacity", "ff7"];
  expect(() => orchestrateSuites({ manifest, inventories, args: ["--grep", "none"], discover, launch,
    reportError: vi.fn() })).toThrow(/filtered selection is empty/);
  expect(launch).not.toHaveBeenCalled();
});

it.each([
  ["missing", (ids) => ({ ...ids, global: [ids.canonical[0], ids.ff7[0]] })],
  ["overlapping", (ids) => ({ ...ids, ff7: ids.canonical })],
])("rejects %s ownership before launch", (_name, change) => {
  const { manifest, inventories, ids } = discoveryFixture();
  const changed = change(ids);
  const launch = vi.fn();
  const discover = (suite, args) => {
    if (args.length) return changed[suite] ?? [];
    return suite ? changed[suite] : changed.global ?? Object.values(changed).flat();
  };
  expect(() => orchestrateSuites({ manifest, inventories, args: [], discover, launch, reportError: vi.fn() }))
    .toThrow(/partition every selected test exactly once/);
  expect(launch).not.toHaveBeenCalled();
});

it("records absent versus intentionally filtered-out and launches only selected suites", () => {
  const manifest = fixtureManifest();
  const inventories = inventoryFor(manifest, new Map([["ff7", "absent"]]));
  const discover = (suite, args) => {
    if (suite === "ff7") return [];
    if (args.length) return suite ? (suite === "capacity" ? ["synthetic-capacity"] : [])
      : ["synthetic-capacity"];
    return suite ? [`synthetic-${suite}`] : ["synthetic-canonical", "synthetic-capacity"];
  };
  const launch = vi.fn().mockReturnValue({ status: 0 });
  const result = orchestrateSuites({ manifest, inventories, args: ["--grep", "capacity"], discover, launch,
    reportError: vi.fn() });
  expect(result.discovery.suites.canonical.state).toBe("filtered-out");
  expect(result.discovery.suites.capacity.state).toBe("selected");
  expect(result.discovery.suites.ff7.state).toBe("absent");
  expect(launch.mock.calls).toEqual([["capacity"]]);
});

it("keeps full availability while filtering canonical and feature partitions independently", () => {
  const { manifest, inventories } = discoveryFixture();
  const available = {
    canonical: ["synthetic-canonical-a", "synthetic-canonical-b"],
    capacity: ["synthetic-capacity"],
    ff7: ["synthetic-ff7"],
  };
  const selected = {
    canonical: ["synthetic-canonical-b"],
    capacity: ["synthetic-capacity"],
    ff7: [],
  };
  const discover = (suite, args) => {
    const values = args.length ? selected : available;
    return suite ? values[suite] : Object.values(values).flat();
  };
  const launch = vi.fn().mockReturnValue({ status: 0 });
  const result = orchestrateSuites({ manifest, inventories, args: ["--grep", "capacity-or-canonical"], discover, launch,
    reportError: vi.fn() });

  expect(result.discovery.all.availableIDs).toEqual([
    "synthetic-canonical-a", "synthetic-canonical-b", "synthetic-capacity", "synthetic-ff7",
  ]);
  expect(result.discovery.all.filteredSelectedIDs).toEqual(["synthetic-canonical-b", "synthetic-capacity"]);
  expect(result.discovery.suites.canonical.state).toBe("selected");
  expect(result.discovery.suites.capacity.state).toBe("selected");
  expect(result.discovery.suites.ff7.state).toBe("filtered-out");
  expect(launch.mock.calls).toEqual([["canonical"], ["capacity"]]);
});

it("launches every valid selected suite while retaining thrown, signal, nonzero and teardown failures", () => {
  const { manifest, inventories, ids, discover } = discoveryFixture();
  const directory = mkdtempSync(path.join(tmpdir(), "savetoolkit-e2e-discovery-"));
  const discoveryPath = path.join(directory, "discovery.json");
  const attempted = [];
  const observations = [];
  const readErrors = [];
  const branchResults = [];
  const reportError = vi.fn();
  const launch = vi.fn().mockImplementation(suite => {
    attempted.push(suite);
    const observation = { suite, fileExists: existsSync(discoveryPath) };
    try {
      observation.persisted = JSON.parse(readFileSync(discoveryPath, "utf8"));
    } catch (error) {
      readErrors.push({ suite, message: error instanceof Error ? error.message : String(error) });
    }
    observations.push(observation);
    if (suite === "canonical") {
      branchResults.push({ suite, branch: "thrown" });
      throw new Error("synthetic launch error");
    }
    if (suite === "capacity") {
      const signalResult = { status: null, signal: "SIGTERM" };
      branchResults.push({ suite, branch: "signal", result: signalResult });
      return signalResult;
    }
    if (suite === "ff7") {
      const nonzeroResult = { status: 1 };
      branchResults.push({ suite, branch: "nonzero", result: nonzeroResult });
      return nonzeroResult;
    }
    branchResults.push({ suite, branch: "success" });
    return { status: 0 };
  });
  try {
    const result = orchestrateSuites({ manifest, inventories, args: [], discover, launch, reportError,
      beforeExecute: result => writeFileSync(discoveryPath, JSON.stringify(result.discovery)) });
    expect(result.exitCode).toBe(1);
    expect(attempted).toEqual(["canonical", "capacity", "ff7"]);
    expect(ids).toEqual({ canonical: ["synthetic-canonical"], capacity: ["synthetic-capacity"], ff7: ["synthetic-ff7"] });
    expect(readErrors).toEqual([]);
    expect(observations).toHaveLength(3);
    expect(observations.every(observation => observation.fileExists)).toBe(true);
    expect(observations[0].persisted).toEqual(result.discovery);
    expect(observations[0].persisted.all).toMatchObject({
      availableIDs: ["synthetic-canonical", "synthetic-capacity", "synthetic-ff7"],
      filteredSelectedIDs: ["synthetic-canonical", "synthetic-capacity", "synthetic-ff7"],
      availableCount: 3,
      filteredSelectedCount: 3,
    });
    expect(observations[0].persisted.suites).toMatchObject({
      canonical: { state: "selected", availableIDs: ["synthetic-canonical"], selectedIDs: ["synthetic-canonical"] },
      capacity: { state: "selected", availableIDs: ["synthetic-capacity"], selectedIDs: ["synthetic-capacity"] },
      ff7: { state: "selected", availableIDs: ["synthetic-ff7"], selectedIDs: ["synthetic-ff7"] },
    });
    expect(branchResults).toEqual([
      { suite: "canonical", branch: "thrown" },
      { suite: "capacity", branch: "signal", result: { status: null, signal: "SIGTERM" } },
      { suite: "ff7", branch: "nonzero", result: { status: 1 } },
    ]);
    expect(reportError).toHaveBeenCalledWith("E2E suite 'canonical' could not launch: synthetic launch error");
    expect(reportError).toHaveBeenCalledTimes(1);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});

it("fails persistence before launching any valid suite", () => {
  const { manifest, inventories, discover } = discoveryFixture();
  const launch = vi.fn();
  expect(() => orchestrateSuites({ manifest, inventories, args: [], discover, launch, reportError: vi.fn(),
    beforeExecute: () => { throw new Error("synthetic discovery persistence failure"); } }))
    .toThrow(/synthetic discovery persistence failure/);
  expect(launch).not.toHaveBeenCalled();
});
