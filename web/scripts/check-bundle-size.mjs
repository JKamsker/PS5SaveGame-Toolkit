import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { resolveConfig } from "vite";
import { checkBudgets, measureBundle } from "./bundle-size.mjs";

try {
  const config = await resolveConfig({}, "build");
  const budgets = JSON.parse(await readFile(new URL("../bundle-budgets.json", import.meta.url), "utf8"));
  const report = await measureBundle(resolve(config.root, config.build.outDir));
  console.table(report.assets.map(asset => ({
    file: asset.file, route: asset.initial ? "initial" : "lazy", raw: asset.raw, gzip: asset.gzip,
    budget: budgets.chunks[asset.name],
  })));
  console.log(`Initial JS: ${report.initial.js} / ${budgets.initial.js} gzip bytes`);
  console.log(`Initial CSS: ${report.initial.css} / ${budgets.initial.css} gzip bytes`);
  const failures = checkBudgets(report, budgets);
  if (failures.length) throw new Error(failures.join("\n"));
  console.log("Bundle size budgets passed (gzip level 6). See docs/PERFORMANCE.md.");
} catch (error) {
  console.error(`Bundle size check failed: ${error.message}`);
  process.exitCode = 1;
}
