# Browser runtime repro scripts

Throwaway differential repros from the browser-rewrite review (see
[the handoff](../../docs/plans/BROWSER_REWRITE_HANDOFF.md)). Each script sends the same requests to the
native ASP.NET Api and to the in-browser WebAssembly runtime (driven through Playwright) and prints both
outcomes. They are diagnostic tools, not tests.

Setup: `cd web && npm ci && npm run build && npx vite preview --port 4221` (the scripts expect the
static site on 4221) and, in another shell, the native Api on the port passed as the first argument:
`dotnet run --project src/SaveToolkit.Api --urls http://127.0.0.1:5233`.

| Script | Shows |
| --- | --- |
| `w3-oob-crash.cjs <apiPort> [steps]` | Witcher 3 edit then action/download traps the WASM runtime (`memory access out of bounds`); native answers 200 |
| `w3-download.cjs <apiPort>` | Same crash via `GET /download` after one edit |
| `gearbox-changes-perf.cjs <apiPort>` | `GET /changes` after one edit: ~45 s native, ~190 s WASM (UI timeout 60 s) |
| `culture-n0.cjs <apiPort> de-DE <zip>` | Browser-language number formatting differs from the Api's invariant output |

Save fixtures are **not committed** (no save data in the repo). The scripts read
`w3/Manual.sav` and `gearbox-fixture/PPSA01463/sdimg_TEST/{1,profile}` next to themselves: produce
them from the synthetic saves the Witcher3 and Gearbox plugin test suites build in code (for example
by writing the bytes a test passes to `MemorySaveSet` to disk), and keep them git-ignored.
