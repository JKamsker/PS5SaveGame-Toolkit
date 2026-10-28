// Minimal ZIP reader for the browser: lists entries from the central directory and extracts single small
// entries (stored or deflate). Only the bytes it needs are read via Blob.slice, so multi-GB dumps stay cheap.

export interface ZipEntry {
  name: string;
  method: number;
  compressedSize: number;
  size: number;
  localHeaderOffset: number;
}

const EOCD = 0x06054b50;
const ZIP64_LOCATOR = 0x07064b50;
const ZIP64_EOCD = 0x06064b50;
const CENTRAL = 0x02014b50;
const LOCAL = 0x04034b50;
const MAX_COMMENT = 0xffff;
const U32_MAX = 0xffffffff;

async function bytes(blob: Blob, start: number, end: number): Promise<DataView> {
  return new DataView(await blob.slice(start, end).arrayBuffer());
}

const u64 = (v: DataView, o: number) => Number(v.getBigUint64(o, true));

/** Returns the archive's entries, or null if the blob is not a readable ZIP. */
export async function readZipIndex(blob: Blob): Promise<ZipEntry[] | null> {
  const tailStart = Math.max(0, blob.size - (MAX_COMMENT + 22 + 20));
  const tail = await bytes(blob, tailStart, blob.size);
  let eocd = -1;
  for (let i = tail.byteLength - 22; i >= 0; i--) {
    if (tail.getUint32(i, true) === EOCD) {
      eocd = i;
      break;
    }
  }
  if (eocd < 0) return null;

  let count = tail.getUint16(eocd + 10, true);
  let cdSize = tail.getUint32(eocd + 12, true);
  let cdOffset = tail.getUint32(eocd + 16, true);
  if ((count === 0xffff || cdOffset === U32_MAX) && eocd >= 20 && tail.getUint32(eocd - 20, true) === ZIP64_LOCATOR) {
    const z64Offset = u64(tail, eocd - 20 + 8);
    const z64 = await bytes(blob, z64Offset, z64Offset + 56);
    if (z64.getUint32(0, true) !== ZIP64_EOCD) return null;
    count = u64(z64, 32);
    cdSize = u64(z64, 40);
    cdOffset = u64(z64, 48);
  }
  if (cdOffset + cdSize > blob.size) return null;

  const cd = await bytes(blob, cdOffset, cdOffset + cdSize);
  const decoder = new TextDecoder();
  const entries: ZipEntry[] = [];
  let p = 0;
  for (let n = 0; n < count && p + 46 <= cd.byteLength; n++) {
    if (cd.getUint32(p, true) !== CENTRAL) return null;
    const nameLength = cd.getUint16(p + 28, true);
    const extraLength = cd.getUint16(p + 30, true);
    const commentLength = cd.getUint16(p + 32, true);
    const entry: ZipEntry = {
      name: decoder.decode(new Uint8Array(cd.buffer, cd.byteOffset + p + 46, nameLength)),
      method: cd.getUint16(p + 10, true),
      compressedSize: cd.getUint32(p + 20, true),
      size: cd.getUint32(p + 24, true),
      localHeaderOffset: cd.getUint32(p + 42, true),
    };
    applyZip64Extra(cd, p + 46 + nameLength, extraLength, entry);
    entries.push(entry);
    p += 46 + nameLength + extraLength + commentLength;
  }
  return entries;
}

/** ZIP64 extra field (0x0001) carries the real values for fields saturated at 0xFFFFFFFF, in this order. */
function applyZip64Extra(cd: DataView, start: number, length: number, entry: ZipEntry): void {
  for (let q = start; q + 4 <= start + length; ) {
    const id = cd.getUint16(q, true);
    const size = cd.getUint16(q + 2, true);
    if (id === 0x0001) {
      let r = q + 4;
      if (entry.size === U32_MAX) (entry.size = u64(cd, r)), (r += 8);
      if (entry.compressedSize === U32_MAX) (entry.compressedSize = u64(cd, r)), (r += 8);
      if (entry.localHeaderOffset === U32_MAX) entry.localHeaderOffset = u64(cd, r);
      return;
    }
    q += 4 + size;
  }
}

/** Extracts one entry (stored or deflate). Intended for small files such as icons. */
export async function extractZipEntry(blob: Blob, entry: ZipEntry): Promise<Blob> {
  const header = await bytes(blob, entry.localHeaderOffset, entry.localHeaderOffset + 30);
  if (header.getUint32(0, true) !== LOCAL) throw new Error(`Corrupt local header for ${entry.name}`);
  const dataStart = entry.localHeaderOffset + 30 + header.getUint16(26, true) + header.getUint16(28, true);
  const raw = blob.slice(dataStart, dataStart + entry.compressedSize);
  if (entry.method === 0) return raw;
  if (entry.method !== 8) throw new Error(`Unsupported compression method ${entry.method}`);
  const stream = raw.stream().pipeThrough(new DecompressionStream("deflate-raw"));
  return new Response(stream).blob();
}
