// @vitest-environment node
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { gzipSync } from "node:zlib";
import { afterEach, expect, it } from "vitest";
import { checkBudgets, measureBundle } from "./bundle-size.mjs";

const directories = [];
afterEach(() => directories.splice(0).forEach(directory => rmSync(directory, { recursive: true, force: true })));

function fixture() {
  const directory = mkdtempSync(join(tmpdir(), "bundle-size-"));
  directories.push(directory);
  mkdirSync(join(directory, ".vite"));
  mkdirSync(join(directory, "assets"));
  const manifest = {
    "index.html": { file: "assets/index.js", name: "index", isEntry: true, imports: ["shared"], dynamicImports: ["editor"], css: ["assets/index.css"] },
    shared: { file: "assets/shared.js", name: "shared", imports: ["index.html"], css: ["assets/index.css"] },
    editor: { file: "assets/editor.js", name: "editor", imports: ["shared"], css: ["assets/editor.css"] },
  };
  for (const name of ["index.js", "shared.js", "editor.js", "index.css", "editor.css"]) {
    writeFileSync(join(directory, "assets", name), name.repeat(100));
  }
  const saveManifest = () => writeFileSync(join(directory, ".vite/manifest.json"), JSON.stringify(manifest));
  saveManifest();
  return { directory, manifest, saveManifest };
}

const generousBudgets = {
  initial: { js: 1000, css: 1000 },
  chunks: { "index.js": 1000, "shared.js": 1000, "editor.js": 1000, "index.css": 1000, "editor.css": 1000 },
};

it("sums static entry imports once, handles cycles, and excludes dynamic JS/CSS from initial totals", async () => {
  const { directory } = fixture();
  const report = await measureBundle(directory);
  const size = file => gzipSync(Buffer.from(file.repeat(100)), { level: 6 }).length;
  expect(report.initial).toEqual({ js: size("index.js") + size("shared.js"), css: size("index.css") });
  expect(report.assets).toHaveLength(5);
  expect(report.assets.find(asset => asset.file === "assets/editor.js"))
    .toMatchObject({ raw: 900, gzip: size("editor.js"), initial: false });
  expect(checkBudgets(report, generousBudgets)).toEqual([]);
}, 15_000); // Cold filesystem/gzip initialization exceeded 5 s on the busy Windows host; no timing assertion.

it.each(["js", "css"])("rejects aggregate initial %s even when every individual chunk fits", async kind => {
  const report = await measureBundle(fixture().directory);
  const budgets = { ...generousBudgets, initial: { ...generousBudgets.initial, [kind]: report.initial[kind] - 1 } };
  expect(checkBudgets(report, budgets)).toEqual([expect.stringContaining(`Initial ${kind.toUpperCase()}`)]);
});

it.each(["editor.js", "editor.css"])("enforces lazy %s limits and accepts an exact boundary", async name => {
  const report = await measureBundle(fixture().directory);
  const asset = report.assets.find(asset => asset.name === name);
  const budgets = { ...generousBudgets, chunks: { ...generousBudgets.chunks, [name]: asset.gzip } };
  expect(checkBudgets(report, budgets)).toEqual([]);
  budgets.chunks[name]--;
  expect(checkBudgets(report, budgets)).toEqual([expect.stringContaining(asset.file)]);
});

it("requires an explicit budget for new chunks", async () => {
  const report = await measureBundle(fixture().directory);
  expect(checkBudgets(report, { ...generousBudgets, chunks: {} })).toHaveLength(5);
});

it("fails on missing builds, missing assets, absent entry points and broken import graphs", async () => {
  const { directory, manifest, saveManifest } = fixture();
  await expect(measureBundle(join(directory, "missing"))).rejects.toThrow();
  rmSync(join(directory, "assets/editor.css"));
  await expect(measureBundle(directory)).rejects.toThrow();
  writeFileSync(join(directory, "assets/editor.css"), "css");
  manifest["index.html"].isEntry = false;
  saveManifest();
  await expect(measureBundle(directory)).rejects.toThrow(/entry/i);
  manifest["index.html"].isEntry = true;
  manifest["index.html"].imports.push("missing");
  saveManifest();
  await expect(measureBundle(directory)).rejects.toThrow(/missing/i);
});

it("rejects JS/CSS outside the manifest instead of silently skipping copied or stale files", async () => {
  const { directory } = fixture();
  writeFileSync(join(directory, "assets/extra.js"), "untracked");
  await expect(measureBundle(directory)).rejects.toThrow(/extra.js/);
});
