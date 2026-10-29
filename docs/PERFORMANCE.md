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
  (**41652 gzip bytes**) and its CSS (**1504**). The existing Opening status covers the code load.
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
| GraphView.js | 1747 | 1930 |
| MapView.js | 2129 | 2350 |
| SpatialShared.js | 1788 | 1970 |
| SpatialShared.css | 911 | 1010 |

CI `validate` runs `npm run check:bundle` immediately after its production build on **both Ubuntu
and Windows**. Synthetic Vitest regressions cover shared imports/cycles, lazy JS/CSS, aggregate
and individual failures, exact limits, new chunks, missing builds/assets/imports and untracked
output. A built-app Playwright regression checks that the file picker does not download editor or
spatial chunks, holds the editor download to exercise Opening, then edits and reloads a deep link.
After intentional output/dependency changes, review the loading graph and remeasure before updating
both JSON limits and this table with a reason; see the [release checks](RELEASING.md#continuous-integration).

## Backend benchmarks

The opt-in `tests/benchmarks/SaveToolkit.Benchmarks` executable measures the real Core and
GenericJson/XorJson paths with [BenchmarkDotNet](https://benchmarkdotnet.org/).
It has no test adapter, is explicitly not a test project, and is not discovered by
`scripts/test.ps1` (which selects `*.Tests.csproj`), `dotnet test`, or CI Validate.
The solution includes it for normal builds; benchmarking requires an explicit run.
No private samples, sample loaders, network data, reference packs or user workspaces are read.

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
over benchmark names. The quick suite covers all 36 combinations (six operations, three sizes,
two formats); allow several minutes. Full is appropriate before drawing performance conclusions.

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
