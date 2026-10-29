# Performance baselines

## Web bundle budgets

Measured **2026-10-29** on Windows, Node **24.19.0**, npm **11.17.0**, Vite **8.3.5** and the
checked-in lockfile. Before: `42236a9a`; after: `feat/web-bundle-budget` editor split.
Reproduce from `web/` after installing dependencies, with the
[scratch environment](RELEASING.md#disk-and-scratch) configured:

```powershell
npm run build
npm run check:bundle
```

Vite's production output is `src/SaveToolkit.Api/wwwroot`, not `web/dist`.
The check resolves that path from Vite config and reads `.vite/manifest.json` plus actual emitted
files. All numbers below are **bytes**, with **gzip level 6** from Node `zlib`, measured separately
per file; sums represent independently compressed HTTP resources, not a single combined archive.
Vite 8's console gzip estimates use a different compressor and can differ slightly (the original
console reported 119.96 kB for initial JS; this reproducible check measures 118.70 kB).
Raw/gzip sizes describe transfer payloads, excluding HTTP headers and execution/parse time.

### Before and after

Hashes are omitted because they change with imports. Workspace/editor styles move to the
DocumentEditor CSS chunk without CSS source changes.

| Chunk | Before raw | Before gzip | After raw | After gzip |
| --- | ---: | ---: | ---: | ---: |
| index.js | 379931 | 118695 | 379762 | 118587 |
| index.css | 37478 | 9943 | 37478 | 9943 |
| WorkspacePage.js | 156784 | 45486 | 14267 | 5131 |
| DocumentEditor.js (new split) | — | — | 143127 | 41652 |
| PresetsPanel.js (lazy preset flow) | — | — | 16081 | 5249 |
| PresetsPanel.css (lazy preset flow) | — | — | 1446 | 523 |
| documentStore.js (shared lazy dependency) | — | — | 2802 | 1015 |
| values.js (shared initial dependency) | — | — | 13967 | 5391 |
| Workspace/editor CSS | 5131 | 1504 | 5131 | 1504 |
| GraphView.js | 4146 | 1748 | 4142 | 1747 |
| MapView.js | 5133 | 2127 | 5134 | 2129 |
| SpatialShared.js | 4660 | 1785 | 4661 | 1788 |
| SpatialShared.css | 2763 | 911 | 2763 | 911 |

**Loading graph:**

- `/` loads index JS and CSS: **118587 / 9943 gzip bytes** after the change. React DOM,
  React Router and TanStack Query are the largest initial dependencies. Initial JS remains well
  under the ~300 kB target and CSS under ~50 kB (decimal kB).
- `/w/:workspaceId` adds WorkspacePage JS: **5131 gzip bytes**. Before, it also fetched the
  editor, tables, virtualizer and reference-picker code immediately (**45486 gzip bytes**).
  JS for home plus the empty workspace falls from **164181 to 123718 gzip bytes** (24.6%).
- Selecting a document, or opening a `?doc=...` deep link, additionally loads DocumentEditor JS
  (**42271 gzip bytes**) and its CSS (**1504**). The preset leaf is loaded as a separate
  **5249-byte** JS/**523-byte** CSS chunk behind the same accessible Suspense status. The existing
  Opening status covers the editor code load.
  Eager/queryable tables, hex virtualization, the reference picker and TanStack Virtual stay together
  in this chunk; they are deferred with the editor, not independently split. Their recursive renderer
  and shared field/state imports make further boundaries a less clear safe win at this size.
- GraphView and MapView already use independent `React.lazy` imports and Suspense, plus spatial
  shared JS/CSS. They load only when the corresponding view renders.
- Reference icons are fetched from document reference-asset API URLs when picker candidate/preview
  images mount. Table/document images use native lazy image loading. No game icon/catalog packs
  are embedded in the web JS; opening a file does not fetch all reference icons.
- Seven unchanged WOFF2 font subsets total **151892 raw bytes**; CSS unicode ranges select the
  subsets needed by page text. Fonts/images/API payloads are not part of the JS/CSS gate.

All JS chunks together grow from **169841 to 171034 gzip bytes** (0.7%) because separate compressed
resources and dynamic-import glue add overhead. This trades a small total increase for a much
smaller file-picker transfer. No UI design, session, mutation or export behavior changes.

**Dependency inspection:** a local uncommitted Vite `generateBundle` analysis and
`npm ls --omit=dev --all` found one version/copy of each runtime package, shared across chunks.
Before final minification, rendered module contributions were approximately React DOM 537 kB,
React Router 213 kB, Query core/bindings 76 kB and Virtual core/bindings 60 kB. These module
lengths are attribution hints, not additive compressed transfer measurements. No dead runtime
dependency or unexpectedly retained barrel import was confirmed. `fflate` is used by e2e fixtures
only and is absent from production chunks. The analysis used existing Vite APIs on D: scratch;
no visualizer, analysis artifact or dependency was added to the repository.

The corrected preset foundation plus transfer composition was remeasured on 2026-10-29:
initial JS 120129 bytes, PresetsPanel JS 5655 bytes and CSS 523 bytes, all within the
existing unchanged budgets. The transfer toolbar is supplied through the panel's child
slot from the already-lazy DocumentEditor chunk (42628 bytes), preserving room for the
foundation's compatibility disclosure and capture controls without adding chunks.
Transfer dialogs use server-owned compatibility and preview-capability summaries.
ImportPresetDialog is 2348 bytes, including explicit
reload-and-repreview recovery, the server's capture-only limitation and memoized conflict
rows so changing one choice does not rerender every record. Its budget is
2600 bytes (10.7% headroom), replacing the earlier 2370-byte limit measured before
those required recovery/capability states and full-library interaction checks. Initial and PresetsPanel limits stay unchanged. The shared transfer
request helper includes the document compatibility endpoint (TransferDialog chunk
932 bytes); its budget remains 1030 bytes. Export is 1125 bytes.
Transfer measurements below reflect this combined source;
other rows preserve the original split baseline. No runtime dependency was added.

The published R6 foundation plus transfer composition was remeasured on 2026-10-30.
The added readiness/boundary disclosure and library filters initially produced
6261 bytes of panel JavaScript and 582 bytes of CSS, exceeding the unchanged
5800/580 limits. Existing saved-row rendering and its styles now form a small lazy
`PresetRows` leaf (1211 bytes JavaScript, 289 bytes CSS), with explicit 1350/320
budgets. Filter state, loading/error handling and transfer controls stay mounted in
the existing panel; row loading has an accessible status. The resulting panel is
5751 bytes JavaScript and 419 bytes CSS; initial JavaScript is 120132 bytes.
Initial, editor, panel and existing transfer limits remain unchanged.

### Enforced gzip limits

`web/bundle-budgets.json` owns these limits, with approximately 10% headroom over the after result.
The initial totals traverse every static entry import and associated CSS, counting shared files
once and excluding dynamic imports. Each emitted JS/CSS file, including lazy chunks, also has
its own limit keyed by Vite's stable chunk name (without a content hash). A new chunk needs an
explicit reviewed budget; it cannot silently escape the check. Missing output, missing import
targets and untracked emitted JS/CSS fail rather than reporting zero. Builds keep Vite's empty
output-directory behavior, so the check never removes or rewrites output.

| Metric | Measured gzip bytes | Budget bytes |
| --- | ---: | ---: |
| Initial JS | 118587 | 130500 |
| Initial CSS | 9943 | 11000 |
| index.js | 118587 | 130500 |
| index.css | 9943 | 11000 |
| WorkspacePage.js | 5131 | 5700 |
| DocumentEditor.js | 41652 | 45900 |
| DocumentEditor.css | 1504 | 1660 |
| PresetsPanel.js | 5249 | 5800 |
| PresetsPanel.css | 523 | 580 |
| PresetRows.js | 1211 | 1350 |
| PresetRows.css | 289 | 320 |
| ImportPresetDialog.js | 2348 | 2600 |
| ExportPresetDialog.js | 1125 | 1250 |
| TransferDialog.js | 932 | 1030 |
| TransferDialog.css | 473 | 520 |
| jsx-runtime.js | 3328 | 3670 |
| documentStore.js | 1015 | 1130 |
| values.js | 5391 | 6000 |
| GraphView.js | 1747 | 1930 |
| MapView.js | 2129 | 2350 |
| SpatialShared.js | 1788 | 1970 |
| SpatialShared.css | 911 | 1010 |

The preset-transfer follow-up adds explicit lazy import/export dialogs and shared native-modal
code/styles. These new chunks load only on the corresponding toolbar action; the existing
PresetsPanel CSS and all existing limits stay unchanged. Vite extracts a shared JSX runtime
initial dependency (3328 gzip bytes), with measured aggregate initial JS around 120.1 kB,
still within its existing 130500-byte limit. New transfer limits retain approximately 10%
headroom; they do not exempt any emitted chunk from checking.

CI `validate` runs `npm run check:bundle` immediately after its production build on **both Ubuntu
and Windows**. Synthetic Vitest regressions cover shared imports/cycles, lazy JS/CSS, aggregate
and individual failures, exact limits, new chunks, missing builds/assets/imports and untracked
output. A built-app Playwright regression checks that the file picker does not download editor or
spatial chunks, holds the editor download to exercise Opening, then edits and reloads a deep link.
After intentional output/dependency changes, review the loading graph and remeasure before updating
both JSON limits and this table with a reason; see the [release checks](RELEASING.md#continuous-integration).

## Backend benchmarks

The opt-in `tests/benchmarks/SaveToolkit.Benchmarks` executable measures Core startup and
GenericJson/XorJson workflows with [BenchmarkDotNet](https://benchmarkdotnet.org/), plus a
separate fresh-process startup/idle observation mode.
It has no test adapter, is explicitly not a test project, and is not discovered by
`scripts/test.ps1` (which selects `*.Tests.csproj`), `dotnet test`, or CI Validate.
The solution includes it for normal builds; benchmarking requires an explicit run.
The workflow cases read no private samples, sample loaders, network data, reference packs or user
workspaces. Startup cases discover the shipped plugins and load their embedded reference packs;
all imported saves are authored synthetic data. Startup uses fresh, empty storage roots.

## Run

Use PowerShell 7 and the .NET SDK selected by `global.json`. Run from the checkout:

```powershell
$scratch = 'D:/Temp/saveeditor/test-scratch/benchmarks'
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
./scripts/bench.ps1 -ScratchDirectory $scratch
./scripts/bench.ps1 -ScratchDirectory $scratch -Job Full -Filter '*PageTable*'
```

On other systems, choose a writable local scratch directory. Without `-ScratchDirectory`,
the script uses `SAVETOOLKIT_TEST_TEMP`, or `SaveToolkit.Benchmarks` under system temp.
Follow the [disk and scratch guidance](RELEASING.md#disk-and-scratch): on the owner's workstation
use D:, including TEMP/TMP/TMPDIR. The runner creates a unique `bench-<id>` under the chosen
directory and redirects those variables for restore, build and child benchmark processes.
It restores the caller's environment and location even on failure.

`-Job Quick` (default) selects ShortRun: one launch, three warmups and three measured iterations.
`-Job Full` selects BenchmarkDotNet's adaptive default job. `-Filter` is a BenchmarkDotNet wildcard
over benchmark names. The suite covers 36 workflow combinations (six operations, three sizes,
two formats) and two catalog/reference cases; allow several minutes. Full is appropriate before
drawing performance conclusions.

Reports, logs and `summary.md` remain under the unique run directory; invocation-owned synthetic
blobs, file spools and redirected temp are removed. BenchmarkDotNet's generated build files use
normal project `bin/`/`obj/`. Delete only known run directories after reviewing their results.
A failed workload/build or empty selection fails the script and does not publish a success summary.

To run just the six synthetic fixture checks without timing, configure the same scratch environment
and invoke the executable's verification mode:

```powershell
$env:TEMP = $env:TMP = $env:TMPDIR = $scratch
$env:SAVETOOLKIT_BENCHMARK_SCRATCH = Join-Path $scratch 'verify'
dotnet run --project tests/benchmarks/SaveToolkit.Benchmarks --configuration Release -- --verify
```

## Workloads and measurement boundaries

Each save contains the same authored JSON array `[0,1,...]`, encoded as UTF-8 for GenericJson
or XOR-1 bytes for XorJson. ZIP entry names, ordering, timestamps and content are deterministic.
Each save has its own directory under a synthetic title; setup creates all inputs outside timing.

| Size | Saves in ZIP (N) | Scalar rows per save |
| --- | ---: | ---: |
| Small | 2 | 256 |
| Medium | 16 | 2,000 |
| Large | 64 | 20,000 |

| Method | Timed work | Setup/reset outside timing |
| --- | --- | --- |
| `ImportZip` | Import an in-memory ZIP to disk, including decompression, CRC/hash checks, blobs and save-set construction. | Generate/compress ZIP; choose fresh destination; delete imported blobs afterwards. No plugin probing/opening is timed here. |
| `OpenDocument` | Read one disk-backed save, plugin parse/open, session creation, first view/index, session disposal. | Import the workspace; no workspace session-cache hit. Disk reads generally use the OS cache. |
| `PageTable` | Query one middle page of 50 rows in native order through the session, including validation and binding registration. | Open session, warm page/cache once; repeated requests use the same page. This measures warm paging, not filtered/sorted-query latency or the whole table. |
| `SemanticReview` | First `GetChanges()` on the current staged session, including F8 traversal and semantic comparison. | Fresh session/view and first-row binding each iteration; stage one edit (first scalar to -1), capturing the original snapshot before timing. |
| `ApplyUndo` | Apply one scalar edit and undo it through the session, including first-edit original capture and history. | Fresh session/view and first-row binding each iteration; dispose afterwards, so history cannot accumulate. |
| `ExportSpooled` | Plan whole-workspace export, serialize/verify one dirty document, compress ZIP to a FileStream, and close it. | Import workspace and stage one edit; repeat into the same spool path with FileMode.Create. No HTTP transfer or durable fsync is measured. |

Iteration setup/cleanup forces one invocation per iteration for import/review/edit, preventing
state reuse and unbounded disk/history growth. These short operations can produce minimum-iteration
warnings and wide error margins; they are useful coarse baselines, not precise microsecond claims.
Other methods use BenchmarkDotNet's normal invocation calibration. Setup also verifies probe/import
counts, page size, the single semantic change, exact undo bytes and the edited ZIP payload/count.

Default production [semantic-review budgets](PLUGIN_GUIDE.md#pagedqueryable-collections) remain enabled.
Large F8 review is intentionally incomplete because of the row traversal budget; the staged early
scalar is still reported. Its result must not be interpreted as a complete scan of 20,000 rows.
Medium and Small fit the row budget.
These workloads exercise raw format paths; they do not represent title-specific adapters or
gameplay validation. Synthetic repeated data is highly compressible, so import/export figures
are not estimates for real save collections.

## Read results

The [BenchmarkDotNet reports](https://benchmarkdotnet.org/articles/configs/exporters.html)
include Mean (time per operation), Error (half the 99.9% confidence interval), StdDev and Allocated
(managed bytes per operation from MemoryDiagnoser). GC columns show collections per 1,000 operations.
Allocated is not peak process memory, native allocation, disk volume or retained heap.
Do not add setup allocations to the measured values or compare them with first-open measurements
from a different boundary. The existing `AllocationProbe` tests measure minimum allocation samples
with their own setup/cleanup rules; this suite complements those regression assertions.

Compare the same method/size/format on the same machine, SDK/runtime, build and storage, with
other work stopped. Record commit, OS, CPU, GC, power mode and job. Large error bars or timing
changes without allocation changes need repeated Full runs before attributing a regression.
This baseline has no pass/fail thresholds and does not gate CI. Investigate unexpected scaling
and report reproducible performance findings on tracking issue #40; do not change production code
merely to improve these numbers.

## F8 semantic-review measurements

The separate [semantic-review performance report](../tests/SaveToolkit.Api.Performance.Tests/SEMANTIC_REVIEW.md)
owns the dated real-save measurements, diagnostic projections, allocation reductions and reproduction
commands. `SemanticReviewMeasurements` runs in the normal API performance test project; its real-save
cases skip without the external samples. These synchronous session measurements exclude HTTP/JSON
transport and use a different boundary from the opt-in benchmarks above.

Read `complete` and warnings with every result. The [review traversal contract](PLUGIN_GUIDE.md#pagedqueryable-collections)
defines the limits; an empty partial log does not prove an unchanged save. Full plugin views can
exhaust budgets before any F8 page is visited. The report labels its reduced F8 projections explicitly;
they are diagnostic fixtures, and their allocation savings do not establish a universal timing speedup.

## First local baseline

Captured **2026-10-29**, branch `feat/benchmarks-baseline`, based on `86e8a4d3` plus this task's
benchmark sources. Windows 11 Pro 25H2 (10.0.26200.9168), **AMD Ryzen 9 9955HX, 16 cores / 32 threads**,
.NET SDK 10.0.400, .NET 10.0.11 x64 RyuJIT, concurrent workstation GC, BenchmarkDotNet 0.15.8,
High performance power plan, D: scratch. Quick/ShortRun completed **36/36** cases in **3m39s**.
This task did not run builds or tests concurrently with timing; other machine activity was uncontrolled.

Each result below is **mean milliseconds / managed KiB allocated per operation**. Numbers are
**indicative**, specific to this machine and workload. Raw Markdown/CSV/HTML reports and the
error/standard-deviation columns are retained in the runner's artifacts directory.

| Method | Format | Small | Medium | Large |
| --- | --- | ---: | ---: | ---: |
| `ImportZip` | GenericJson | 1.070 / 25.1 | 4.311 / 84.0 | 46.912 / 577.5 |
| `ImportZip` | XorJson | 1.270 / 25.1 | 6.399 / 84.0 | 46.030 / 580.5 |
| `OpenDocument` | GenericJson | 0.201 / 194.7 | 3.076 / 1,425.6 | 30.500 / 14,382.8 |
| `OpenDocument` | XorJson | 0.115 / 142.4 | 0.885 / 1,050.1 | 26.207 / 10,952.2 |
| `PageTable` | GenericJson | 0.042 / 104.6 | 0.037 / 105.0 | 0.083 / 105.8 |
| `PageTable` | XorJson | 0.054 / 162.9 | 0.085 / 164.8 | 0.079 / 165.6 |
| `SemanticReview` | GenericJson | 5.759 / 2,517.6 | 75.547 / 20,115.9 | 143.045 / 59,434.0 |
| `SemanticReview` | XorJson | 4.859 / 1,818.7 | 65.151 / 15,111.0 | 79.844 / 50,919.7 |
| `ApplyUndo` | GenericJson | 6.087 / 2,691.3 | 83.562 / 21,436.1 | 142.334 / 71,645.5 |
| `ApplyUndo` | XorJson | 11.831 / 2,642.7 | 62.929 / 20,629.3 | 265.249 / 107,460.3 |
| `ExportSpooled` | GenericJson | 0.352 / 209.0 | 6.396 / 1,517.9 | 52.933 / 15,005.7 |
| `ExportSpooled` | XorJson | 0.604 / 457.9 | 5.311 / 3,426.0 | 143.972 / 35,338.5 |

The three-sample confidence intervals are often wider than the means. For example, Small/XorJson
ApplyUndo had an 11.831 ms mean and a 6.779 ms median. Use Full and repeated runs before interpreting
small timing differences. Large review is budget-limited as described above.

An allocation profiling candidate is the first scalar edit/undo on Large/XorJson: approximately
105 MiB allocated for one 106 KiB input, including original semantic capture. Large/GenericJson
uses about 70 MiB at that boundary. This is a measured hotspot, not evidence of a regression or a
correctness defect; production profiling/changes are deferred to #40 outside this benchmark task.
A separate targeted Quick rerun allocated 104.3 MiB for Large/XorJson ApplyUndo, confirming the
allocation magnitude; its 313.5 ms mean still had a very wide 1,433.1 ms error interval.

## Startup and idle cost (#40)

Captured **2026-10-29** on `review/perf-startup`, baseline `2b15e8e8`, with the catalog change
in `0dbd2269`. Same Windows/CPU/SDK/runtime/power plan as above. The API uses server GC;
the console measurement worker uses workstation GC. There are **17 plugins plus `builtin.raw`**.
All saves are synthetic. No private samples are loaded by these measurements.

Run the separate observation mode after building Release (no BenchmarkDotNet warmup/calibration):

```powershell
$scratch = 'D:/Temp/saveeditor/test-scratch/startup-observation'
New-Item -ItemType Directory -Force $scratch | Out-Null
$env:TEMP = $env:TMP = $env:TMPDIR = $scratch
dotnet build tests/benchmarks/SaveToolkit.Benchmarks -c Release --warnaserror
dotnet tests/benchmarks/SaveToolkit.Benchmarks/bin/Release/net10.0/SaveToolkit.Benchmarks.dll `
  --startup src/SaveToolkit.Api/bin/Release/net10.0/SaveToolkit.Api.dll $scratch
```

This starts five fresh worker/API pairs, with a new empty workspace/pack root per API. Use a new
scratch directory each run. `startup.json` retains every observation and API logs remain beside it.
It requires loopback HTTP, not Internet access. `--startup-worker <scratch>` is the internal child
mode. Run serially with other tests/builds stopped. No production instrumentation/endpoints are added.

The API stopwatch begins immediately before `Process.Start` and ends after the complete successful
`GET /api/plugins` response, including JSON validation and all plugins. Polling adds up to a 10 ms
delay plus connection overhead. These are fresh-process starts with an uncontrolled OS file cache,
**not** a reboot/disk-cold guarantee. This measures the framework-dependent Release DLL, without
`dotnet run`, compilation, UI loading, or browser startup. Before/after binaries were copied to
separate sibling D: directories. The build-output API has no copied wwwroot and logs that warning
in both phases; the packaged UI is validated separately by the bundle smoke.

The worker separately times discovery/constructors, then each first probe of a disk-backed
Small/GenericJson save, then construction of a new embedded `ReferencePackStore`. Synthetic input
generation/import and full collections are outside those timing/allocation intervals. Retained pack
heap is `GC.GetTotalMemory(true)` after minus before construction, with the store kept alive. It is
an estimate of live managed objects, not working set. The second discovery is explicitly warm.

Five observations per phase below: **median [minimum, maximum]**. Timings are milliseconds;
allocations are managed bytes on the synchronous measuring thread. No test/build ran alongside
these process observations; unrelated machine activity was uncontrolled.

| Boundary | Before | After |
| --- | ---: | ---: |
| Process start → `/api/plugins`, ms | 1,934 [1,710, 2,409] | 1,746 [1,199, 3,345] |
| First catalog discovery, ms | 66.64 [42.12, 2,981.10] | 58.42 [48.40, 2,090.82] |
| First catalog discovery, B | 618,888 | 400,192 [394,000, 400,192] |
| Warm discovery, ms | 4.36 [2.97, 11.87] | 2.99 [2.59, 3.17] |
| Warm discovery, B | 399,872 [399,872, 400,024] | 175,832 |
| All embedded packs, ms | 978.76 [771.51, 1,676.76] | 918.25 [732.96, 985.72] |
| All embedded packs, allocated MiB | 388.70 [379.08, 389.02] | 388.67 [379.69, 388.69] |
| All embedded packs, retained managed MiB | 152.13 [152.09, 152.13] | 152.13 [152.09, 152.13] |

Caching `AssemblyName` only within discovery removes repeated `Assembly.GetName()` allocations
while retaining live assembly enumeration, `ReferenceMatchesDefinition`, duplicate detection and
fresh plugin instances. First discovery allocates about **35% less**, warm discovery **56% less**
in the standalone worker. Startup latency is too noisy to attribute a speedup, and there is no
demonstrated reduction in idle memory. The large first-discovery outliers are retained in the report.

### Reference loading and deferred work

`Program.cs` registers a singleton store; resolving the hosted workspace sweeper resolves its
workspace/session dependencies, constructing the catalog and reference store before the server is
ready. `ReferencePackStore.LoadEmbedded` loads **all 40 packs**, **175 datasets**, and **105,084 items**
(records plus assets), including unused titles. Hash checking, record parsing, key/category indexes
and asset bytes are eager. They remain reachable through the singleton for the process lifetime.
Only sorted browse indexes in the SDK's decoded catalogs are lazy. Pinning a document shares packs;
closing the document does not evict them. Measurements have no stored user packs.

**Medium, deferred:** lazy records/assets could avoid much of the roughly 152 MiB retained heap
and startup decode work. This needs an explicit design for eager rejection/load errors, exact
listing counts, cross-dataset asset validation, custom limits, concurrent first use and immutable
document snapshots. The decoder is shared with SDK direct-open contexts. Changing those contracts
or moving errors to first access exceeds this task's behavior-preserving Core loading scope.
No lazy-loading or global-cache change is made here.

### First probes

The shared JSON probe medians below come from the five **after** workers. Only GenericJson and raw
match; all other entries measure first-use rejection of that same file, not their accepted formats.
Accepted-probe medians come from five fresh, separately filtered Release test processes:

```powershell
# Set TEMP/TMP/TMPDIR and SAVETOOLKIT_TEST_TEMP to D: as above.
$env:SAVETOOLKIT_STARTUP_PROBES = "$scratch/accepted-probes.csv"
dotnet test --project tests/SaveToolkit.Core.Tests -c Release `
  --filter-class '*StartupProbeMeasurements' --output Detailed
```

The test selects each plugin's first authored conformance mutation seed, imports it before timing,
and asserts acceptance. Fixture generation may initialize shared format helpers; this is the first
`Probe` invocation after fixture construction, not a wholly cold parser/JIT guarantee. It never
calls the sample loaders. Times include disk stream creation/first read (usually OS-cached).
There are no latency assertions. Values are **median ms / allocated B**; probe code is unchanged.

| Plugin | Shared JSON | Accepted synthetic seed |
| --- | ---: | ---: |
| builtin.raw | 1.590 / 83,968 | Same shared JSON |
| astro-playroom | 0.137 / 64 | 1.696 / 83,536 |
| atomic-heart | 0.342 / 83,160 | 2.532 / 83,648 |
| bg3 | 0.233 / 32 | 1.417 / 82,592 |
| cyberpunk2077 | 0.240 / 82,272 | 1.097 / 82,528 |
| eldenring | 0.231 / 56 | 7.457 / 82,728 |
| ffxvi | 0.112 / 32 | 9.129 / 82,512 |
| ff7r | 0.138 / 32 | 8.686 / 82,624 |
| gearbox | 0.116 / 32 | 2.151 / 82,656 |
| format.json | 2.911 / 83,448 | 10.974 / 83,080 |
| gow-ragnarok | 0.352 / 160 | 1.609 / 82,880 |
| insomniac | 0.425 / 82,256 | 1.431 / 82,512 |
| pragmata | 0.108 / 32 | 15.076 / 82,512 |
| suckerpunch | 0.128 / 32 | 7.079 / 82,560 |
| tales-of-arise | 0.066 / 32 | 8.667 / 205,648 |
| engine.unreal-gvas | 0.773 / 82,216 | 11.231 / 83,224 |
| witcher3 | 0.120 / 32 | 1.900 / 82,504 |
| format.json-xor | 0.620 / 82,352 | 61.380 / 437,800 |

**Low, deferred profiling candidate:** XorJson's accepted first use allocates 428 KiB versus roughly
81 KiB for most accepted probes. The cause is not established by allocation totals. It is outside
Core catalog/reference loading; no plugin change is proposed from these coarse timings. JSON's
rejection path does not reveal that cost.

### Idle and repeated lifecycle

Each API snapshot waits 500 ms without requests. No target GC or working-set trim is forced. Each
cycle imports the Small ZIP (two saves, 256 scalars each), explicitly opens the first with GenericJson
(including its first view/HTTP DTO), closes the document and deletes the workspace. After the first
cycle, the harness repeats the same cycle **20 additional times**, snapshotting after each and
requiring the final workspace list to be empty. Memory is **MiB**, median [min, max] across five APIs.

| API idle point | Before working set | After working set | Before private bytes | After private bytes |
| --- | ---: | ---: | ---: | ---: |
| Startup | 350.4 [347.5, 361.1] | 358.4 [348.8, 366.6] | 266.7 [260.0, 317.3] | 274.4 [265.2, 295.9] |
| One cycle | 360.5 [358.3, 373.0] | 367.4 [357.9, 376.7] | 280.8 [263.1, 323.0] | 289.6 [268.4, 300.4] |
| Another 20 cycles | 384.6 [380.1, 404.6] | 389.9 [381.5, 406.0] | 302.8 [279.6, 349.3] | 310.0 [301.1, 325.3] |

Working set grows after the warmup cycle by about 23–24 MiB at the median; these short observations
do not distinguish runtime/JIT/buffer/GC high-water retention from a leak. There is no defensible
working-set CI threshold. `StartupLifecycleAllocationTests` instead keeps the catalog/reference store
alive and proves all 20 removed workspace/session pairs collectible after full GC, with no registered
workspaces or remaining blob directories. Its smaller four-scalar fixture allocates **831,840 B per
20 cycles**, below a **2 MiB** budget. `PluginDiscoveryAllocationTests` fell from **2,152,568 B** before
the fix to about **356,552 B** in the richer test host, below **512 KiB**. Both use `AllocationProbe`:
Release enforces bounds; Debug reports. Collectibility/behavior checks run in both configurations.
This bounds the tested Core lifetime, not every plugin or all possible HTTP/native retention.

### Warm BenchmarkDotNet cases

`CatalogStartupBenchmarks.Discover` measures warmed discovery/constructors with all plugins.
`LoadEmbeddedPacks` constructs a fresh store with all shipped packs; discovery is setup and there
is one invocation per iteration to bound transient memory. These are warm-JIT/OS-cache operations,
not substitutes for the fresh-process measurements. Run explicitly with:

```powershell
./scripts/bench.ps1 -ScratchDirectory $scratch -Filter '*CatalogStartupBenchmarks*'
```

The local Quick/ShortRun completed **2/2** cases in 55 seconds: discovery **2.920 ms / 170.1 KiB**,
pack loading **842.822 ms / 379.0 MiB** allocated. The 99.9% error half-widths were respectively
3.286 ms and 686.422 ms; these timings are coarse. Repeated pack-load allocation differs from
first use; these totals do not identify the cause. Compare the same boundary.

### Packaged win-x64 size and trimming candidates

The official `scripts/package.ps1` produced `SaveToolkit-0.1.0-rc.2.42-win-x64.zip` at `ffebb3e5`:
**69,474,718 bytes (66.26 MiB)** compressed; **185,340,676 bytes (176.75 MiB)** expanded,
**432 files**. This is a descriptive after-change bundle measurement, not a before/after size claim.
The unchanged web sources were built on D: and their generated wwwroot linked for packaging;
`-SkipWebBuild` reused that build. Publish settings remain self-contained, untrimmed, multiple files.

| Contents | Expanded MiB | ZIP payload MiB |
| --- | ---: | ---: |
| Runtime, third-party dependencies, licences and other files | 107.50 | 48.54 |
| 17 plugin DLLs, including embedded reference packs | 64.41 | 15.06 |
| Native SQLite | 1.89 | 1.05 |
| Other toolkit DLLs | 0.96 | 0.35 |
| PDB symbols | 0.96 | 0.53 |
| Built UI | 1.04 | 0.65 |

ZIP payload sums exclude archive headers. The largest plugin is Gearbox at 22.80 MiB expanded;
its DLL compresses to 2.12 MiB. DLL size includes code and resources, not a measured asset-only total.

**Documented only:**

- Separating symbols could save about 0.53 MiB compressed; retain a separate symbol artifact for
  diagnostics. No symbol/publish change is made for this small saving.
- A separate framework-dependent distribution could omit the bundled runtime, but would require
  users to install the matching ASP.NET runtime. The 107.50 MiB group includes other dependencies
  and notices, so it is not an estimate of achievable runtime savings.
- Optional reference-pack distributions/resource compaction merit separate investigation; removing
  packs would change available names/icons/provenance. IL trimming alone does not make the eager
  decoded objects lazy. Preserve manifests, hashes and attribution in any future format change.
- General IL trimming needs explicit roots/contracts for reflection-based plugin discovery and
  activation, JSON metadata and native codec/SQLite paths, plus all-plugin conformance and built-app
  smoke coverage. File size does not prove an assembly unused. Do not remove runtime DLLs by hand.

The official win-x64 checksum/UI/JavaScript smoke passes. All three optional native apphost
configuration tests also pass (appsettings, environment, command-line overrides), including synthetic
workspace and user-pack import. No publish settings were changed.
The native API also reports **18 catalog entries, 40 packs and zero pack load errors**.

### Validation and retained observations

The API Release build has **0 warnings / 0 errors**. The full `scripts/test.ps1 -Configuration Release`
run with external private samples passes **21 projects: 7,663 passed, 0 failed, 9 skipped**. These
existing skips are four inherited synthetic-only conformance sample cases, the opt-in pack-hash
printer, the known backup-twin storage bug, and three optional native bundle cases. The latter three
were subsequently run against this package: **3 passed, 0 skipped**. No skip was introduced here;
the generic zero-skip target cannot be met without changing those existing out-of-scope/opt-in tests.
Both new allocation tests pass in Debug with report-only bounds. All six original benchmark fixture
checks and both new BDN cases pass. Documentation checks against this branch's `HEAD`: **40 passed**.
Against moving local `main`: **39 passed / 1 failed**, because its newer script declares
`-MaxParallel` absent from this checkout's unchanged `docs/RELEASING.md` parameter inventory.

Raw process observations, five accepted-probe CSVs, BDN reports, package inventory, smoke logs and
TRX results remain under `D:/Temp/saveeditor/test-scratch/review-perf-startup`. Private samples were
read in place only by the required test suite; none were used in the benchmarks or committed.

## Lazy embedded reference packs (#40)

The earlier deferred reference-loading finding is addressed on `perf/lazy-reference-packs`,
against baseline `9be32cd8` (including the Release runner fix `d11e997c`). Measurements use the same
Windows/CPU/.NET/High performance configuration described above, workstation GC for workers and
server GC for the API. All 40 shipped packs and their attribution/content remain present.

Startup still validates every manifest, file hash, records schema, image and cross-dataset asset
reference. Rejected-pack reporting and exact dataset counts therefore keep their existing behavior.
Validation releases its temporary JSON models and image buffers. Each requested records dataset
then creates its catalog once; individual images copy their resource bytes only when requested.
`Lazy<T>` publishes these results across concurrent callers. Pack selection and pinning inspect
metadata without materializing a losing candidate. User ZIP imports retain their existing complete
snapshot because their disposable archive source cannot be reopened later.

The full Release mutation-fuzz suites also exercise the first document open and enforce their
existing allocation budgets. They caught excessive first-use allocation when lazy catalog creation
repeated startup record validation and copied every attributes dictionary to share its string keys.
Materialization now reopens the immutable, already validated embedded records directly and shares
string values and attribute keys during JSON parsing, allowing the parsed attributes dictionaries
to become the catalog's read-only dictionaries. A bounded stack/rented character buffer and ordinal
span lookup avoid allocating temporary strings for repeated JSON text. Startup validation remains
complete, and imported ZIP snapshots retain their prior path; no allocation budget was relaxed.

Record strings share an ordinal pool owned by the store or SDK loaded context. It is collectible
with that owner and remains empty during embedded validation. This avoids the permanent lifetime of
`string.Intern`; it also shares identical text across dataset and locale boundaries when such data
is present. The shipped inventory has **77,038 English records and 25,083 language-neutral records**,
so retaining separate translated locale copies is not the cause of this baseline's idle cost.

The public-API content census finds **102,121 records**, **322,284 attribute entries**, and
**11,222,038 bytes (10.70 MiB) of image payload**. Before sharing, the records expose **1,054,776
distinct string objects but only 256,008 ordinal string values**: their UTF-16 character payload is
**72.31 MiB**, versus **31.22 MiB** for one copy of each value. These payload counts exclude string
object headers, dictionaries, records, arrays, indexes and allocator alignment; they are not a full
heap-size attribution. Parsed record/attribute graphs and repeated strings dominate the roughly
152 MiB retained baseline; image buffers account for a smaller part. Resource bytes in loaded
assembly/native pages are outside this managed-heap measurement.

### Reproduce reference observations

Build Release first, then run with no other builds/tests in progress:

```powershell
$scratch = 'D:/Temp/saveeditor/test-scratch/perf-lazy-reference-packs'
New-Item -ItemType Directory -Force $scratch | Out-Null
$env:TEMP = $env:TMP = $env:TMPDIR = $scratch
dotnet build tests/benchmarks/SaveToolkit.Benchmarks -c Release --warnaserror
$bench = 'tests/benchmarks/SaveToolkit.Benchmarks/bin/Release/net10.0/SaveToolkit.Benchmarks.dll'
dotnet $bench --references "$scratch/references"
dotnet $bench --startup src/SaveToolkit.Api/bin/Release/net10.0/SaveToolkit.Api.dll "$scratch/startup"
./scripts/bench.ps1 -ScratchDirectory "$scratch/bdn" -Filter '*CatalogStartupBenchmarks*'
```

`--references` starts five fresh workers and retains `references.json`. Each worker discovers the
catalog before measuring a new store's construction, synchronous allocations and retained managed
heap (`GC.GetTotalMemory(true)` after minus before, with the store kept alive). It then visits packs
in store order. The first-access timer obtains the first records dataset and searches one row;
the separate remaining timer searches one row from every other dataset and requests every image
listed by that pack's embedded manifest. Neither timer builds optional sort indexes or scans all
records. Counts, string identity/value census and locale distribution come from a subsequent
complete paged traversal. The exhaustive retained measurement includes the small measurement lists,
asset-path inventory and public context cache, so it is not solely decoder object size.

First access is cold with respect to that worker's lazy catalogs/buffers, after startup validation
has already touched resource bytes and JSON code. It is not a disk-cold promise. The eager baseline
already has every catalog/image, making its first-access figures mostly public lookup overhead.
Both phases use the same worker instrumentation; baseline SDK/Core/API/plugin binaries were saved
before production edits. BenchmarkDotNet's baseline source is an isolated D: archive of `9be32cd8`,
so generated benchmark builds cannot accidentally include the new decoder. The API startup boundary
and natural-idle working-set limitations are the same as in the earlier startup section.

### Before/after observations

Captured **2026-10-29**, five fresh workers/API processes per phase, no concurrent builds/tests.
Values are **median [minimum, maximum]**. The source baseline is `9be32cd8`; the measured change is
`cf9cf8f3` plus the benchmark instrumentation. Unrelated machine activity and the OS cache remain
uncontrolled.

| Boundary | Before | After |
| --- | ---: | ---: |
| Store construction, ms | 885.741 [845.910, 1113.541] | 892.982 [797.150, 994.548] |
| Store construction, allocated MiB | 388.699 [388.626, 388.719] | 369.698 [369.671, 369.716] |
| Idle retained managed MiB | 152.139 [152.099, 152.146] | 2.995 [2.988, 2.998] |
| All materialized, retained managed MiB | 153.290 [153.249, 153.296] | 92.618 [92.612, 93.137] |
| Process start to complete `/api/plugins`, ms | 1831.380 [1756.858, 3567.810] | 1904.303 [1207.579, 2347.306] |
| API natural-idle working set, MiB | 346.750 [342.984, 350.934] | 215.375 [208.918, 236.457] |
| API natural-idle private bytes, MiB | 263.469 [256.254, 267.746] | 128.000 [121.355, 164.340] |

Unused embedded packs retain **98.0% less managed memory** at this boundary. After every dataset and
image has been requested, retained memory is still about **39.6% lower**. All **1,060,047** observed
record-string occurrences then point to exactly **256,008** string instances, equal to the number
of ordinally unique values; their character payload falls from **72.31 to 31.22 MiB**. Figures include
all still-reachable storage. Once every catalog has materialized, lazy factories release the pool
dictionary; the shared strings remain reachable through records. Startup allocation falls only about
4.9% because preserving startup validation still requires transient JSON models and image buffers.
Fresh-worker construction is roughly unchanged; overlapping API-startup ranges do not establish
a reliable HTTP cold-start speedup.

The tradeoff is extra parsing when a validated embedded dataset is first used. The sum of measured
store-construction and all catalog/image first-access intervals rises from **891.908 to 1797.568 ms**
(medians across workers). That sum excludes discovery, GC, manifest inventory and the later full
content census; it is not an end-to-end stopwatch. Typical sessions only request their matching
datasets/images, while an exhaustive cold read now performs validation and materialization separately.

### First access per pack

Median milliseconds across the same five workers. **First** means the first records catalog's
one-row search; **All** includes that first search, one-row searches for every remaining records
dataset, and all images in the pack. It does not enumerate every record or build sort indexes.
The eager baseline's small lookup numbers primarily reflect timer/call/page overhead. The first
pack also pays shared first-use/JIT work; later packs can benefit from strings already shared by
earlier packs. `witcher3.maps` has only assets, so its first records-search timer is empty.

| Pack | Before first, ms | After first, ms | After all datasets/assets, ms |
| --- | ---: | ---: | ---: |
| `astro.catalogue.en` | 0.876 | 9.983 | 14.160 |
| `atomic-heart.items.en` | 0.003 | 0.257 | 0.418 |
| `bg3.starter.en` | 0.001 | 0.030 | 0.560 |
| `cyberpunk2077.game-assets.en` | 0.001 | 0.454 | 1.382 |
| `cyberpunk2077.identifiers` | 0.001 | 18.988 | 90.979 |
| `cyberpunk2077.wiki-labels.en` | 0.001 | 0.092 | 0.095 |
| `eldenring.community.en` | 0.001 | 9.242 | 23.928 |
| `eldenring.names.en` | 0.001 | 63.133 | 126.268 |
| `eldenring.progress.en` | 0.001 | 4.432 | 4.593 |
| `ff7r.rebirth.en` | 0.002 | 5.023 | 7.318 |
| `ff7r.remake.en` | 0.001 | 1.365 | 1.933 |
| `ffxvi.abilities.en` | 0.001 | 0.486 | 0.740 |
| `ffxvi.feats.en` | 0.001 | 0.034 | 0.071 |
| `ffxvi.maps.en` | 0.001 | 0.033 | 0.200 |
| `ffxvi.nex.en` | 0.001 | 0.351 | 0.409 |
| `ffxvi.published.en` | 0.001 | 4.679 | 45.145 |
| `ffxvi.wiki.en` | 0.001 | 1.145 | 1.150 |
| `gearbox.bl3` | 0.001 | 0.193 | 185.159 |
| `gearbox.ttwl` | 0.001 | 0.238 | 243.691 |
| `genericjson.cp2077.v136` | 0.001 | 0.048 | 0.068 |
| `gow-ragnarok.2018.en` | 0.001 | 12.875 | 12.893 |
| `gow-ragnarok.assets.en` | 0.001 | 0.092 | 0.190 |
| `gow-ragnarok.community.en` | 0.001 | 0.212 | 0.234 |
| `gow-ragnarok.gear.en` | 0.001 | 8.386 | 8.393 |
| `gow-ragnarok.ps5.en` | 0.001 | 0.153 | 0.239 |
| `gow-ragnarok.runtime.en` | 0.001 | 4.687 | 4.728 |
| `gow-ragnarok.skills.en` | 0.001 | 0.511 | 0.512 |
| `hogwarts.legilimens` | 0.001 | 4.685 | 4.865 |
| `insomniac.rift.en` | 0.001 | 0.309 | 1.180 |
| `insomniac.rift.maps.en` | 0.001 | 3.511 | 4.234 |
| `pragmata.evidence.en` | 0.001 | 1.011 | 7.315 |
| `suckerpunch.tsushima` | 0.001 | 0.234 | 2.368 |
| `suckerpunch.yotei` | 0.001 | 0.132 | 0.313 |
| `talesofarise.catalog` | 0.001 | 2.197 | 34.516 |
| `unrealgvas.ps5` | 0.001 | 0.162 | 0.332 |
| `unrealgvas.storage` | 0.001 | 0.080 | 0.080 |
| `witcher3.catalog.en` | 0.001 | 29.559 | 32.403 |
| `witcher3.gwent.en` | 0.001 | 1.866 | 3.253 |
| `witcher3.maps` | 0.000 | 0.000 | 0.848 |
| `xorjson.community.en` | 0.001 | 1.881 | 10.672 |

The Release retained-memory regression runs one fresh child process, warms discovery/decoder caches,
and measures three new stores after all catalog startup validation using process-wide full-GC heap
readings. As with the existing allocation probes, the minimum of three samples must be at most
**16 MiB**, reducing sensitivity to unrelated runtime allocation.
The eager baseline failed at **159,251,440 bytes (151.87 MiB)**; the lazy implementation measured
**2,863,488 / 2,861,288 / 2,861,288 bytes (about 2.73 MiB)** in its test harness. These values use a
different harness boundary from the worker above. Debug reports the same measurements without
enforcing the memory budget. Concurrent first-access tests separately verify shared catalog/asset
identity and exactly one resource materialization after startup validation.

The first-catalog allocation regression materializes **5,000 authored records** in a fresh validated
context per sample. The initial lazy implementation failed its **8 MiB** Release budget at
**12,301,624 bytes**; the refined materializer passes at **5,756,488 bytes**. The existing three plugin
mutation-fuzz allocation budgets remain unchanged.

Raw observations and logs remain under `D:/Temp/saveeditor/test-scratch/perf-lazy-reference-packs`:
`before-references/references.json`, `after-references/references.json`,
`before-startup/startup.json`, and `after-startup/startup.json`. Saved framework-dependent binaries
are in `before-bin`/`after-bin`, and the original tracked source snapshot is in `baseline-source`.
No private saves were read by these benchmark modes or copied into their scratch directories.
Earlier after-change observations from `2575df5f` are retained under `interim-2575df5f`; the tables
here use the final `cf9cf8f3` captures at the main paths above.

### BenchmarkDotNet cross-check

The unchanged `scripts/bench.ps1 -Filter '*CatalogStartupBenchmarks*'` completed **2/2 Quick/ShortRun
cases in each phase**, using the isolated baseline archive and the current checkout respectively.
Three measured iterations per case, one launch, three warmups; total BDN times were 42 and 41 seconds
(excluding the outer source build). Values below are means and their **99.9% error half-widths**;
the very wide timing intervals require repeated Full runs before making precise latency claims.
These warmed repeated-store timings differ from the fresh-worker boundary above.

| Method | Before mean ± error, ms | After mean ± error, ms | Before allocated | After allocated |
| --- | ---: | ---: | ---: | ---: |
| `Discover` | 2.697 ± 5.378 | 1.551 ± 4.230 | 171.26 KiB | 170.19 KiB |
| `LoadEmbeddedPacks` | 695.380 ± 775.679 | 365.285 ± 500.486 | 379.018 MiB | 359.956 MiB |

`Discover` is an unchanged control path; its small difference is not attributed to reference loading.
`LoadEmbeddedPacks` includes complete validation but deliberately does not request a catalog/image,
so it measures the new idle boundary rather than deferred first-use work.
Reports remain under the task scratch root at
`before-bdn/bench-17b7657de14240e097179337d5e18df8/summary.md` and
`after-bdn/bench-addb7a6c9d10479285dadd1271c23d91/summary.md`, with detailed CSV/HTML/Markdown files
under each run's `artifacts/results`. Build, worker and API logs remain beside the reports.
