# Performance review (#40)

Reviewed `plugins/Pragmata` from `c12d5148bbe345036f25f8e4adc19a304b3548b5`
on `review/perf-pragmata`. Findings were posted on #40 before implementation.
No format semantics, write scope, warnings, maturity or integrity checks changed.

## Reproduction

The source-only baseline snapshot, identical before/after console harnesses,
invocation instructions, logs and TRX reports are retained at
`D:/Temp/saveeditor/review-results/perf-pragmata/`. No private bytes are committed.
Run `baseline-harness/Perf.csproj` then `harness/Perf.csproj` with
`dotnet run --project <path> -c Release`. The D: README records absolute project
references, baseline snapshot setup and the required artifact/TEMP/TMP variables.

Measurements are synchronous Release .NET 10.0.11 on Windows, with three warmups
and 11 measured operations. Each result reports the median of
`GC.GetAllocatedBytesForCurrentThread` deltas and the median elapsed milliseconds.
Import, sample selection and pinned reference-pack construction are outside
measurement. Bounds excludes parsing; Open includes decryption, validation,
parsing, edit bounds and model creation. Open + first view also calls BuildView.
First-page cases prepare a fresh document outside measurement, including index
construction in the measured query. Repeated queries reuse a document/index;
regressions separately change offsets, limits and search/filter/sort criteria.
Synthetic evidence uses a trivial detail node; retail evidence uses real details.

## Findings and results

Three medium findings were fixed; no high/low findings or deferred findings.

- `eaddc47`: immutable physical paths were recursively formatted on every access.
  Memoize them lazily, keeping unopened raw subtrees free of formatted paths.
- `29547b3`: bounds construction allocated recursive enumerators and copied
  unchanged specs for matching fields. Visit the depth-bounded parsed tree directly
  and replace a spec only when its accumulated bounds change.
- `289e5b3`: every page rebuilt descriptions and sorted the entire collection.
  Lazily retain model projections and one ordered match set per collection. Evidence
  summaries/fingerprints and combined search text are also lazy and reused.

Final sequential measurements (`before-final.txt`, `after-final.txt`):

| Operation | Before allocated bytes | After allocated bytes | Median ms, before → after |
| --- | ---: | ---: | ---: |
| Largest sample Open | 52,841,376 | 35,936,064 | 69.4034 → 56.8801 |
| Largest sample Open + first view | 54,705,152 | 37,686,688 | 63.4313 → 64.3641 |
| Largest sample bounds only | 19,395,896 | 1,021,400 | 8.6661 → 12.7511 |
| Retail evidence first 10 rows | 39,861,144 | 3,399,136 | 22.7730 → 12.3116 |
| Retail evidence repeated 10 rows | 39,860,792 | 19,176 | 48.5492 → 0.0105 |
| Retail evidence repeated search | 49,699,568 | 19,176 | 22.6555 → 0.0092 |
| Retail evidence repeated sort | 39,860,896 | 19,304 | 35.6444 → 0.0093 |
| Retail quantity plan | 165,904 | 89,048 | 0.2023 → 0.1064 |
| Authored 10,000-item first 10 rows | 4,272,512 | 3,863,064 | 2.1141 → 3.0370 |
| Authored 10,000-item repeated 10 rows | 3,630,920 | 59,144 | 2.8456 → 0.0458 |
| Authored 10,000-item repeated search | 4,812,024 | 59,112 | 3.2360 → 0.0443 |
| Authored 10,000-item repeated sort | 3,591,480 | 59,528 | 4.4955 → 0.0465 |
| Authored 10,000-record evidence first page | 12,675,064 | 4,526,776 | 11.0242 → 3.1046 |
| Authored evidence repeated page/search/sort | 12,674,976–15,240,872 | 7,176–7,240 | 7.6929–9.6416 → 0.0027–0.0047 |
| Same physical path, 10,000 reads | 4,480,000 | 0 | 2.9918 → 0.0617 |
| Authored 10,000-item quantity plan | 19,190,632 | 10,232,480 | 24.9450 → 17.8291 |

Largest sample: `data010Slot.bin`, 3,367,308 bytes. Its largest registered
collection has 6,988 rows. Open/first-view allocations fall about 31%; bounds
allocations fall about 95%. Timing varies considerably with host load and runtime
tiering (including unchanged container code); no consistent Open/first-view or
first inventory-page latency improvement is claimed. No test gates elapsed time.

The tradeoff is retaining model projections and at most one ordered result per
queried collection until a successful edit invalidates them. Values/search strings
are bounded by the existing summary limits and physical record count. No page
views, caller filter dictionaries or query history accumulate. Fresh page views
still publish all bindings/details, even on an index hit. Re-registering an evidence
table refreshes its detail callback while preserving its model index. Successful
primary, raw, reference, clock and action edits invalidate both collection families;
rejected/no-op edits preserve the unchanged indexes.

Open decrypts/parses once. Queries/plans do neither. Serialize deliberately verifies
complete rebuilt output through Mandarin validation and typed parsing; that remains.
The inspected planning lookups are linear or dictionary-backed; no O(n²) defect was
established. Remaining output-sized plan allocations are not claimed eliminated.

## Validation

- Baseline: **240 passed / 0 failed / 0 skipped**.
- Final complete sample-backed Release suite: **273 passed / 0 failed / 0 skipped**.
  TRX names confirm all 240 original cases remain, with 33 added cases.
- Sample-free Release suite: **199 passed / 0 failed / 74 expected sample skips**.
  Of the 33 new cases, 32 execute without private samples.
- API Release build with `--warnaserror`: **0 warnings / 0 errors**.
- Before fixes: both path/plan allocation tests fail, bounds allocation fails,
  and all nine initial collection allocation cases fail. The final budgets retain
  room for runtime-tier allocation variance and required retained index state while
  rejecting the original allocations. Full-suite plan allocations can reach 13.2 MB
  versus 10.2 MB in the isolated harness; its budget is 14 MB, below the 19.2 MB baseline.
- New deterministic checks cover all inventory columns/directions/ties across pages,
  mutable filter snapshots, changed criteria, cache eviction/invalidation call counts,
  fresh details, exact membership, live edits/reversal and raw item/equipped changes.
  Existing corpus, conformance, malformed input, output hashes, byte-identical no-op,
  edit/reopen and undo/redo checks remain green.
- All changed repository files are within `plugins/Pragmata`; handwritten C# stays
  below 300 lines. No dependencies, shared tooling, sample bytes, push or merge.

Existing upstream [typed-stream definitions](https://github.com/kvasszn/ree-save-editor/blob/main/src/save/types.rs)
and [Mandarin implementation](https://github.com/kvasszn/ree-save-editor/blob/main/src/save/crypto/mandarin.rs)
were rechecked as format context. Their existing source/rights attribution remains
in the plugin README; no upstream implementation or new format assumptions were imported.
