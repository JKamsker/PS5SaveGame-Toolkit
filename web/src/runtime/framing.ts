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

/** Builds a frame in the same layout, for responses the worker answers itself. */
export function encodeFrame(status: number, headers: HeaderPairs, body: Uint8Array = new Uint8Array()): Uint8Array {
  const head = new TextEncoder().encode(JSON.stringify({ status, headers }));
  const frame = new Uint8Array(4 + head.byteLength + body.byteLength);
  new DataView(frame.buffer).setUint32(0, head.byteLength, true);
  frame.set(head, 4);
  frame.set(body, 4 + head.byteLength);
  return frame;
}

/** A problem+json frame shaped like the API's own problems (`title`, `detail`, legacy `error`). */
export function problemFrame(status: number, title: string, detail: string): Uint8Array {
  const body = { type: "about:blank", title, status, detail, error: detail };
  return encodeFrame(status, [["Content-Type", "application/problem+json"], ["Cache-Control", "no-cache,no-store"]],
    new TextEncoder().encode(JSON.stringify(body)));
}

