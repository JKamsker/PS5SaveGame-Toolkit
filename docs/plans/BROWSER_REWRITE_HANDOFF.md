# Browser rewrite: handoff

Branch `claude/amazing-bell-rwkytz` (based on `main` at `b03a219`), PR #86. Read
[BROWSER_RUNTIME.md](../BROWSER_RUNTIME.md) first; it is the architecture reference and is current.

## What this branch does

The toolkit runs entirely in the browser as a static site; no server is needed. It is still
plugin-based: every `plugins/<Name>/` assembly is compiled to WebAssembly and discovered at boot.

```
React UI (web/src) -> web/src/api/http.ts fetchApi -> web/src/runtime (Web Worker client)
  Web Worker: dotnet.js boots web/public/_framework, [JSExport] dispatch in src/SaveToolkit.Browser
  SaveToolkit.Browser (wasm exe) -> SaveToolkit.LocalApi.LocalApiHost.SendAsync(LocalRequest)
  SaveToolkit.LocalApi (no ASP.NET) -> Core / Sdk / Codecs / plugins (unchanged contracts)
```

- `src/SaveToolkit.LocalApi`: the whole `/api` contract in-process, with a dependency-free router,
  multipart/url-encoded/JSON-charset parsing, ETag/If-Match and problem+json. DTOs moved here from the
  Api; namespaces are unchanged. `SaveToolkit.Api` (ASP.NET) still exists, references LocalApi, and is
  the parity oracle in `tests/SaveToolkit.LocalApi.Tests`, which differentially tests the hand-written
  parsers against ASP.NET.
- `src/SaveToolkit.Browser`: the WASM host.
  - A build-time plugin manifest is generated from the same glob as the Api, and each plugin assembly
    is loaded by name.
  - `TrimMode=partial`, reflection JSON, native relink (SQLite + IDBFS).
  - Exports: `DispatchAsync` plus `TakeResponse` with framing.
  - `BrowserHost.Initialize` sets the invariant culture before any plugin loads.
- Persistence: presets and user reference packs are kept on Emscripten IDBFS, coordinated across tabs
  with Web Locks, BroadcastChannel and a generation marker, mtime marking, post-flush verification, an
  all-or-nothing flush and a reconnecting IndexedDB connection (`web/src/runtime/persistence.ts`,
  `idbfsTree.ts`, `durableRoutes.ts`). Workspaces live in tab memory, with a `beforeunload` guard.
- Boot is about 1.1 s. Embedded reference packs are validated lazily and on idle
  (`EmbeddedPackSet`), never skipped.
- `SaveToolkit.Sdk.Crypto` adds a managed MD5 (RFC 1321) and AES-ECB/CBC (FIPS-197). Browser WASM has
  neither, so five plugins use them.
- `SaveToolkit.Sdk.Compression` holds managed, bounds-checked ports of K4os 1.3.8's LZ4 HC and 64-bit
  fast encoders, byte-identical to native K4os (see open-work items 1 and 1b).
- CI and release:
  - `ci.yml` builds the static site with the `wasm-tools` workload.
  - `pages.yml` deploys the docs at the site root and the app under `/app/`, with a 404 SPA fallback.
  - `release.yml` ships `SaveToolkit-<v>-web.zip`.
- The e2e suite runs against `web/dist`. Playwright's network hooks cannot see worker traffic, so they
  are replaced by `web/e2e/apiRoutes.ts`, `apiPageHooks.ts` and `apiRequest.ts`.

## State at handoff (verified on `dc1a7640`)

| Check | Result |
| --- | --- |
| Full e2e | 152 passed, 1 skipped on `1de61815`; on `dc1a7640` the 63 affected specs passed |
| `npm run test` (web unit tests) | 1599 pass |
| `npm run typecheck` / `lint` / `check:bundle` | pass / 0 errors (46 pre-existing warnings) / pass |
| `python3 -B docs/tests/test_documentation.py --code-ref HEAD` | OK |
| `... --code-ref main` | fails only 5 checks, for files that are absent on main until this merges |
| Native: all 24 test projects | 0 failures |
| `tests/SaveToolkit.Core.Tests` | 2355 total, 22 skipped |
| `tests/SaveToolkit.LocalApi.Tests` | 319 pass |
| `tests/SaveToolkit.Codecs.Tests`, Witcher3, BaldursGate3, Cyberpunk2077, Gearbox plugin tests | 0 failures |

Every skip depends on private samples. The commit history contains many `wip: snapshot` commits
(periodic saves of parallel agents' work). Squash on merge.

## Open work: resolved

All items from the previous handoff are fixed. Repro scripts are in
[tools/browser-repros](../../tools/browser-repros/README.md).

1. **Witcher 3 WASM trap.** The Mono interpreter stores struct field offsets in 16 bits
   (dotnet/runtime#46622), so every field of K4os LZ4 HC's 256 KiB state struct past 64 KiB aliased
   the hash table and the match finder read out of bounds. Replaced by a managed, bounds-checked port in
   `src/SaveToolkit.Sdk/Compression/` (byte-identical to K4os 1.3.8). BG3 was affected too: its corrupt
   blocks were caught as 422. A trap is now always a runtime crash, never a hang (`failures.ts`,
   `client.ts`, `worker.ts`); `browser-codecs.e2e.ts` covers both games.
   - **1b. LZ4 fast level.** K4os picks its engine by pointer size, so 32-bit WASM ran the 32-bit
     engine, whose bytes differ from native for blocks of 65,547 B or more and for all frames. A managed
     port of the 64-bit fast encoder (`Lz4FastCompression`, `Lz4FastCompressionChain`) is used by
     Cyberpunk and BG3, which are now byte-identical in the browser.
2. **Gearbox `GET /changes`.** Each field registration rebuilt the whole field-metadata cache, so a
   review was O(n²). `FieldMetadataCache` (`src/SaveToolkit.Core/Editing/`) updates it incrementally:
   39.6 s to 1.1 s natively, 166.7 s to 3.2 s in WASM, with response bodies hash-identical.
3. **Culture.** `BrowserHost.Initialize` sets the invariant culture; `ff7-culture.e2e.ts` proves a
   de-DE worker now shows "9,434,752".
4. **Image cache and boot.** `web/src/lib/apiObjectUrls.ts` invalidates on every change except known
   reads, keeps assets across non-structural edits and ignores its own tab's announcements. The boot
   deadline is split (90 s only while files download, then 5 min), and the base path matches
   case-insensitively.
5. **e2e harness.** Weakening the harness is proven to fail tests; `apiRequest()` waits until the app
   is idle (including bodies still serializing); `runtime/client.ts` posts in call order (FIFO);
   `preset-tabs.e2e.ts` runs two real tabs.
6. **Review loop.** Seven rounds were run. Notable fixes they produced:
   - A download or preset capture waits for queued edits; capture re-checks the snapshot and the
     rendered document before sending row keys.
   - The `readsDurableData` route list is derived from the LocalApi route table (`durableRoutes.ts`,
     checked by a test that parses the C# endpoints), so group proposals and opens refresh packs.
   - The cross-tab lock is released when the runtime stops mid-mutation; the coordinator latches on a
     trap and never reverts or reloads into a trapped runtime.
   - The IDBFS flush is atomic (Emscripten's reconcile committed partial writes).
   - FF7 presets on an unreachable storage root answer 503 again, not 409 (a regression from
     `742ebb7`; `PresetLibraryStorage.cs`, `PresetStorageIdentity.cs`).
   - Client FIFO: a failing body, abort or crash keeps call order, settles every call and detaches its
     abort listener; a refused `postMessage` is forgotten.
   - `isRuntimeStopped` requires the runtime's wording at the start of a message, so .NET text that
     quotes save or preset content is never a trap; it and the other error predicates are total, so
     the worker queue stays alive.
   - A failed cross-tab announcement after a successful save is logged, not reported as an error.

   <!-- ROUND7_RESULT -->

## Remaining known limits and follow-ups

- The managed LZ4 ports refuse streams larger than 1 GiB; native K4os would renormalise its window.
- The `apiPageHooks.ts` `idle()` heuristic does not cover writes the app delays on purpose with a
  timer.
- The image cache's "assets change only with structure" rule relies on the SDK's `StructureChanged`
  contract being honoured by every plugin.
- The Api itself still formats with its process culture (invariant on CI); this was not changed.
- The `worker.js` bundle budget was raised to 6670 gzip bytes
  ([PERFORMANCE.md](../PERFORMANCE.md)).

## Environment notes

- You need the .NET 10 SDK plus `dotnet workload install wasm-tools` (native relink), Node 22+, and
  Chromium for Playwright. Build the site with `cd web && npm ci && npm run build`; serve it with
  `npx vite preview`. Generating the e2e fixtures needs `dotnet` on `PATH`.
- `/opt/pw-browsers` may lag the installed Playwright: 1.64.1 wants `chromium-1248`, while cloud
  containers had `chromium-1243`. Point `PLAYWRIGHT_BROWSERS_PATH` at a directory of symlinks with the
  expected names. CI installs its own browsers.
- Pass `--artifacts-path <dir>` when several builds or tests run in parallel; shared `obj/` dirs
  collide. The `wasm-tools` builds and the e2e runs are memory and disk heavy: one container ran out of
  both with five or more parallel agents.
- `tools/browser-repros` expects the preview on port 4221, which collides between parallel agents;
  give each agent its own port.
- Gotchas already solved; keep them:
  - `dotnetSidecar` must be set before booting .NET in a worker.
  - `UseSystemResourceKeys=false`, so error messages are real.
  - The browser has no `MD5` or `Aes.Create`.
  - Never block (`.Result`) on the single WASM thread.
  - Do not use K4os LZ4 encoders in plugins that run in the browser; use `SaveToolkit.Sdk.Compression`.
