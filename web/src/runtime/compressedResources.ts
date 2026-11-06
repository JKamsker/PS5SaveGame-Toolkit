/**
 * Static hosts (GitHub Pages, `vite preview`) rarely serve the framework's precompressed files with a
 * Content-Encoding, so the raw ~80 MB would be downloaded. Fetch the `.gz` sibling instead and
 * decompress it in the worker (~21 MB transferred). Falls back to the default URL when the `.gz`
 * file is missing or the browser lacks DecompressionStream.
 */

/** The resource types worth compressing: managed assemblies, the runtime binary and ICU data. */
const compressible = new Set(["assembly", "pdb", "dotnetwasm", "globalization"]);

const GZIP_MAGIC_0 = 0x1f;
const GZIP_MAGIC_1 = 0x8b;

export type BootResourceLoader = (type: string, name: string, defaultUri: string) => Promise<Response> | null;

/**
 * `onBytes` reports every chunk of a runtime file as it arrives, so a slow download that is still moving
 * counts as progress even while one large file takes minutes.
 */
export function createCompressedResourceLoader(fetcher: typeof fetch = fetch, onBytes?: (bytes: number) => void): BootResourceLoader {
  const counted = onBytes ? countingFetch(fetcher, onBytes) : fetcher;
  if (typeof DecompressionStream === "undefined") return () => null;
  return (type, name, defaultUri) => compressible.has(type) ? load(counted, name, defaultUri) : null;
}

function countingFetch(fetcher: typeof fetch, onBytes: (bytes: number) => void): typeof fetch {
  return async (input, init) => {
    const response = await fetcher(input, init);
    if (!response.body || typeof TransformStream === "undefined") return response;
    const counter = new TransformStream<Uint8Array, Uint8Array>({
      transform(chunk, controller) { onBytes(chunk.byteLength); controller.enqueue(chunk); },
    });
    return new Response(response.body.pipeThrough(counter), { status: response.status, statusText: response.statusText, headers: response.headers });
  };
}

async function load(fetcher: typeof fetch, name: string, uri: string): Promise<Response> {
  const headers = { "content-type": name.endsWith(".wasm") ? "application/wasm" : "application/octet-stream" };
  try {
    const compressed = await fetcher(`${uri}.gz`);
    if (compressed.ok) {
      const bytes = new Uint8Array(await compressed.arrayBuffer());
      // A server that already decoded the file (Content-Encoding) hands us the raw bytes.
      if (bytes[0] !== GZIP_MAGIC_0 || bytes[1] !== GZIP_MAGIC_1) return new Response(bytes, { headers });
      const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream("gzip"));
      return new Response(await new Response(stream).arrayBuffer(), { headers });
    }
  } catch {
    // Fall back to the uncompressed file below.
  }
  return fetcher(uri);
}
