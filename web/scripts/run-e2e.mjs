import { spawnSync } from "node:child_process";
import { mkdirSync, mkdtempSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { tmpdir } from "node:os";
import path from "node:path";

const cli = createRequire(import.meta.url).resolve("@playwright/test/cli");
const args = process.argv.slice(2);
const scratch = path.resolve(process.env.SAVETOOLKIT_E2E_SCRATCH ?? (process.platform === "win32"
  ? "D:/Temp/saveeditor/test-scratch/e2e-smoke"
  : path.join(tmpdir(), "saveeditor/test-scratch/e2e-smoke")));
mkdirSync(scratch, { recursive: true });
const invocation = mkdtempSync(path.join(scratch, "invocation-"));

function environment(suite, discovery = false) {
  const root = path.join(invocation, suite || "all");
  const env = { ...process.env, SAVETOOLKIT_E2E_SCRATCH: root,
    SAVETOOLKIT_E2E_SUITE: suite, PLAYWRIGHT_HTML_OUTPUT_DIR: path.join(root, "playwright-report"),
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
        for (const test of spec.tests) result.push(`${spec.id}:${test.projectName}`);
      visit(suite.suites);
    }
  }
  visit(report.suites);
  return result.sort();
}

function discover(suite) {
  const result = spawnSync(process.execPath, [cli, "test", ...args, "--list", "--pass-with-no-tests", "--reporter=json"],
    { env: environment(suite, true), encoding: "utf8", maxBuffer: 16 * 1024 * 1024 });
  if (result.error || result.status !== 0) throw new Error(result.error?.message || result.stderr || result.stdout);
  return identities(JSON.parse(result.stdout));
}

try {
  const all = discover("");
  const canonical = discover("canonical");
  const unreal = discover("unreal");
  const combined = [...canonical, ...unreal].sort();
  if (!all.length || new Set(combined).size !== combined.length || JSON.stringify(all) !== JSON.stringify(combined))
    throw new Error("E2E suites must partition every selected test exactly once, with no empty overall selection.");
  writeFileSync(path.join(invocation, "discovery.json"), JSON.stringify({ all, canonical, unreal }, null, 2));
  console.log(`E2E discovery: ${all.length} selected = ${canonical.length} canonical + ${unreal.length} real Unreal. Reports: ${invocation}`);
  if (!args.includes("--list")) {
    for (const [suite, selected] of [["canonical", canonical], ["unreal", unreal]]) {
      if (!selected.length) continue; // A developer's file/grep filter can select only one complementary suite.
      const result = spawnSync(process.execPath, [cli, "test", ...args],
        { env: environment(suite), stdio: "inherit" });
      if (result.error) throw result.error;
      if (result.status !== 0) process.exitCode = result.status || 1;
      // Run the second mandatory suite even when the first fails, retaining both reports.
    }
  }
} catch (error) {
  console.error(`E2E failed: ${error.message}`);
  process.exitCode = 1;
}
