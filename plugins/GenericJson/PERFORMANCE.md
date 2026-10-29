# Performance review — issue #40, 2026-10-29

Branch: `review/perf-genericjson`. Baseline: `3e777c6`. All changes are inside this plugin.

Six findings fixed: four medium (redundant editable-tree construction for probe/export;
repeated structure reviews; query allocations; quadratic/repeated title-adapter construction)
and two low (selected reset scans; redundant numeric validation/hash buffers on opening).
No confirmed finding is deferred. Experimental maturity, write warnings and action bounds remain intact.

## Reproduction

The synchronous external harness and its baseline source archive are preserved at
`D:/Temp/saveeditor/review-results/perf-genericjson/`:

```powershell
& D:/Temp/saveeditor/review-results/perf-genericjson/run.ps1 -Baseline -Label before-final
& D:/Temp/saveeditor/review-results/perf-genericjson/run.ps1 -Label after-final
```

`Program.cs`, `Harness.csproj`, `baseline-harness/Harness.csproj` and the unmodified production
sources extracted from `git archive 3e777c6 plugins/GenericJson/SaveToolkit.Plugins.GenericJson`
are retained there. Both projects reference the same unchanged SDK/Testing projects in this worktree.
The baseline extraction has a separate harness project; the archived production code is unmodified.
The scripts set all test/temp paths on D: and read the existing private sample archives through
`SAVETOOLKIT_ARTIFACTS`. No sample data is committed or printed.

Environment: Windows x64, SDK 10.0.400, .NET 10.0.11, Release, `DOTNET_TieredCompilation=0`.
Each operation has three warmups and eleven measurements, with independent medians of elapsed
Stopwatch time and `GC.GetAllocatedBytesForCurrentThread`. Timing is diagnostic, never a test gate.
Sample imports and reference-pack store construction/pinning are outside measurements. Open includes
its own input read and first `BuildView`, including pinned references for the sample.
These measure the plugin's abstract view construction; HTTP serialization/browser rendering is outside scope.

The largest recognized sample across the three supported title archives is Cyberpunk `settings.json`,
45,415 bytes. The synthetic array is built in memory from integers 0 through 49,999, 288,891 bytes.
The additional wrapper contains 50,000 boolean object members in the supported wrapper title/role.
Pages request 20 rows. The sorted query searches for `499`, sorts paths descending and retains the
existing 10,000-match sort bound. The selected reset targets the changed final scalar. The wrapper
preview selects one unchanged value, so it has no effects; its full model previously cloned/reparsed anyway.

## Results

Numbers below come from the final complete runs in `before-final.txt` and `after-final.txt`.
The earlier isolated title-adapter run in `progress.txt` already included streaming validation;
it is retained as supporting evidence, not mixed into this comparison.

| Operation | Before ms / allocated bytes | After ms / allocated bytes |
| --- | ---: | ---: |
| Sample probe | 0.3967 / 938,712 | 0.1590 / 128,232 |
| Sample Open + first view | 2.3335 / 3,393,344 | 1.3264 / 2,092,920 |
| Sample repeated first view | 0.6560 / 1,061,584 | 0.0078 / 25,808 |
| Sample verified export | 0.4172 / 987,648 | 0.1752 / 177,168 |
| 50k array probe | 63.4598 / 37,043,672 | 1.3015 / 289,040 |
| 50k array Open + first view | 81.8149 / 45,841,824 | 55.3486 / 35,465,496 |
| 50k array repeated first view | 6.2577 / 4,401,272 | 0.0014 / 5,440 |
| Deep whole-tree page | 10.4342 / 8,439,632 | 0.0116 / 41,952 |
| Deep direct-array page | 0.0162 / 52,048 | 0.0114 / 42,016 |
| Direct-array missing-text search | 12.4832 / 21,888,096 | 3.5530 / 752 |
| Direct-array unchanged-state filtered page | 8.4586 / 11,240,128 | 0.6996 / 42,752 |
| Direct-array path-sorted matches | 13.3867 / 21,895,664 | 5.0052 / 60,912 |
| 50k array verified export | 82.4942 / 38,092,664 | 4.6657 / 1,338,136 |
| One-row reset preview | 2.2416 / 2,393,304 | 0.0006 / 1,496 |
| 50k array move preview | 159.6849 / 82,566,976 | 71.2903 / 37,036,048 |
| 50k-member wrapper Open + first view | 4,625.7839 / 52,466,824 | 78.2810 / 43,731,544 |
| Unchanged one-row wrapper preview | 2,774.7592 / 72,734,912 | 0.0744 / 2,336 |

The real-sample reference-call regression records eight initial catalogue `Find` calls. Repeating the
view and an empty-scope preview formerly increased that total to 24; it now remains eight.
Cache reuse never crosses a document-state change.

## Implementation and preservation

- Probe validates complete syntax/UTF-8/Unicode/depth/node limits with `Utf8JsonReader`, without an
  editable tree. Open still constructs the single lossless editable model. Export still reparses
  every output and compares names, nesting, child counts, scalar types and exact number lexemes.
  It never materializes a second tree or converts numbers to floating point.
- Structural review strings retain their complete SHA-256 digest and bounded prefix. Reindex clears
  cached reviews after all membership/name/order mutations and snapshot restores. Hashing array IDs
  uses reusable bounded character/UTF-8 buffers, avoiding eager identity strings for unopened children.
- Queries use the live preorder index or direct child list. Plain pages address their slice directly.
  Text/path predicates format into a bounded stack buffer; only returned cells/sort keys need path
  strings. Escaped member names and friendly labels are cached until reindex. Identity predicates
  resolve once, filters are copied once, and structure-state comparison reads native child identities.
  Sort keys are computed once per match, retaining ordinal lexical order and the existing identity ties.
- Selected reset plans resolve only requested identities and sort them by live document order.
- Reindex's existing occurrence-count pass marks unique members, including siblings beyond the
  adapter prefix. This removes repeated full-sibling scans without losing duplicate-key rejection
  in title writes. Semantic adapters are lazy and reused until scalar/structure changes or restore.
  Empty-effect semantic previews do not clone/write; every real change still validates an isolated trial.
- Numeric parsing checks SDK resource eligibility before the existing fully validated exact-value
  constructor. Oversized lexemes/extreme exponents still use the verbatim read-only fallback.

API behavior was checked against Microsoft's primary documentation for
[`Utf8JsonReader`](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.utf8jsonreader?view=net-10.0),
[`CopyString`](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.utf8jsonreader.copystring?view=net-10.0)
and [`ValueTextEquals`](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.utf8jsonreader.valuetextequals?view=net-10.0).
No upstream code was copied. JSON remains uncompressed; no SQLite/decompression path exists here.

## Validation

- Baseline: **349 passed / 0 failed / 0 skipped**. Final: **402 passed / 0 failed / 0 skipped**.
  TRX case-name comparison confirms every original case remains, with 53 added cases.
- Eleven allocation/call-count cases were observed failing before their fixes, with red TRX reports
  preserved on D:. All new performance gates assert allocated bytes or reference lookup counts.
  Cold and repeated missing-text queries, sorted pages, and the existing direct-page behavior are covered.
- Independent characterization covers exact structure digests, long names, duplicate occurrences,
  both sort directions and ties, complete filtered page membership, invalid/empty selection,
  added/read-only/container reset rows, moved identities, late duplicates, rename and undo invalidation.
- Streaming verifier tests reject changed hierarchy, counts, duplicate-name ordering, numeric spelling,
  type, values and malformed Unicode. Existing full samples, huge-number and corruption/boundary
  suites pass, including the 250,000-node case and byte-identical no-op round trips.
- Five before/after SHA-256 fingerprints of JSON-serialized abstract views/pages match: sample first
  view/search page, synthetic first view/deep whole-tree page/path-sorted page.
- Final API Release build with `--warnaserror`: **0 warnings / 0 errors**. Scope and whitespace checks pass;
  all handwritten C# files remain below 300 lines. No push, merge, PR or console-validation claim.

Structural previews and previews with real semantic effects still clone the editable model to isolate
trial writes. Searches/sorts still inspect all candidates required by the query. These are retained
correctness costs, not deferred confirmed findings. No global parse/result cache was introduced.

Cleanup handoff: automatic approval review rejected recursive deletion of
`D:/Temp/saveeditor/test-scratch/review-perf-genericjson` as **blocked by policy**.
The orchestrator must clean up that task scratch folder. The separate review-results directory
is intentionally retained for reproducibility and verification.
