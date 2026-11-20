// @vitest-environment node
// jsdom's Blob has no stream(); the worker runs with the platform's.
import { expect, it, vi } from "vitest";
import { createCompressedResourceLoader } from "./compressedResources";

const gzip = async (bytes: Uint8Array) =>
  new Uint8Array(await new Response(new Blob([bytes as BlobPart]).stream().pipeThrough(new CompressionStream("gzip"))).arrayBuffer());

/** A response whose body arrives in chunks, like a slow download. */
const chunked = (bytes: Uint8Array, size: number) => new Response(new ReadableStream<Uint8Array>({
  start(controller) {
    for (let offset = 0; offset < bytes.length; offset += size) controller.enqueue(bytes.slice(offset, offset + size));
    controller.close();
  },
}));

it("decompresses the .gz sibling and reports every received chunk as download progress", async () => {
  const raw = new Uint8Array(4096).map((_, i) => i % 251);
  const compressed = await gzip(raw);
  const fetcher = vi.fn((url: string) => Promise.resolve(url.endsWith(".gz") ? chunked(compressed, 100) : new Response(null, { status: 404 })));
  const chunks: number[] = [];
  const load = createCompressedResourceLoader(fetcher as typeof fetch, bytes => chunks.push(bytes));

  const response = await load("assembly", "App.wasm", "/_framework/App.wasm")!;
  expect(new Uint8Array(await response.arrayBuffer())).toEqual(raw);
  expect(chunks.length).toBe(Math.ceil(compressed.length / 100));
  expect(chunks.reduce((sum, bytes) => sum + bytes, 0)).toBe(compressed.length);
  expect(load("js-module-runtime", "dotnet.runtime.js", "/_framework/dotnet.runtime.js")).toBeNull();
});

it("counts the uncompressed fallback too", async () => {
  const raw = new Uint8Array(300).fill(7);
  const fetcher = vi.fn((url: string) => Promise.resolve(url.endsWith(".gz") ? new Response(null, { status: 404 }) : chunked(raw, 128)));
  let received = 0;
  const load = createCompressedResourceLoader(fetcher as typeof fetch, bytes => { received += bytes; });
  const response = await load("assembly", "App.wasm", "/_framework/App.wasm")!;
  expect(new Uint8Array(await response.arrayBuffer())).toEqual(raw);
  expect(received).toBe(300);
});
