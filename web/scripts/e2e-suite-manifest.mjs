import { readFileSync, readdirSync, statSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const manifestPath = fileURLToPath(new URL("./e2e-suites.json", import.meta.url));
const manifestVersion = 2;

function normalizedTestFile(file) {
  const normalized = file.replaceAll("\\", "/");
  const parts = normalized.split("/");
  if (!normalized || normalized.startsWith("/") || parts.some(part => !part || part === "..")
    || !normalized.endsWith(".e2e.ts"))
    throw new Error(`E2E suite manifest contains an invalid test file '${file}'.`);
  return normalized;
}

export function readSuiteManifest() {
  const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
  if (manifest.version !== manifestVersion || manifest.testDirectory !== "e2e" || !Array.isArray(manifest.suites))
    throw new Error(`E2E suite manifest must declare version ${manifestVersion} and the e2e test directory.`);

  const ids = new Set();
  const files = new Map();
  let complements = 0;
  for (const suite of manifest.suites) {
    if (!suite || typeof suite.id !== "string" || ids.has(suite.id))
      throw new Error("E2E suite manifest contains a missing or duplicate suite ID.");
    if (!Array.isArray(suite.patterns) || !suite.patterns.every(pattern => typeof pattern === "string")
      || !Array.isArray(suite.files) || !suite.files.every(file => typeof file === "string"))
      throw new Error(`E2E suite '${suite.id}' must declare patterns and files.`);
    if (suite.role !== "feature" && suite.role !== "complement")
      throw new Error(`E2E suite '${suite.id}' has an unknown role.`);
    if (suite.role === "feature" && (!suite.patterns.length || !suite.files.length))
      throw new Error(`E2E feature suite '${suite.id}' must declare patterns and feature files.`);
    if (suite.role === "complement" && (!suite.files.length || suite.patterns.length))
      throw new Error("The canonical complement suite must declare an explicit file allowlist and no patterns.");
    suite.patterns = suite.patterns.map(pattern => pattern.replaceAll("\\", "/"));
    suite.files = suite.files.map(normalizedTestFile);
    for (const pattern of suite.patterns) globRegex(pattern);
    for (const file of suite.files) {
      if (files.has(file)) throw new Error(`E2E suite files overlap between '${files.get(file)}' and '${suite.id}'.`);
      files.set(file, suite.id);
    }
    if (suite.role === "complement") complements++;
    ids.add(suite.id);
  }
  if (complements !== 1) throw new Error("E2E suite manifest must declare exactly one complement suite.");
  return manifest;
}

export function featureSuites(manifest) {
  return manifest.suites.filter(suite => suite.role === "feature");
}

export function complementSuite(manifest) {
  return manifest.suites.find(suite => suite.role === "complement");
}

export function allFeaturePatterns(manifest) {
  return featureSuites(manifest).flatMap(suite => suite.patterns);
}

export function testFiles(testDirectory) {
  const files = [];
  function visit(directory, relativeDirectory) {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const fullPath = path.join(directory, entry.name);
      const relativePath = path.posix.join(relativeDirectory, entry.name);
      if (entry.isDirectory()) visit(fullPath, relativePath);
      else if (entry.isFile() && entry.name.endsWith(".e2e.ts")) files.push(relativePath);
    }
  }
  if (statSync(testDirectory).isDirectory()) visit(testDirectory, "");
  return files.sort();
}

function globRegex(pattern) {
  const normalized = pattern.replaceAll("\\", "/");
  if (!normalized.startsWith("**/"))
    throw new Error(`Unsupported E2E ownership pattern '${pattern}': expected a **/ prefix.`);
  if (normalized.slice(3).includes("**"))
    throw new Error(`Unsupported E2E ownership pattern '${pattern}': ** is only supported as **/.`);

  let expression = "^(?:[^/]+/)*";
  for (let index = 3; index < normalized.length; index++) {
    const character = normalized[index];
    if (character === "*") expression += "[^/]*";
    else if (character === "?") expression += "[^/]";
    else expression += character.replace(/[\\^$+{}()[\].|]/g, "\\$&");
  }
  return new RegExp(`${expression}$`);
}

export function filesForSuite(suite, files) {
  const patterns = suite.patterns.map(globRegex);
  return files.filter(file => patterns.some(pattern => pattern.test(file))).sort();
}

function matchingFeatureSuites(manifest, file) {
  return featureSuites(manifest).filter(suite => filesForSuite(suite, [file]).length);
}

function validatePatternOwnership(manifest) {
  const declared = new Map();
  for (const suite of manifest.suites) {
    for (const file of suite.files) {
      if (declared.has(file)) throw new Error(`E2E suite files overlap between '${declared.get(file).id}' and '${suite.id}'.`);
      declared.set(file, suite);
    }
  }
  for (const [file, owner] of declared) {
    const matches = matchingFeatureSuites(manifest, file);
    if (owner.role === "feature" && (matches.length !== 1 || matches[0].id !== owner.id))
      throw new Error(`E2E suite file '${file}' has overlapping or mismatched feature patterns.`);
    if (owner.role === "complement" && matches.length)
      throw new Error(`E2E canonical file '${file}' is claimed by a feature pattern.`);
  }
  return declared;
}

export function validateFeatureInventory(manifest, testDirectory) {
  const ownership = validatePatternOwnership(manifest);
  const files = testFiles(testDirectory);
  const unknown = files.filter(file => !ownership.has(file));
  if (unknown.length) throw new Error(`E2E test inventory has undeclared .e2e.ts files: ${unknown.join(", ")}.`);

  return manifest.suites.map(suite => {
    const suiteFiles = [...suite.files].sort();
    if (suite.role === "complement") {
      const missing = suiteFiles.filter(file => !files.includes(file));
      if (missing.length) throw new Error(`E2E canonical file allowlist is missing: ${missing.join(", ")}.`);
      return { suite, state: "present", declared: suiteFiles, matched: suiteFiles };
    }

    const matched = filesForSuite(suite, files);
    const missing = suiteFiles.filter(file => !files.includes(file));
    if (!matched.length && missing.length === suiteFiles.length)
      return { suite, state: "absent", declared: suiteFiles, matched: [] };
    if (missing.length && matched.length)
      throw new Error(`E2E suite '${suite.id}' has an incomplete feature-file inventory: missing ${missing.join(", ")}.`);
    if (matched.length !== suiteFiles.length)
      throw new Error(`E2E suite '${suite.id}' has a pattern/file ownership mismatch.`);
    return { suite, state: matched.length ? "present" : "absent", declared: suiteFiles, matched };
  });
}
