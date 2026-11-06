# Browser runtime

The toolkit is a static web app. The React UI, the .NET runtime compiled to WebAssembly and every
plugin are plain files in `web/dist`; any static host (GitHub Pages, `npm run preview`, a local
`python3 -m http.server`) can serve them. No server process runs, and imported saves never leave the
browser tab. The C# Sdk, Core, Codecs and plugins are the same assemblies the tests and the optional
ASP.NET host use; only the transport changed.

## Request path

```
React UI (web/src)                                    main thread
  │ fetchApi("/api/...")  →  runtime client (web/src/runtime/client.ts)
  │                           serializes method, URL, headers, body (multipart as the browser encodes it)
  ▼ postMessage {type:"request"} (body ArrayBuffer transferred)
Web worker (web/src/runtime/worker.ts)                one request at a time, in arrival order
  │ boots _framework/dotnet.js once (.gz siblings decompressed in the worker)
  ▼ Interop.DispatchAsync(method, url, headersJson, body)   [JSExport]
SaveToolkit.Browser (src/SaveToolkit.Browser)         .NET WebAssembly host (Mono interpreter)
  │ BrowserHost: parses headers, sweeps idle workspaces between requests
  ▼ LocalApiHost.SendAsync(LocalRequest)
SaveToolkit.LocalApi (src/SaveToolkit.LocalApi)        transport-independent /api router
  │ routes, validation, ETag/If-Match, problem+json, Content-Disposition (same DTOs as the Api)
  ▼
SaveToolkit.Core / Sdk / Codecs / plugins             unchanged: import, sessions, edits, export
```

The UI receives a real `Response` for every `/api` call, so components, caching and error handling
are the same as with an HTTP server. [API.md](API.md) documents the contract; its routes, status codes
and bodies apply unchanged.

## Interop and framing

`src/SaveToolkit.Browser/Interop.cs` exports three functions to the worker:

| Export | Purpose |
| --- | --- |
| `Initialize(optionsJson)` | Loads the plugin assemblies, creates the `LocalApiHost` and returns boot info JSON: version, plugin IDs and assemblies that failed to load. Options name the session and persistent roots. |
| `DispatchAsync(method, url, headersJson, body)` | Runs one request and returns an integer handle. Headers are a JSON array of `[name, value]` pairs; `url` is the percent-encoded path and query. |
| `TakeResponse(handle)` | Removes and returns the framed response for a handle (source-generated interop cannot return `Task<byte[]>`). |

A response frame (`WireFormat.cs`, decoded by `web/src/runtime/framing.ts`) is a 4-byte
little-endian length N, then N bytes of UTF-8 JSON `{ "status": 200, "headers": [[name, value], ...] }`,
then the raw body. The worker transfers the frame buffer to the page without copying it again.
Unhandled host failures become a `500` problem+json frame; a request before initialization gets `503`.

Worker messages (`web/src/runtime/protocol.ts`): the page sends `boot`, `request` and `cancel`; the
worker answers with `progress`, `ready`, `failed`, `response` and `error`. A request aborted before the
worker starts it is dropped; one already running completes, and its late result is discarded, matching
an aborted network fetch.

## Plugin discovery

WebAssembly loads assemblies lazily, so reflection-based discovery would only see the built-in raw
viewer. `SaveToolkit.Browser.csproj` references every `plugins/*/*/SaveToolkit.Plugins.*.csproj` (tests
excluded, same glob as the Api) and its `GenerateSaveToolkitPluginManifest` target writes the resolved
plugin assembly names into a generated `PluginManifest` class. At boot `PluginLoader` loads each name,
then `PluginCatalog.Discover()` finds the plugin types. Adding a plugin folder needs no host or UI edit;
rebuilding the static site includes it. An assembly that fails to load is logged and skipped.

## Persistence

| Data | Location in the worker | Lifetime |
| --- | --- | --- |
| Workspaces (imports, sessions, history, exports) | `/savetoolkit/session` on Emscripten's in-memory file system | The tab. Reloading or closing it discards them; export first. |
| Presets and reference packs | `/savetoolkit/persist` mounted as IDBFS | IndexedDB of the site's origin. Flushed after every non-GET request. |

Without IndexedDB (some private windows, blocked storage) the persistent root stays in memory and boot
info reports `persistent: false`. Storage is per origin: the hosted build, a local preview and a
release archive served on another port each have their own presets and packs. Idle workspaces are
swept between requests with the same timeout as the Api host, so all work stays serialized.

## Startup

Time to first answer is about 1.1 s in headless Chromium. The ~40 embedded reference packs are
not validated at construction: a document open validates the packs that can apply to its plugin, and
one second after boot the host validates the remaining packs one at a time while no request is
running. A request that arrives mid-pack waits for that pack (up to ~0.5 s measured). Every pack is
still fully checked exactly once; see `ReferencePackStore` and `EmbeddedPackSet` in Core.

## Build and deployment

- `cd web; npm run build` runs `scripts/build-wasm.mjs` (a Release `dotnet publish` of
  `src/SaveToolkit.Browser`, then a copy of its `_framework` into `web/public/_framework`, git-ignored),
  then `tsc -b` and `vite build` into `web/dist`. `npm run build:wasm` and `npm run build:ui` run the
  halves separately. Building the runtime needs the `wasm-tools` workload: SQLite is relinked into
  `dotnet.native.wasm` and IDBFS is linked in.
- `SAVETOOLKIT_WEB_BASE` sets the base path, for example `/PS5SaveGame-Toolkit/app/` on GitHub Pages.
  The router, worker and `_framework` URLs all follow it. Release archives use `/`.
- Static hosts rarely negotiate precompressed files, so the worker fetches each runtime file's `.gz`
  sibling and decompresses it itself, falling back to the raw file. Brotli files are not shipped.
- [RELEASING.md](RELEASING.md#github-pages) describes the Pages workflow and the release archive.

## Limits

- **Download and boot.** The first visit transfers about 22 MB of gzip runtime and plugin data
  (about 82 MB uncompressed); later visits use the HTTP cache. Booting takes roughly a second on a
  desktop before the first request is answered; see [PERFORMANCE.md](PERFORMANCE.md#browser-runtime-boot).
- **Memory.** The runtime uses 32-bit WebAssembly memory with the .NET default maximum heap of
  2 GiB, shared by every plugin's embedded data and all open workspaces. An upload exists several times
  during import (the page's buffer, the worker's copy in the .NET heap and the extracted files in
  memory), so very large dumps can fail with an out-of-memory error where the native host would
  succeed. Import large dumps one title at a time and close workspaces you no longer need.
- **One thread.** All requests run sequentially in one worker; a long import or export delays later
  requests, though the UI thread stays responsive. Code that blocks on a task cannot complete there.
- **Framework APIs.** APIs annotated `[UnsupportedOSPlatform("browser")]` throw
  `PlatformNotSupportedException` (MD5 throws `CryptographicException`). In .NET 10 that includes `MD5`
  and `Aes.Create`; SHA-1/SHA-2, HMAC-SHA and `RandomNumberGenerator` work. Plugins that need MD5 or
  AES (BaldursGate3, GodOfWarRagnarok, EldenRing, Pragmata, TalesOfArise) use `SaveToolkit.Sdk.Crypto`
  (`PortableMd5`, `IncrementalMd5`, `PortableAes`): the platform implementation natively, a managed
  RFC 1321 / FIPS-197 implementation in the browser, tested against official vectors and the platform.
  See [PLUGIN_GUIDE.md](PLUGIN_GUIDE.md#browser-runtime).
- **No OpenAPI document or SPA fallback.** `/openapi/v1.json` exists only on the ASP.NET host. A static
  host returns its own 404 for a reloaded deep link; workspaces would be gone after a reload anyway.

## Parity testing

The browser cannot be exercised by every backend test, so correctness is anchored to the ASP.NET host:

- `tests/SaveToolkit.LocalApi.Tests` runs the same request scenarios against the real Api
  (`WebApplicationFactory`) and an in-process `LocalApiHost`, comparing status, content type, ETag,
  `Allow`, `Content-Disposition` and normalized bodies. It also checks the hand-written HTTP primitives
  (query, multipart, entity tags, content disposition) against their ASP.NET counterparts and reruns
  existing Api contract tests against the in-process host.
- `tests/SaveToolkit.Api.Performance.Tests` keeps the OpenAPI snapshot, so contract drift in either host
  fails CI.
- `web/src/runtime/client.test.ts` covers frame decoding and the worker client: multipart
  serialization, ordering, cancellation, boot failure and `Response` reconstruction.
- The CI `e2e` job builds `web/dist` (runtime included) and runs the Playwright suites against
  `vite preview`, exercising the real WebAssembly runtime in Chromium with no API process.
