const zlib = require("zlib");
const crypto = require("crypto");
const sha = b => crypto.createHash("sha256").update(b).digest("hex").slice(0, 16);

function readZip(buf) {
  let eocd = -1;
  for (let i = buf.length - 22; i >= 0 && i > buf.length - 70000; i--) if (buf.readUInt32LE(i) === 0x06054b50) { eocd = i; break; }
  if (eocd < 0) return { error: "no EOCD" };
  const count = buf.readUInt16LE(eocd + 10);
  let off = buf.readUInt32LE(eocd + 16);
  const entries = [];
  for (let n = 0; n < count; n++) {
    if (buf.readUInt32LE(off) !== 0x02014b50) return { error: "bad CD" };
    const madeBy = buf.readUInt16LE(off + 4), needed = buf.readUInt16LE(off + 6), flags = buf.readUInt16LE(off + 8);
    const method = buf.readUInt16LE(off + 10), time = buf.readUInt16LE(off + 12), date = buf.readUInt16LE(off + 14);
    const crc = buf.readUInt32LE(off + 16), csize = buf.readUInt32LE(off + 20), usize = buf.readUInt32LE(off + 24);
    const nlen = buf.readUInt16LE(off + 28), xlen = buf.readUInt16LE(off + 30), clen = buf.readUInt16LE(off + 32);
    const extAttr = buf.readUInt32LE(off + 38), local = buf.readUInt32LE(off + 42);
    const name = buf.slice(off + 46, off + 46 + nlen).toString("utf8");
    const extra = buf.slice(off + 46 + nlen, off + 46 + nlen + xlen).toString("hex");
    const lnlen = buf.readUInt16LE(local + 26), lxlen = buf.readUInt16LE(local + 28);
    const data = buf.slice(local + 30 + lnlen + lxlen, local + 30 + lnlen + lxlen + csize);
    let content;
    try { content = method === 8 ? zlib.inflateRawSync(data) : data; } catch (e) { content = Buffer.from("INFLATE ERROR " + e.message); }
    entries.push({ name, method, flags, madeBy, needed, time, date, crc: crc.toString(16), csize, usize, extAttr, extra, localExtra: buf.slice(local + 30 + lnlen, local + 30 + lnlen + lxlen).toString("hex"), sha: sha(content), contentLength: content.length, _content: content });
    off += 46 + nlen + xlen + clen;
  }
  return { entries, comment: buf.slice(eocd + 22).toString("hex") };
}
module.exports = { readZip, sha };
