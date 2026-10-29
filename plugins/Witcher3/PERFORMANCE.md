# Performance review (#40)

Measured 2026-10-29 on Windows x64, Release .NET 10, on branch
`review/perf-witcher3`. This review changes allocations and presentation work;
it introduces no new format or gameplay assumptions.

## Findings and fixes

- **Low, explorer strings** (`8f31699`): parsing used to format an opaque offset
  label for every node, even when a dictionary name immediately replaced it.
  Known token tags also created identical strings repeatedly. Format fallback
  labels only for opaque nodes and reuse literal tags. Full validation and tree
  indexing still happen at open; lazy expansion still bounds presentation.
- **Medium, saved-world queries** (`16ab604`, `cb5f16e`): a small query created
  formatted metadata and detail views for every record before paging. Retain
  immutable metadata only for queried kinds and create details only for returned
  rows. Ordinary text searches inspect those existing strings. Multiline searches
  preserve the previous cross-field matching through an uncached concatenation.
  Recipes, facts and journal have no writer, so this metadata cannot become stale
  after supported edits. No full catalogue metadata index was added.
- **Medium, Gwent catalogue snapshots** (`2059fb9`): search, sort, filters and row
  rendering repeatedly cloned the complete current card collection. Capture it
  once per query, with ordinal raw-key lookup shared by all query stages. The
  next query captures edits again. Unavailable and empty collections retain their
  different labels.

No confirmed finding is deferred. Reopening serialized output remains an
intentional integrity check. Container decompression, directory parsing, embedded
archive bounds and unknown bytes remain unchanged. Optional projections are
initialized once per document; reference images remain host-owned cached assets.

## Measurement method

The largest of all eight supplied records is
`Manual.8559a.7e73c800.31c94f5.sav`: **228,734 stored bytes, 724,174 expanded
bytes, 18,445 indexed spans, 21,894 explorer nodes**. Import the archive once,
select by stored length and create an in-memory file before timing. No sample
bytes are committed.

Benchmark project, source and raw JSON stay outside the checkout at
`D:/Temp/saveeditor/benchmarks/review-perf-witcher3/`. The harness references
the production plugin and Testing projects; it performs no edits. Run with:

```powershell
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$env:SAVETOOLKIT_TEST_TEMP = 'D:/Temp/saveeditor/test-scratch'
$env:TEMP = $env:TMP = 'D:/Temp/saveeditor/test-scratch/review-perf-witcher3'
$env:DOTNET_TieredCompilation = '0'
dotnet run --project D:/Temp/saveeditor/benchmarks/review-perf-witcher3/Benchmark.csproj -c Release -- results.json
```

Each operation has five warmups and 21 measurements, with full GC between
measurements. Report medians of `Stopwatch` elapsed time and
`GC.GetAllocatedBytesForCurrentThread` deltas, excluding the stopwatch object.
These are synchronous paths. Input import, disk reads and initial pack loading
are excluded unless explicitly named. Open includes the SDK input-buffer copy,
decompression, directory/tree validation and player parsing. First view means a
fresh document's complete root view, with its optional projections initialized.
Initial pages request 50 rows from each of the 15 declared collections. Repeated
page measurements request five rows from an already-open document. The Gwent
search requests five rows with text `saved copies` and sort column `owned`.
Every run verifies byte-identical no-op serialization.

## Results

Before: `baseline-stable.json` at parent `5c07288`. After:
`final-with-retention.json` at `cb5f16e`. Bytes are decimal allocated bytes,
not peak working set. Wall times are indicative: other work on this shared
machine varied substantially between runs; allocation deltas were stable.

| Operation | Before ms | After ms | Before allocated bytes | After allocated bytes |
| --- | ---: | ---: | ---: | ---: |
| Open | 4.410 | 3.156 | 6,764,616 | 5,110,624 |
| Open + first view | 6.128 | 4.305 | 8,286,784 | 6,632,880 |
| Open + view + all initial pages | 7.890 | 5.999 | 11,245,656 | 9,420,144 |
| Repeated recipe page, 5 rows | 0.126 | 0.065 | 54,856 | 7,176 |
| Repeated facts page, 5 rows | 0.278 | 0.197 | 272,904 | 27,336 |
| Repeated journal page, 5 rows | 0.290 | 0.103 | 401,440 | 12,896 |
| Gwent catalogue search/sort, 5 rows | 0.648 | 0.372 | 555,104 | 174,280 |
| Fresh host pack store + open + view + initial pages | 37.901 | 54.346 | 34,894,936 | 33,074,832 |

Open + first view allocates **20.0% less**; repeated journal pages allocate
**96.8% less**; the Gwent search allocates **68.6% less**. Fresh-store timings
in repeated runs ranged from 37.9–64.1 ms before and 54.3–56.6 ms after, so
no startup timing improvement is claimed. Host pack validation/loading remains
outside this plugin's scope; its allocated-byte reduction comes from the plugin.
No-op serialization remains a complete original-file copy (229,072 allocated
bytes including output bookkeeping), with no recompression.

## Retained memory and lazy loading

Use `GC.GetTotalMemory(true)` before and after retaining eight documents, divide
by eight and take the median of seven batches. Keep the pinned reference context
outside the measurement. Discard root views and response pages before full GC;
these numbers estimate live document state, excluding shared pack assets. The
baseline copy is reconstructed from the parent source into a separate D: project;
raw results are in `baseline-with-retention.json` and `final-with-retention.json`.

| Live document state | Before bytes/document | After bytes/document |
| --- | ---: | ---: |
| Open + root view | 5,592,233 | 5,109,348 |
| After visiting initial pages | 5,607,251 | 5,482,699 |
| After searching recipes, facts and journal | 5,607,044 | 5,482,699 |

The saved-world metadata cache costs approximately **373 KB** once all three
kinds are visited. Literal tags save approximately **483 KB** of live tree
strings, so total retained state is still lower in these scenarios. Before any
query, no saved-world row metadata or detail views are built. Returned detail
views are not cached. Searching does not retain a duplicate search corpus.
Multiline queries can allocate temporary combined text to preserve legacy matches.
Sorting still needs arrays proportional to matching records; only returned rich
views are page-bounded. These measurements do not imply O(page-size) total memory.

## Validation

Baseline: **128 passed, 0 failed, 0 skipped**. Ten added cases bring the final
suite to **138 passed, 0 failed, 0 skipped**. Seven initial regressions failed
before fixes (tree allocation, facts/journal allocation, four snapshot-count
cases). Two refined first-text-search checks also failed before removing the
duplicate search corpus. Recipe allocation and missing/empty collection cases
are characterizations. Budgets concern allocations or callback counts, never
wall time. Tests also cover late-page identities, current edits, complete-list
search/filter behavior, lazy detail expansion and cross-field multiline matches.
Existing conformance, byte-preservation, write, undo/redo and F6 tests all pass.

Full API build: **0 warnings, 0 errors**. All changed source files remain below
300 lines. No repository files outside `plugins/Witcher3/` changed. No push or
merge was performed. Existing Experimental maturity and all write warnings remain.

Implementation follows the existing attributed format sources in
[README.md](README.md#sources-and-attribution); no schema was inferred for this
review. Allocation versus retained-memory distinctions follow the runtime APIs
[GC.GetAllocatedBytesForCurrentThread](https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread)
and [GC.GetTotalMemory](https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalmemory).
