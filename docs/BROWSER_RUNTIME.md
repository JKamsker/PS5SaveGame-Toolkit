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
| `ReloadPersistent()` | Makes the preset and reference-pack stores re-read their files after the worker reloaded the persistent mount (see [Persistence](#persistence)); returns an error message or an empty string. |

A response frame (`WireFormat.cs`, decoded by `web/src/runtime/framing.ts`) is a 4-byte
little-endian length N, then N bytes of UTF-8 JSON `{ "status": 200, "headers": [[name, value], ...] }`,
then the raw body. The worker transfers the frame buffer to the page without copying it again.
Unhandled host failures become a `500` problem+json frame; a request before initialization gets `503`.
There is no server log in the browser, so the host replaces the shared "See the server log" detail of
unexpected `500`s (`LocalApiOptions.DescribeUnexpectedError`) with one that points at the developer
console, or says the runtime ran out of memory; status and problem shape are unchanged.

Worker messages (`web/src/runtime/protocol.ts`): the page sends `boot`, `request` and `cancel`; the
worker answers with `progress`, `ready`, `failed`, `started`, `response` and `error`. A request aborted
before the worker starts it is dropped; one already running completes, and its late result is
discarded, matching an aborted network fetch. `started` marks the moment a request leaves the worker's
queue: `fetchApi` starts its deadline (60 s, 5 min for transfers) there, so a read queued behind a long
import does not time out; only the queue wait itself is capped, at 30 minutes. An `error` whose cause
is an exhausted heap carries `outOfMemory`, and the UI then says so instead of "did not respond".

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
| Presets and reference packs | `/savetoolkit/persist` mounted as IDBFS | IndexedDB of the site's origin, shared by all its tabs. Flushed before a mutation answers. |

Without IndexedDB (some private windows, blocked storage) the persistent root stays in memory, boot
info reports `persistent: false` and the UI shows a banner that presets and packs will not survive a
reload. Storage is per origin: the hosted build, a local preview and a release archive served on another
port each have their own presets and packs. Idle workspaces are swept between requests with the same
timeout as the Api host, so all work stays serialized.

Every tab has its own in-memory copy of the mount, and `FS.syncfs(false)` mirrors that whole copy onto
IndexedDB, deleting entries the copy lacks. A tab flushing a stale copy would therefore delete or revert
what another tab saved. `web/src/runtime/persistence.ts` prevents that:

- **Durable mutations** (every non-GET request except workspace routes; a workspace's `presets/*`
  routes are included because capture saves a preset, except preview, apply and
  `transfer/compatibility`, which only read the library and run as reads) run under an exclusive
  [Web Lock](https://developer.mozilla.org/docs/Web/API/Web_Locks_API) shared by all tabs. Inside it the
  worker reloads the mount from IndexedDB (`syncfs(true)`); if the `.generation` marker differs from the
  one this tab last saw, `ReloadPersistent()` makes the .NET stores re-read their files
  (`LocalApiHost.ReloadPersistentStores`). Then the request runs and, only if the tree changed, the
  worker writes a new generation, flushes, checks that IndexedDB now lists exactly the in-memory entries
  and announces the change on a `BroadcastChannel`.
- **A failed store reload** (for example another tab on a newer version of the site saved a preset
  schema this version cannot read) is not adopted: the stores keep their last good view for reads, and
  every mutation is refused with `503` until a reload succeeds, because the request would write that
  stale view back over the other tab's saves. As a second guard, `PresetStore` refuses create, rename
  and delete with `409 presetLibraryReloadRequired` when `presets.json` is no longer the file it last
  read or wrote (both hosts). User reference packs need no such guard: each is its own file, and a
  write never touches the others. A store reload keeps the preset library's identity when
  `presets.json` is byte-for-byte the file the store last read or wrote, so a reviewed preset-package
  import survives unrelated changes in other tabs (a pack import, say) and is refused only when the
  library really changed.
- **A library this version cannot read at startup** (malformed, or saved by a newer version) does not
  stop the runtime from booting: like the Api, whose preset store is a lazily created singleton, only
  preset requests fail (`422`, with the reason), the file is left untouched, and every later preset
  request reads it again, so a repaired or removed file is picked up without a reload.
- **Change detection does not use the clock.** IDBFS flushes an entry only when its modification time
  differs from the stored one, and MEMFS stamps writes with `Date.now()`, which some browsers round to
  100 ms (Firefox `privacy.resistFingerprinting`, Tor Browser), so a file rewritten within one tick of
  its stored copy would silently not be saved. Before a mutation runs, the worker stamps every entry of
  the mount with a sentinel time; afterwards, entries still carrying it (and the same node) get their
  stored time back, and all others count as changed and get a time strictly after the stored one
  (`web/src/runtime/idbfsTree.ts`).
- **Reads** of presets and reference packs (their routes, a document's preset routes and opening a
  document) after such an announcement reload the mount under a shared lock first, so another tab's
  save is visible on the next such request once the announcement has arrived (milliseconds later). They
  wait at most 1 s for the lock: while another tab holds it (a long import, a frozen tab) they answer
  from the cached view, keep it marked stale and do not wait again until the lock is next granted. Other reads
  never wait for or touch storage. Until then a read may be stale, but a write never is: it always
  reloads under the lock first.
- **Cleared site data.** IDBFS opens its IndexedDB connection once and caches it, so after "Clear site
  data" (or the database being deleted) every later sync would fail until a reload, even after another
  tab recreated the database. The worker drops the cached connection when the browser closes it (or
  another tab needs it closed) and marks the data stale, and when a sync fails on a cached connection it
  retries once on a new one. The next preset or pack read, and any write, then reloads what IndexedDB
  holds now: an emptied database means the presets and packs are gone, and the tab shows that.
- **A hung mutation.** Web Locks are released when a tab (and its worker) closes or reloads. A mutation
  whose .NET dispatch never settles would otherwise hold the exclusive lock for as long as the tab stays
  open; after 10 minutes (twice the UI's longest request deadline) the worker releases it, stops
  touching storage for good and reports the runtime as crashed, and the page offers a reload. A
  synchronous hang also stops the worker's timers, so then only closing or reloading that tab helps;
  other tabs' writes answer `503` after 30 s meanwhile, and their reads are not delayed.
- **A failed flush** (for example `QuotaExceededError`) is never a silent success: the worker reloads
  what IndexedDB really holds, so this tab's stores match it, and answers `507 Insufficient Storage`
  (`503` for other storage errors) as problem+json that the UI shows. A request that failed anyway keeps
  its own error. If even that reload fails, the tab forgets which generation its stores reflect, so the
  next request reloads them before anything is written (the rejected change is never flushed later).
  If IndexedDB cannot be read before a mutation, or another tab holds the lock for more than 30 s, the
  mutation is refused with `503` without running. A tab that boots while another holds the lock for
  more than 30 s starts with an empty mount but stays persistent: reads try the lock without waiting
  and load the data as soon as it is free, and a mutation always waits for the lock and loads first.
- Without Web Locks (an insecure `http://` origin other than localhost, or an old browser) the same
  reload/flush sequence runs unserialized; boot info reports `tabCoordination: false` and the UI asks
  the user to change presets and packs in one tab at a time.

Boot info also lists plugin assemblies that failed to load (`failedAssemblies`); the UI shows them in
the same non-blocking banner.

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
- **SPA fallback.** The build also writes `dist/404.html`, a copy of `index.html`, which static hosts serve
  for unknown paths, so a reloaded deep link such as `/w/<id>` loads the app and routes it (to "This
  workspace is gone" after a reload). The copy works from any nested path because the base path is absolute
  (`/` or `/<repo>/app/`); a relative base such as `./` would resolve `assets/` below the deep link, so the
  build skips the fallback for it with a warning (`web/src/build/spaFallback.ts`). GitHub Pages only uses
  the `404.html` at the site root, so the Pages workflow copies the app's file there; addresses outside
  the app's base (a moved documentation page) then show a not-found page that links to the documentation
  home and the editor, without downloading the runtime.
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
- **No OpenAPI document.** `/openapi/v1.json` exists only on the ASP.NET host.
- **Workspaces last as long as the tab.** While this tab's runtime holds a workspace, the page registers a
  `beforeunload` handler so the browser asks before a reload or close discards it, and the workspace header
  says that work is kept only in this tab until exported. The page tracks workspaces from imports, closes,
  workspace reads and "workspace not found" answers, and every workspace list replaces that set (an
  import that timed out in the page but finished in the runtime, an idle sweep). A reloaded workspace link
  shows "This workspace is gone" with that explanation. If the runtime crashes after booting (an
  Emscripten abort such as running out of memory, a WebAssembly trap or a .NET exit, reported by the
  runtime's abort hook or detected on the request that hit it), the UI says the tab's workspaces were lost,
  offers a reload and stops asking before unload. An uncaught JavaScript error the runtime survives is
  logged in the worker and does not end it. A request waiting for a boot that receives no data for 90 s
  (progress counts downloaded bytes, so one large file on a slow connection keeps it alive) fails with a
  message instead of waiting forever; reads that failed that way are fetched again once the runtime is ready.
- **Images share the single worker.** Icons and assets are fetched through the runtime, at most two at a
  time, so a user action never waits behind more than two image requests; an image request whose last user
  unmounted before it finished is aborted (dropped unstarted by the worker). Long lists load images lazily.
  Fetched images are cached by path until a change can alter them. Importing or removing a reference pack
  in another tab of the site drops cached reference assets (a tab ignores its own broadcast echo). A
  structural edit, an applied action or a revert drops its document's assets; a plain field edit keeps them,
  because asset bytes change only with the view structure. Any other request that may write (including one
  that failed without an answer) drops what it could affect, and closing a document or workspace drops
  everything below it. Images on screen then fetch the new bytes.

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
  `web/src/runtime/persistence.test.ts` models tabs sharing one IndexedDB (concurrent saves, reloads
  after another tab's save, quota failures), and `tests/SaveToolkit.LocalApi.Tests/PersistentReloadTests.cs`
  checks that reloaded stores never write a stale view back.
- The CI `e2e` job builds `web/dist` (runtime included) and runs the Playwright suites against
  `vite preview`, exercising the real WebAssembly runtime in Chromium with no API process.
