# Performance and memory review — issue #40

Measured 2026-10-29 on `review/perf-eldenring`, against baseline
`0275d6ee54eb0f658c05307f6b47a56f2325c4bd`. Production fixes:
`86e85cf` (regulation diagnostics) and `2a6f0aa` (reference searches).
All authored changes stay inside `plugins/EldenRing`; no samples or benchmark projects are committed.

This report retains that round's measurements and test counts. The later #40 test-quality review
removed the two-second startup guard mentioned below; it is no longer a current acceptance gate.
Current accuracy-pass validation is recorded in the plugin README.

## Findings and disposition

| Severity | Finding | Disposition |
| --- | --- | --- |
| Medium | Successful PARAM validation interpolated two contextual error strings for every row. | Fixed in `86e85cf`: format messages only when their bounds check fails. Error text, checks, lazy decoding and metadata caching are preserved. |
| Medium | Every catalog search formatted full descriptions, provenance and attribute labels for all candidate rows, including zero-result searches. | Fixed in `2a6f0aa`: scan existing text parts and reuse immutable dataset/source evidence within a fixed cache budget. Only reference catalogs use this path; the generic inventory/progress/regulation query helper is unchanged. |

No findings are deferred. The following investigated costs are not bugs:

- Regulation metadata is lazy and already cached per document without retaining its decoded archive.
  Repeated expansion allocates only small view metadata, not another decompression buffer.
- The plugin validates active characters/inventory on opening and again before serialization. The
  no-op/export buffer is an independent complete copy, as required for preservation and isolation.
  This review does not replace validation with a stale parsed model or return the live mutable array.
- The historical pack-reader buffer problem in `ASSET_ROUND.md` is already resolved in shared Core
  on this baseline: embedded seekable files use exact arrays; nonseekable streams use a pooled chunk.
  No plugin-local pack loader, compressed pack format or framework edit is needed.
- An apparent inventory-search slowdown in exploratory runs disappeared when repeated operations
  were isolated from earlier decoding garbage. Its source path is unchanged; final allocations are
  identical and timing ranges overlap. This was not treated as a reproducible code defect.

## Measurement method

Windows x64, .NET SDK **10.0.400**, runtime **10.0.11**, Release. Largest available Elden Ring
`PPSA03169` `memory.dat`: **28,967,040 bytes**; primary/backup twins have the same size and hash.
The harness selects the largest file without printing names, identifiers or save contents.

Projects and all raw logs/results are outside the repository:

- `D:/Temp/saveeditor/benchmarks/review-perf-eldenring/Benchmark.csproj` and `Program.cs`.
- Baseline plugin source exported with `git archive 0275d6e`, plus a matching console project, in
  `D:/Temp/saveeditor/benchmarks/review-perf-eldenring-before/`. Its scratch project references the
  same repository SDK/Testing projects and embeds the original plugin resources.
- Final paired data: `before-reviewed.jsonl`, `after-reviewed.jsonl`, `summary-reviewed.json` in the
  main benchmark folder. Earlier exploratory JSONL is retained separately, not mixed into this table.

Five alternating baseline/changed fresh-process pairs. Sample import/extraction and plugin
construction happen before measurement. A full GC precedes the first measured pack load. Each
interval uses `Stopwatch` and `GC.GetAllocatedBytesForCurrentThread()`; allocated bytes include
managed allocations on the current thread, not peak RSS or retained memory. The first operations
have no warmup: load/pin all packs, Open, BuildView, collection metadata, first held/catalog/community
pages, then lazy regulation expansion. BuildView does not expand regulation or all character sections.

Each repeated operation has its own full GC before three warmups and ten measured iterations;
GC is outside the measured interval. `DOTNET_TieredCompilation=0` is set for these final paired runs
to avoid comparing different compilation tiers. Consequently first-use times include full JIT costs
under this configuration; these numbers are not a production startup SLA. No tests or builds run
concurrently with the final benchmark pairs. Times still vary with the shared host and filesystem.

Both catalog miss queries use `Text = "no-such-item-match"`, no filters/sort and default page size.
Inventory queries use `slots/0/inventory/held`; the community query uses `catalog/community-items`.
The retained-heap observation is `GC.GetTotalMemory(true)` after the two repeated catalog misses
minus its value immediately before them, with the document/context kept alive. It measures the
additional live search state, not the document's total footprint.

All runs export the following in the same shell as the command:

```powershell
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$env:SAVETOOLKIT_TEST_TEMP='D:/Temp/saveeditor/test-scratch'
$env:TEMP='D:/Temp/saveeditor/test-scratch/review-perf-eldenring'
$env:TMP=$env:TEMP
```

## Results

Medians across five processes; time ranges are min–max. Bytes are per operation, not MiB.
Pack loading and initial view code were not optimized here; their timing differences are not
attributed to these fixes. Small allocation differences at startup include the empty search caches.

| First operation | Before ms (range) | After ms (range) | Before allocated bytes | After allocated bytes |
| --- | ---: | ---: | ---: | ---: |
| Load/pin all packs | 181.06 (171.50–199.44) | 200.79 (178.16–222.71) | 58,923,224 | 58,923,008 |
| Open | 49.59 (47.48–50.74) | 47.86 (46.17–55.65) | 30,306,632 | 30,307,136 |
| BuildView | 35.64 (34.75–45.27) | 35.47 (34.88–44.04) | 272,792 | 272,792 |
| Collection metadata | 2.21 (2.01–2.66) | 2.73 (2.22–3.48) | 828,392 | 828,392 |
| Held inventory page | 17.16 (16.74–18.92) | 19.19 (17.10–20.66) | 1,907,112 | 1,907,112 |
| Item catalog page | 3.56 (3.37–4.78) | 4.02 (3.77–4.59) | 1,210,760 | 1,210,920 |
| Community catalog page | 6.61 (5.67–14.25) | 6.18 (5.76–6.86) | 1,046,784 | 1,046,944 |
| Lazy regulation expansion | 63.54 (60.86–75.97) | 37.09 (30.71–37.83) | 84,473,744 | 46,256,648 |

| Repeated operation | Before ms (range) | After ms (range) | Before allocated bytes | After allocated bytes |
| --- | ---: | ---: | ---: | ---: |
| Held inventory default page | 0.581 (0.579–0.886) | 0.634 (0.611–0.721) | 1,845,300 | 1,845,300 |
| Held inventory miss | 1.484 (1.308–2.287) | 1.393 (1.310–1.542) | 2,149,772 | 2,149,772 |
| 5,770-record item catalog miss | 7.991 (6.600–11.477) | 1.247 (1.089–1.732) | 27,481,636 | 2,388 |
| 3,221-record community item miss | 4.041 (3.643–4.469) | 0.680 (0.638–0.748) | 22,582,772 | 450,468 |
| Cached regulation expansion | 0.00014 (0.00012–0.00019) | 0.00017 (0.00013–0.00028) | 1,412 | 1,412 |
| Serialize, including validation/copy | 4.268 (3.477–4.666) | 4.682 (4.310–5.379) | 30,160,590 | 30,160,593 |

Lazy regulation saves **38,217,096 allocated bytes**, about 45%, on the sample. It still must allocate
the 37,745,424-byte decoded archive while validating it; that buffer is not retained in metadata.
Catalog miss allocations decrease by over 99.99% / about 98.0%, respectively. Additional live state
after searching both catalogs is **63,416 bytes**, compared with **56 bytes** before: a **63,360-byte**
increase for cached shared provenance, instead of retaining a formatted description for every row.

Caches are document-owned, with at most **128 entries / 65,536 UTF-16 characters** of formatted
provenance per browser, plus bounded dictionary metadata. Once either budget is exhausted, further
source evidence is formatted without caching it. This keeps large imported packs with distinct
per-row URLs from retaining full provenance for every record. Searches containing the join separator
`·` deliberately fall back to the original formatter to preserve exact cross-part substring matches;
colon-spanning community attribute searches likewise preserve `key: value` matching. These less
common searches do not have the same allocation reduction. Exact detail filters/sorts still render
their original text. No save model, write gate, warning, maturity or reference attribution is changed.

## Verification

- Baseline full suite: **257 passed / 0 failed / 0 skipped**.
- Test first: sample directory validation failed at **38,293,104 allocated bytes** against a 2 MiB
  metadata budget. Both repeated-search tests failed at **27,619,920 / 22,651,128 bytes** against
  a 1 MiB budget. Two rendered-text characterization cases already passed before implementation.
- Added **eight cases** total: directory allocation, two search allocations, two rendered-text
  equivalence cases (including separator boundaries, exact filters and sorting), and two protocol
  validation cases, and cache-overflow behavior on a 100-source synthetic imported catalog. The
  cache-budget test first failed because all sources stayed cached; it now verifies an early cache
  hit and an uncached oversized tail. Assertions use allocation budgets, not tight stopwatch thresholds.
- Final full plugin suite: **265 passed / 0 failed / 0 skipped**. Existing sample-backed conformance,
  corruption, byte-identical no-op serialization, edit/undo/redo and Experimental warnings remain covered.
- One intermediate full run hit the existing `CatalogCompletenessTests` two-second startup guard
  at 3.37 seconds on the shared host. The guard was not weakened; the final full run passed in
  15.13 seconds with zero failures and benchmarks stopped. Initial baseline and earlier full runs also passed.
- Final API build: **0 warnings / 0 errors**. Both scratch Release benchmark builds also pass.
- Scope/whitespace audit: no authored files outside `plugins/EldenRing`, all plugin C# below
  300 lines, no hooks/tooling, sample data, push or merge.

Commands: `dotnet test --project plugins/EldenRing/SaveToolkit.Plugins.EldenRing.Tests --no-ansi`
and `dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj --nologo -v quiet`, with the environment
above. Full logs and test-first failures remain in the D: benchmark folder.
