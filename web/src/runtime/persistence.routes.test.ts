// @vitest-environment node
import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { expect, it } from "vitest";
import { readsDurableData, writesDurableData } from "./persistence";

/**
 * Checks the durable-data classification against the LocalApi route table itself: a route whose handler
 * reads the preset library or the reference packs (or pins packs to a new document) must refresh another
 * tab's saves first, or take the cross-tab lock when it writes them.
 */
const ENDPOINTS = "../src/SaveToolkit.LocalApi/Endpoints";
/**
 * Handler code that reaches durable data: the stores and the workspace methods that call
 * ReferencePackStore.Pin (Workspace.Open, DocumentGroupRegistry via ProposeGroups and OpenGroup).
 */
const DURABLE = /Services\.(?:Presets|Packs|Transfers|PresetLibrary)\b|\.Open\(|\.OpenGroup\(|\.ProposeGroups\(/;

interface Route { verb: string; path: string; durable: boolean }

function routes(): Route[] {
  const files = readdirSync(ENDPOINTS).filter(name => name.endsWith(".cs"))
    .map(name => ({ type: name.slice(0, -3), text: readFileSync(join(ENDPOINTS, name), "utf8") }));
  const constants = new Map<string, string>();
  const resolve = (type: string, expression: string): string => expression.split("+").map(term => {
    const trimmed = term.trim();
    const literal = /^"([^"]*)"$/.exec(trimmed);
    if (literal) return literal[1]!;
    const value = constants.get(trimmed.includes(".") ? trimmed : `${type}.${trimmed}`);
    if (value === undefined) throw new Error(`Unresolved route constant ${trimmed} in ${type}`);
    return value;
  }).join("");
  // Constants may refer to other files' constants: resolve until nothing new resolves.
  const pending = files.flatMap(({ type, text }) => [...text.matchAll(/const string (\w+) = ([^;]+);/g)]
    .map(match => ({ type, name: match[1]!, expression: match[2]! })));
  for (let progress = true; progress;) {
    progress = false;
    for (const constant of pending.splice(0)) {
      try { constants.set(`${constant.type}.${constant.name}`, resolve(constant.type, constant.expression)); progress = true; }
      catch { pending.push(constant); }
    }
  }
  return files.flatMap(({ type, text }) => {
    const maps = [...text.matchAll(/routes\.Map\("(\w+)",\s*([^,]+?),\s*c\s*=>/g)];
    return maps.map((match, index) => {
      const start = match.index + match[0].length;
      // The handler ends at the next route or where the Map method closes (helpers follow it).
      const methodEnd = text.indexOf("\n    }", start);
      const end = Math.min(maps[index + 1]?.index ?? text.length, methodEnd < 0 ? text.length : methodEnd);
      const path = resolve(type, match[2]!).replace(/\{(\w+)\}/g, "$1-1");
      return { verb: match[1]!, path, durable: DURABLE.test(text.slice(start, end)) };
    });
  });
}

it("refreshes or locks every LocalApi route that reads or pins presets and reference packs", () => {
  const table = routes();
  const durable = table.filter(route => route.durable);
  expect(durable.map(route => `${route.verb} ${route.path}`)).toEqual(expect.arrayContaining([
    "POST /api/workspaces/workspaceId-1/files/fileId-1/open",
    "GET /api/workspaces/workspaceId-1/files/fileId-1/group-proposals",
    "POST /api/workspaces/workspaceId-1/document-groups",
    "GET /api/reference-packs",
    "GET /api/presets",
  ]));
  for (const { verb, path } of durable)
    expect(writesDurableData(verb, path) || readsDurableData(path), `${verb} ${path}`).toBe(true);
});

it("does not hold reads of anything else behind another tab's storage lock", () => {
  const reads = routes().filter(route => !route.durable && route.verb === "GET");
  expect(reads.length).toBeGreaterThan(10);
  for (const { path } of reads) expect(readsDurableData(path), path).toBe(false);
});

it("refreshes reference packs before opening a document or a document group, or proposing groups", () => {
  for (const url of ["/api/workspaces/w/files/f/open", "/api/workspaces/w/document-groups",
    "/api/workspaces/w/files/f/group-proposals?pluginId=p"]) {
    expect(writesDurableData(url.endsWith("group-proposals?pluginId=p") ? "GET" : "POST", url), url).toBe(false);
    expect(readsDurableData(url), url).toBe(true);
  }
});
