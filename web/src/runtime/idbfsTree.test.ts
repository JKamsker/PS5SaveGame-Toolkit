// @vitest-environment node
import { describe, expect, it } from "vitest";
import { trackTreeChanges, verifyMirrored, writeAfter, type IdbfsSets, type TreeFs } from "./idbfsTree";

/**
 * A model of MEMFS under a coarse clock (Firefox resistFingerprinting, Tor Browser: 100 ms) and of
 * IDBFS, which flushes an entry only when its modification time differs from the stored one.
 */
type Entry = { ino: number; dir: boolean; mtime: number; content: string };
const ROOT = "/persist";

class Fs implements TreeFs {
  nodes = new Map<string, Entry>();
  idb = new Map<string, Entry>();
  stored = 0;
  clock = 1_000;
  private ino = 1;

  readdir(path: string) {
    const prefix = `${path}/`;
    return [".", "..", ...[...this.nodes.keys()].filter(p => p.startsWith(prefix) && !p.slice(prefix.length).includes("/"))
      .map(p => p.slice(prefix.length))];
  }
  stat(path: string) {
    const node = this.nodes.get(path);
    if (!node) throw new Error(`ENOENT ${path}`);
    return { mode: node.dir ? 1 : 0, ino: node.ino, mtime: new Date(node.mtime) };
  }
  isDir(mode: number) { return mode === 1; }
  utime(path: string, atime: number, mtime: number) { this.nodes.get(path)!.mtime = Math.max(atime, mtime); }
  analyzePath(path: string) { return { exists: this.nodes.has(path) }; }
  /** Rewrites a file in place (same node), like FS.writeFile or File.WriteAllBytes on an existing file. */
  writeFile(path: string, content: string) {
    const node = this.nodes.get(path);
    if (node) Object.assign(node, { content, mtime: this.clock });
    else this.nodes.set(path, { ino: this.ino++, dir: false, mtime: this.clock, content });
  }
  mkdir(path: string) { this.nodes.set(path, { ino: this.ino++, dir: true, mtime: this.clock, content: "" }); }
  /** Like File.Move(temp, target, overwrite: true): the target path gets the temp file's node. */
  rename(from: string, to: string) {
    this.nodes.set(to, this.nodes.get(from)!);
    this.nodes.delete(from);
  }
  remove(path: string) { this.nodes.delete(path); }
  /** FS.syncfs(false): writes entries whose time differs, deletes entries the tree lacks. */
  save() {
    for (const [path, node] of this.nodes) {
      if (this.idb.get(path)?.mtime !== node.mtime) { this.idb.set(path, { ...node }); this.stored++; }
    }
    for (const path of [...this.idb.keys()]) if (!this.nodes.has(path)) this.idb.delete(path);
  }
  /** FS.syncfs(true), the other way round. */
  load() {
    for (const [path, entry] of this.idb) {
      if (this.nodes.get(path)?.mtime !== entry.mtime) this.nodes.set(path, { ...entry, ino: this.ino++ });
    }
    for (const path of [...this.nodes.keys()]) if (!this.idb.has(path)) this.nodes.delete(path);
  }
  sets(): IdbfsSets {
    const set = (tree: Map<string, Entry>) => ({ entries: Object.fromEntries([...tree].map(([p, e]) => [p, { timestamp: new Date(e.mtime) }])) });
    return {
      getLocalSet: (_mount, callback) => callback(null, set(this.nodes)),
      getRemoteSet: (_mount, callback) => callback(null, set(this.idb)),
    };
  }
}

function booted() {
  const fs = new Fs();
  fs.mkdir(`${ROOT}/presets`);
  fs.writeFile(`${ROOT}/presets/presets.json`, '["orig"]');
  fs.mkdir(`${ROOT}/reference-packs`);
  fs.writeFile(`${ROOT}/reference-packs/pack-a.zip`, "large archive");
  fs.save();
  fs.stored = 0;
  return fs;
}

/** One mutation: track, change, finish, flush. The clock never advances (one 100 ms tick). */
function mutate(fs: Fs, change: () => void) {
  const finish = trackTreeChanges(fs, ROOT, () => fs.clock);
  change();
  const changed = finish();
  fs.save();
  return changed;
}

describe("tracking changes on a coarse clock", () => {
  it("flushes a rewrite made in the same clock tick as the stored copy", async () => {
    const fs = booted();
    // The model reproduces the defect: untracked, IDBFS skips the rewrite.
    fs.writeFile(`${ROOT}/presets/presets.json`, '["lost"]');
    fs.save();
    expect(fs.idb.get(`${ROOT}/presets/presets.json`)?.content).toBe('["orig"]');
    fs.load();
    expect(mutate(fs, () => fs.writeFile(`${ROOT}/presets/presets.json`, '["renamed"]'))).toBe(true);
    expect(fs.idb.get(`${ROOT}/presets/presets.json`)?.content).toBe('["renamed"]');
    await expect(verifyMirrored(fs.sets(), ROOT)).resolves.toBeUndefined();
  });

  it("flushes a same-size replacement by rename, and again in the same tick", () => {
    const fs = booted();
    for (const name of ['["one!"]', '["two!"]']) {
      expect(mutate(fs, () => {
        fs.writeFile(`${ROOT}/presets/presets.json.tmp`, name);
        fs.rename(`${ROOT}/presets/presets.json.tmp`, `${ROOT}/presets/presets.json`);
      })).toBe(true);
      expect(fs.idb.get(`${ROOT}/presets/presets.json`)?.content).toBe(name);
    }
  });

  it("leaves unchanged entries alone, so large packs are not rewritten", () => {
    const fs = booted();
    const pack = fs.nodes.get(`${ROOT}/reference-packs/pack-a.zip`)!.mtime;
    mutate(fs, () => fs.writeFile(`${ROOT}/presets/presets.json`, "[]"));
    expect(fs.stored).toBe(1);
    expect(fs.nodes.get(`${ROOT}/reference-packs/pack-a.zip`)!.mtime).toBe(pack);
    expect(mutate(fs, () => undefined)).toBe(false);
    expect(fs.stored).toBe(1);
  });

});

describe("tracking removals, the generation marker and verification", () => {
  it("reports removals and lets a reload restore a change that was not flushed", () => {
    const fs = booted();
    expect(mutate(fs, () => fs.remove(`${ROOT}/reference-packs/pack-a.zip`))).toBe(true);
    expect(fs.idb.has(`${ROOT}/reference-packs/pack-a.zip`)).toBe(false);

    const finish = trackTreeChanges(fs, ROOT, () => fs.clock);
    fs.writeFile(`${ROOT}/presets/presets.json`, '["rejected"]');
    expect(finish()).toBe(true);
    expect(finish()).toBe(true);
    fs.load();
    expect(fs.nodes.get(`${ROOT}/presets/presets.json`)?.content).toBe('["orig"]');
  });

  it("writes the generation marker after the previous one", () => {
    const fs = booted();
    writeAfter(fs, `${ROOT}/.generation`, "g1", () => fs.clock);
    fs.save();
    writeAfter(fs, `${ROOT}/.generation`, "g2", () => fs.clock);
    fs.save();
    expect(fs.idb.get(`${ROOT}/.generation`)?.content).toBe("g2");
  });

  it("rejects when IndexedDB does not hold the in-memory tree", async () => {
    const fs = booted();
    fs.utime(`${ROOT}/presets/presets.json`, 5, 5);
    await expect(verifyMirrored(fs.sets(), ROOT)).rejects.toThrow(/did not store 1 changed entry \(\/persist\/presets\/presets.json\)/);
    fs.save();
    await expect(verifyMirrored(fs.sets(), ROOT)).resolves.toBeUndefined();
  });
});
