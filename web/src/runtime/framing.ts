import type { HeaderPairs } from "./protocol";

/**
 * Decodes a response frame produced by SaveToolkit.Browser (WireFormat.cs):
 * a 4-byte little-endian length N, N bytes of UTF-8 JSON `{ status, headers: [[name, value], ...] }`,
 * then the raw body.
 */
export function decodeFrame(frame: Uint8Array): { status: number; headers: HeaderPairs; bodyOffset: number } {
  if (frame.byteLength < 4) throw new Error("Truncated response frame.");
  const length = new DataView(frame.buffer, frame.byteOffset, frame.byteLength).getUint32(0, true);
  const bodyOffset = 4 + length;
  if (bodyOffset > frame.byteLength) throw new Error("Truncated response header.");
  const head: unknown = JSON.parse(new TextDecoder().decode(frame.subarray(4, bodyOffset)));
  if (!head || typeof head !== "object" || !("status" in head) || typeof head.status !== "number" ||
    !("headers" in head) || !Array.isArray(head.headers)) throw new Error("Malformed response header.");
  const headers: HeaderPairs = [];
  for (const pair of head.headers as unknown[]) {
    if (!Array.isArray(pair) || pair.length !== 2 || typeof pair[0] !== "string" || typeof pair[1] !== "string")
      throw new Error("Malformed response header pair.");
    headers.push([pair[0], pair[1]]);
  }
  return { status: head.status, headers, bodyOffset: frame.byteOffset + bodyOffset };
}

