import { rm } from "node:fs/promises";
import path from "node:path";

export default async function teardown() {
  const directory = process.env.SAVETOOLKIT_E2E_RUN_DIRECTORY;
  if (!directory) return;
  const resolved = path.resolve(directory);
  // Only remove a directory created for this run under the configured scratch root.
  if (path.dirname(resolved) !== path.resolve(process.env.TEMP!) || !path.basename(resolved).startsWith("run-")) {
    throw new Error(`Refusing to remove an unexpected e2e directory: ${resolved}`);
  }
  await rm(resolved, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
}
