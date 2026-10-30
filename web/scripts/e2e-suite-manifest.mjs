import { readFileSync, readdirSync, statSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const manifestPath = fileURLToPath(new URL("./e2e-suites.json", import.meta.url));

export function readSuiteManifest() {
  const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
  if (manifest.version !== 1 || manifest.testDirectory !== "e2e" || !Array.isArray(manifest.suites))
    throw new Error("E2E suite manifest must declare version 1 and the e2e test directory.");

  const ids = new Set();
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
    if (suite.role === "complement" && (suite.patterns.length || suite.files.length))
      throw new Error("The canonical complement suite cannot declare feature patterns or files.");
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

export function validateFeatureInventory(manifest, testDirectory) {
  const files = testFiles(testDirectory);
  return featureSuites(manifest).map(suite => {
    const declared = [...suite.files].sort();
    const matched = filesForSuite(suite, files);
    const missing = declared.filter(file => !files.includes(file));
    const undeclared = matched.filter(file => !declared.includes(file));
    if (missing.length && matched.length)
      throw new Error(`E2E suite '${suite.id}' has an incomplete feature-file inventory: missing ${missing.join(", ")}.`);
    if (undeclared.length)
      throw new Error(`E2E suite '${suite.id}' has undeclared matching feature files: ${undeclared.join(", ")}.`);
    if (matched.length !== declared.length)
      return { suite, state: "absent", declared, matched };
    return { suite, state: "present", declared, matched };
  });
}
