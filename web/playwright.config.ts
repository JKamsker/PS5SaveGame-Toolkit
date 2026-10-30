import { mkdtempSync, mkdirSync, readFileSync } from "node:fs";
import { createServer } from "node:net";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { defineConfig, devices } from "@playwright/test";

const repositoryRoot = fileURLToPath(new URL("..", import.meta.url));
const suiteManifest = JSON.parse(readFileSync(new URL("./scripts/e2e-suites.json", import.meta.url), "utf8")) as {
  version: number;
  suites: { id: string; role: "feature" | "complement"; patterns: string[]; files: string[]; optionalFiles?: string[] }[];
};
const suite = process.env.SAVETOOLKIT_E2E_SUITE;
const featureSuites = suiteManifest.suites.filter(entry => entry.role === "feature");
const selectedSuite = suiteManifest.suites.find(entry => entry.id === suite);
if (suite && !selectedSuite) throw new Error(`Unknown e2e suite: ${suite}`);
const featurePatterns = featureSuites.flatMap(entry => entry.patterns);
const scratch = path.resolve(process.env.SAVETOOLKIT_E2E_SCRATCH ?? (process.platform === "win32"
  ? "D:/Temp/saveeditor/test-scratch/e2e-smoke"
  : path.join(tmpdir(), "saveeditor/test-scratch/e2e-smoke")));
mkdirSync(scratch, { recursive: true });
// Chromium's temporary profiles and downloads must use the same isolated scratch root.
process.env.TEMP = process.env.TMP = process.env.TMPDIR = scratch;
if (process.platform === "win32") process.env.PLAYWRIGHT_BROWSERS_PATH ??= path.join(scratch, "browsers");

async function freeLoopbackUrl(): Promise<string> {
  const server = createServer();
  await new Promise<void>((resolve, reject) => {
    server.once("error", reject);
    server.listen(0, "127.0.0.1", resolve);
  });
  const address = server.address();
  if (!address || typeof address === "string") throw new Error("Expected a loopback TCP port.");
  await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
  return `http://127.0.0.1:${address.port}`;
}

// Config is also loaded in workers. Reuse the run's URL and directory there.
const baseURL = process.env.SAVETOOLKIT_E2E_URL ??= await freeLoopbackUrl();
const runDirectory = process.env.SAVETOOLKIT_E2E_RUN_DIRECTORY ??= mkdtempSync(path.join(scratch, "run-"));

export default defineConfig({
  testDir: "./e2e",
  // Keep browser tests out of Vitest's *.test / *.spec discovery.
  testMatch: selectedSuite?.role === "feature" ? selectedSuite.patterns : "**/*.e2e.ts",
  testIgnore: selectedSuite?.role === "complement" ? featurePatterns : [],
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  timeout: 30_000,
  globalTimeout: 180_000,
  globalTeardown: "./e2e/teardown.ts",
  outputDir: path.join(scratch, "test-results"),
  reporter: [["list"], ["html", { outputFolder: process.env.PLAYWRIGHT_HTML_OUTPUT_DIR ?? path.join(scratch, "playwright-report"), open: "never" }],
    ["junit", { outputFile: process.env.PLAYWRIGHT_JUNIT_OUTPUT_FILE ?? path.join(scratch, "e2e.xml") }]],
  use: {
    baseURL,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    command: `dotnet run --project src/SaveToolkit.Api --configuration Release --no-build --no-launch-profile --urls ${baseURL} -- --Toolkit:DataRoot="${path.join(runDirectory, "workspaces")}" --Toolkit:PackRoot="${path.join(runDirectory, "reference-packs")}" --Toolkit:PresetRoot="${path.join(runDirectory, "presets")}"`,
    cwd: repositoryRoot,
    url: `${baseURL}/api/version`,
    reuseExistingServer: false,
    timeout: 60_000,
    gracefulShutdown: { signal: "SIGTERM", timeout: 5_000 },
  },
});
