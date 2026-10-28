// @vitest-environment node
import { describe, expect, it } from "vitest";
import { extractZipEntry, readZipIndex } from "./zipIndex";

const enc = new TextEncoder();

async function deflateRaw(data: Uint8Array): Promise<Uint8Array> {
  const stream = new Blob([data as BlobPart]).stream().pipeThrough(new CompressionStream("deflate-raw"));
  return new Uint8Array(await new Response(stream).arrayBuffer());
}

/** Builds a tiny ZIP with the given entries; method 8 entries are deflated. */
async function buildZip(files: { name: string; data: Uint8Array; method: 0 | 8 }[]): Promise<Blob> {
  const parts: Uint8Array[] = [];
  const central: Uint8Array[] = [];
  let offset = 0;
  for (const f of files) {
    const name = enc.encode(f.name);
    const payload = f.method === 8 ? await deflateRaw(f.data) : f.data;
    const local = new DataView(new ArrayBuffer(30));
    local.setUint32(0, 0x04034b50, true);
    local.setUint16(8, f.method, true);
    local.setUint32(18, payload.length, true);
    local.setUint32(22, f.data.length, true);
    local.setUint16(26, name.length, true);
    parts.push(new Uint8Array(local.buffer), name, payload);

    const cd = new DataView(new ArrayBuffer(46));
    cd.setUint32(0, 0x02014b50, true);
    cd.setUint16(10, f.method, true);
    cd.setUint32(20, payload.length, true);
    cd.setUint32(24, f.data.length, true);
    cd.setUint16(28, name.length, true);
    cd.setUint32(42, offset, true);
    central.push(new Uint8Array(cd.buffer), name);
    offset += 30 + name.length + payload.length;
  }
  const cdSize = central.reduce((n, c) => n + c.length, 0);
  const eocd = new DataView(new ArrayBuffer(22));
  eocd.setUint32(0, 0x06054b50, true);
  eocd.setUint16(8, files.length, true);
  eocd.setUint16(10, files.length, true);
  eocd.setUint32(12, cdSize, true);
  eocd.setUint32(16, offset, true);
  return new Blob([...parts, ...central, new Uint8Array(eocd.buffer)] as BlobPart[]);
}

describe("zipIndex", () => {
  it("lists entries, including repeated paths", async () => {
    const zip = await buildZip([
      { name: "PPSA02530/sdimg_A/data.bin", data: enc.encode("one"), method: 0 },
      { name: "PPSA02530/sdimg_A/data.bin", data: enc.encode("two"), method: 0 },
    ]);
    const entries = await readZipIndex(zip);
    expect(entries?.map((e) => e.name)).toEqual(["PPSA02530/sdimg_A/data.bin", "PPSA02530/sdimg_A/data.bin"]);
  });

  it("extracts stored and deflated entries", async () => {
    const icon = enc.encode("PNG-ish data ".repeat(50));
    const zip = await buildZip([
      { name: "a.txt", data: enc.encode("hello"), method: 0 },
      { name: "PPSA01/sdimg_X/sce_sys/icon0.png", data: icon, method: 8 },
    ]);
    const entries = (await readZipIndex(zip))!;
    expect(await (await extractZipEntry(zip, entries[0]!)).text()).toBe("hello");
    expect(new Uint8Array(await (await extractZipEntry(zip, entries[1]!)).arrayBuffer())).toEqual(icon);
  });

  it("returns null for non-zip data", async () => {
    expect(await readZipIndex(new Blob([enc.encode("not a zip at all")]))).toBeNull();
  });
});
