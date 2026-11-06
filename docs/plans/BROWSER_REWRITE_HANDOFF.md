# Browser rewrite: handoff

Branch `claude/amazing-bell-rwkytz` (based on `main` at `b03a219`). Read
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
- Persistence: presets and user reference packs are kept on Emscripten IDBFS, coordinated across tabs
  with Web Locks, BroadcastChannel and a generation marker, mtime marking, post-flush verification and a
  reconnecting IndexedDB connection (`web/src/runtime/persistence.ts`, `idbfsTree.ts`). Workspaces live
  in tab memory, with a `beforeunload` guard.
- Boot is about 1.1 s. Embedded reference packs are validated lazily and on idle
  (`EmbeddedPackSet`), never skipped.
- `SaveToolkit.Sdk.Crypto` adds a managed MD5 (RFC 1321) and AES-ECB/CBC (FIPS-197). Browser WASM has
  neither, so five plugins use them.
- CI and release:
  - `ci.yml` builds the static site with the `wasm-tools` workload.
  - `pages.yml` deploys the docs at the site root and the app under `/app/`, with a 404 SPA fallback.
  - `release.yml` ships `SaveToolkit-<v>-web.zip`.
- The e2e suite runs against `web/dist`. Playwright's network hooks cannot see worker traffic, so they
  are replaced by `web/e2e/apiRoutes.ts`, `apiPageHooks.ts` and `apiRequest.ts`.

## State at handoff (verified on the final tree)

| Check | Result |
| --- | --- |
| `dotnet test --project tests/SaveToolkit.LocalApi.Tests` | 319/319 |
| `tests/SaveToolkit.Core.Tests` (last full run, no C# change since) | 2349: 2327 passed, 22 skipped (samples) |
| `npm run typecheck` / `lint` / `test` / `check:bundle` | pass / 0 errors (46 pre-existing warnings) / 1123 pass / pass |
| e2e `failure-*`, `smoke` on a fresh `npm run build` | 44/44 |
| Full e2e (last full run, before the round-3/4 UI and persistence fixes) | 141 passed, 1 skipped |
| `python3 -B docs/tests/test_documentation.py --code-ref HEAD` | OK. `--code-ref main` fails 5 checks until this merges (new files are absent on main) |

The commit history contains many `wip: snapshot` commits: periodic saves of parallel agents' work.
Squash on merge.

## Open work, in priority order

The adversarial review loop (review, fix, re-review) ran four rounds and had not converged. Repro
scripts for items 1 to 3 are in [tools/browser-repros](../../tools/browser-repros/README.md).

1. **CRITICAL: Witcher 3 edit, then download, export or action, traps the WASM runtime**
   (`RuntimeError: memory access out of bounds`). Afterwards every workspace request hangs until reload,
   and the native Api answers 200. The suspect is
   `LZ4Codec.Encode(..., LZ4Level.L12_MAX)` at `plugins/Witcher3/.../WitcherWriter.cs:83`. K4os HC
   unsafe code may overflow the WASM stack; test `WasmStackSize`/`EmccStackSize` or a heap-state API, and
   keep the output bytes identical. BaldursGate3 `LarianCompression.cs:69` uses the same level and is
   unverified. Audit every unsafe codec call site in WASM, and make a trap mark the runtime as crashed
   (`web/src/runtime/client.ts` `fail()`) instead of hanging. Add a browser regression test (e2e) that
   edits and exports a Witcher 3 fixture.
2. **Gearbox `GET /changes` after one edit takes 43 to 55 s natively and about 188 s in WASM**, on a
   304-byte synthetic BL3 save. The UI timeout is 60 s (`web/src/api/http.ts:11`), so the review always
   times out in the browser. Profile natively. The review walk is bounded by rows, not time
   (`src/SaveToolkit.Core/Sessions/EditHistory.cs:17-23`). Fix the root cause without changing the body;
   check other plugins' `/changes` timings.
3. **Culture**: the browser runtime formats with the browser language. In de-DE, "9.434.752" appears
   where the Api shows "9,434,752". Affected: FF7 `FinalFantasy7Document.cs:110`, SuckerPunch
   `SuckerPunchOverview.cs:28` and `YoteiDocument.cs:51`, `InsomniacDocument.cs:227`, BG3 and GoW
   stats. Likely fix: set `CultureInfo.DefaultThreadCurrentCulture` and `DefaultThreadCurrentUICulture`
   to the invariant culture in the Browser host to match the Api and the `tests/CultureMatrix.cs`
   contract. Headless Chromium always reports en-US, so e2e never covers this.
4. **Image cache invalidation** (`web/src/lib/apiObjectUrls.ts`), all verified, low severity:
   - `POST …/actions/apply` and `…/changes/revert` do not refresh document images. Invert the rule:
     every document POST invalidates except known reads.
   - Every edit refetches all visible `…/assets` images. Atomic Heart refetches 40 icons per edit.
     Invalidate lazily, or only on structural change.
   - The tab's own preset saves (a BroadcastChannel echo) refetch reference images. The message should
     say what changed and carry a tab nonce.
   - Suspected: the 90 s boot deadline can fire after downloads finish (WASM start, lock wait, store
     load); `isOutsideApp` is case-sensitive while React Router is not.
5. **e2e harness (partly done in this branch)**:
   - Done: `FailureMonitor` now fails on unexpected worker API errors, a real runtime-crash e2e test
     exists (`failure-document.e2e.ts`, using `crashRuntime`), and cancels are forwarded to the worker.
   - Still to do:
     - Prove with mutations that both weakenings are now caught: a stray failing request in
       `client.ts workspace()`, and `fail()` no longer rejecting pending requests.
     - Make `apiRequest()` reads ordered behind the app's queued requests (`proveUnchangedDraft`,
       `actions.e2e.ts:179`).
     - Add one real two-tab preset-recovery e2e test.
     - Rerun the full suite.
6. **Interrupted review**: round 4 of the persistence and runtime review was stopped unfinished. Rerun
   it on the current code, then continue the loop until a round finds nothing verified.

## Environment notes

- You need the .NET 10 SDK plus `dotnet workload install wasm-tools` (native relink), Node 22+, and
  Chromium for Playwright. Build the site with `cd web && npm ci && npm run build`; serve it with
  `npx vite preview`.
- Pass `--artifacts-path <dir>` when several builds or tests run in parallel; shared `obj/` dirs
  collide. The `wasm-tools` builds and the e2e runs are memory and disk heavy: one container ran out of
  both with five or more parallel agents.
- Gotchas already solved; keep them:
  - `dotnetSidecar` must be set before booting .NET in a worker.
  - `UseSystemResourceKeys=false`, so error messages are real.
  - The browser has no `MD5` or `Aes.Create`.
  - Never block (`.Result`) on the single WASM thread.
