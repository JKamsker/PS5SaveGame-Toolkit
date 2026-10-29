import { readFile, readdir } from "node:fs/promises";
import { join } from "node:path";
import { gzipSync } from "node:zlib";

const isCode = file => /\.(js|css)$/.test(file);

async function codeFiles(directory, prefix = "") {
  const files = [];
  for (const entry of await readdir(join(directory, prefix), { withFileTypes: true })) {
    const file = prefix + entry.name;
    if (entry.isDirectory()) files.push(...await codeFiles(directory, `${file}/`));
    else if (isCode(file)) files.push(file);
  }
  return files;
}

/** Inspect emitted bytes, not source lengths or Vite's rounded console estimates. */
export async function measureBundle(directory) {
  const manifest = JSON.parse(await readFile(join(directory, ".vite/manifest.json"), "utf8"));
  const names = new Map();
  for (const chunk of Object.values(manifest)) {
    if (!chunk.file?.endsWith(".js")) continue;
    if (!chunk.name) throw new Error(`Missing chunk name: ${chunk.file}`);
    names.set(chunk.file, `${chunk.name}.js`);
    for (const file of chunk.css ?? []) {
      if (!names.has(file)) names.set(file, `${chunk.name}.css`);
    }
  }

  const initialFiles = new Set();
  const visited = new Set();
  function visit(key) {
    if (visited.has(key)) return;
    const chunk = manifest[key];
    if (!chunk) throw new Error(`Missing manifest import: ${key}`);
    visited.add(key);
    initialFiles.add(chunk.file);
    for (const file of chunk.css ?? []) initialFiles.add(file);
    // Dynamic imports are measured individually, but are not initial-route transfers.
    for (const dependency of chunk.imports ?? []) visit(dependency);
  }
  const entries = Object.keys(manifest).filter(key => manifest[key].isEntry);
  if (entries.length === 0) throw new Error("No entry points in build manifest. Run npm run build first.");
  for (const entry of entries) visit(entry);
  // Validate lazy import edges too, so an incomplete manifest cannot silently pass.
  for (const chunk of Object.values(manifest)) {
    for (const dependency of [...(chunk.imports ?? []), ...(chunk.dynamicImports ?? [])]) {
      if (!manifest[dependency]) throw new Error(`Missing manifest import: ${dependency}`);
    }
  }

  const assets = [];
  for (const file of await codeFiles(directory)) {
    if (!names.has(file)) throw new Error(`Untracked JS/CSS asset: ${file}. Rebuild and review its budget.`);
  }
  for (const [file, name] of [...names].sort(([a], [b]) => a.localeCompare(b))) {
    const bytes = await readFile(join(directory, file));
    assets.push({ file, name, raw: bytes.length, gzip: gzipSync(bytes, { level: 6 }).length, initial: initialFiles.has(file) });
  }
  const initial = { js: 0, css: 0 };
  for (const asset of assets) {
    if (asset.initial) initial[asset.file.endsWith(".js") ? "js" : "css"] += asset.gzip;
  }
  return { assets, initial };
}

export function checkBudgets(report, budgets) {
  const failures = [];
  function check(label, actual, limit) {
    if (!Number.isSafeInteger(limit) || limit <= 0) failures.push(`${label}: missing or invalid gzip budget.`);
    else if (actual > limit) failures.push(`${label}: ${actual} gzip bytes exceeds ${limit} by ${actual - limit} bytes.`);
  }
  for (const kind of ["js", "css"]) check(`Initial ${kind.toUpperCase()}`, report.initial[kind], budgets.initial?.[kind]);
  for (const asset of report.assets) check(asset.file, asset.gzip, budgets.chunks?.[asset.name]);
  return failures;
}
