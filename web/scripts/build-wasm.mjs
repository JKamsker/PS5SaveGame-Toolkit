// Publishes the in-browser .NET runtime (src/SaveToolkit.Browser, Release) and copies its
// wwwroot/_framework into web/public/_framework (git-ignored), from where Vite serves it in dev
// and copies it into dist/ on build.
//
// Environment:
//   SAVETOOLKIT_SKIP_WASM_BUILD=1     reuse an existing public/_framework (fails if missing)
//   SAVETOOLKIT_WASM_PUBLISH_DIR      publish output directory (default: <tmp>/savetoolkit-wasm-publish)
//   SAVETOOLKIT_DOTNET_ARTIFACTS      passed to dotnet as --artifacts-path (isolates obj/bin)
//   DOTNET                            dotnet executable (default: dotnet on PATH)
import { spawnSync } from "node:child_process";
import { cp, mkdir, readdir, rm, stat } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const web = fileURLToPath(new URL("..", import.meta.url));
const project = join(web, "..", "src", "SaveToolkit.Browser", "SaveToolkit.Browser.csproj");
const target = join(web, "public", "_framework");

async function exists(path) {
  try { await stat(path); return true; } catch { return false; }
}

if (process.env.SAVETOOLKIT_SKIP_WASM_BUILD === "1") {
  if (!await exists(join(target, "dotnet.js"))) {
    console.error(`SAVETOOLKIT_SKIP_WASM_BUILD=1, but ${target} has no runtime. Run npm run build:wasm first.`);
    process.exit(1);
  }
  console.log(`Reusing the existing runtime in ${target}.`);
  process.exit(0);
}

const publishDir = process.env.SAVETOOLKIT_WASM_PUBLISH_DIR ?? join(tmpdir(), "savetoolkit-wasm-publish");
const args = ["publish", project, "--configuration", "Release", "--output", publishDir, "--nologo"];
if (process.env.SAVETOOLKIT_DOTNET_ARTIFACTS) args.push("--artifacts-path", process.env.SAVETOOLKIT_DOTNET_ARTIFACTS);
await rm(publishDir, { recursive: true, force: true });
console.log(`dotnet ${args.join(" ")}`);
const result = spawnSync(process.env.DOTNET ?? "dotnet", args, { stdio: "inherit", env: { DOTNET_CLI_TELEMETRY_OPTOUT: "1", DOTNET_NOLOGO: "1", ...process.env } });
if (result.error) throw result.error;
if (result.status !== 0) process.exit(result.status ?? 1);

const source = join(publishDir, "wwwroot", "_framework");
if (!await exists(join(source, "dotnet.js"))) throw new Error(`Publish did not produce ${source}/dotnet.js.`);
await rm(target, { recursive: true, force: true });
await mkdir(target, { recursive: true });
// Keep raw files (fallback) and .gz siblings (decompressed by the worker); static hosts cannot negotiate .br.
await cp(source, target, { recursive: true, filter: file => !file.endsWith(".br") });
const files = await readdir(target);
let raw = 0, gzip = 0;
for (const file of files) {
  const { size } = await stat(join(target, file));
  if (file.endsWith(".gz")) gzip += size; else raw += size;
}
console.log(`Copied ${files.length} runtime files to ${target}: ${(raw / 1048576).toFixed(1)} MiB raw, ${(gzip / 1048576).toFixed(1)} MiB gzip.`);
