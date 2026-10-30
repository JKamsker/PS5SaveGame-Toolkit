import { defineConfig, devices } from "@playwright/test";
import { mkdtempSync, mkdirSync } from "node:fs";
import { createServer } from "node:net";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = fileURLToPath(new URL("..", import.meta.url));
const unrealTests = "**/unreal-*.e2e.ts";
const suite = process.env.SAVETOOLKIT_E2E_SUITE;
if (suite && suite !== "canonical" && suite !== "unreal") throw new Error(`Unknown e2e suite: ${suite}`);
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
  testMatch: suite === "unreal" ? unrealTests : "**/*.e2e.ts",
  testIgnore: suite === "canonical" ? unrealTests : [],
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  timeout: 30_000,
  globalTimeout: 180_000,
  globalTeardown: "./e2e/teardown.ts",
  outputDir: path.join(scratch, "test-results"),
  reporter: [["list"], ["html", { outputFolder: path.join(scratch, "playwright-report"), open: "never" }],
    ["junit", { outputFile: path.join(scratch, "e2e.xml") }]],
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
