# Performance review (#40)

Historical measurements and validation from 2026-10-29, not current suite counts or a fresh
benchmark of main. See [README validation](README.md#validation) for the documentation-accuracy run.

Branch: `review/perf-astroplayroom`. Baseline: `b4c2e20`. Production changes stop at `e099950`.
All source/test edits stay under `plugins/AstroPlayroom/`; the parallel shared
`InMemoryCollection` optimization is outside this review.

## Reproduction

The standalone synchronous harness is retained on D: at
`D:/Temp/saveeditor/review-results/perf-astroplayroom/` (`Harness.csproj`, `Program.cs`, `run.ps1`).
Its project references this worktree's plugin and Testing projects. From the same worktree, run:

```powershell
& D:/Temp/saveeditor/review-results/perf-astroplayroom/run.ps1 -Label after-final
```

With a clean checkout, run the same script at baseline `b4c2e20` using a different label, then
restore the review branch. The retained `baseline-final.txt` and `after-final.txt` were measured
on those respective source versions. No private samples or decoded sample data are in the harness
source or repository. Imported samples use process-managed D: scratch.

The runner exports the prescribed external `SAVETOOLKIT_ARTIFACTS` path,
`SAVETOOLKIT_TEST_TEMP=D:/Temp/saveeditor/test-scratch`, and both TEMP/TMP to
`D:/Temp/saveeditor/test-scratch/review-perf-astroplayroom`. It disables tiered compilation and
runs Release on SDK 10.0.400 / runtime 10.0.11. Each operation has five warmups followed by 21
observations, with independent medians of elapsed time and
[`GC.GetAllocatedBytesForCurrentThread`](https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread).
These are allocated managed bytes on the measuring thread, not retained-memory measurements.

All four sample files tie at 1,048,576 bytes. Choose generation-1 primary deterministically.
Import/discovery, plugin construction and pinned reference-pack loading are outside measurements.
Open includes the file read/copy and parser. First view includes the complete initial view tree.
The compound first-page operation uses a fresh document each iteration, so it includes lazy query
projection creation. Other queries/plans reuse one open document. Pages request 20 rows; the late
page uses offset 140. Synthetic wide input has 2,048 attributes; mixed input has 4,096 numeric
text tokens separated by comments, within the parser's supported limits.

## Measurements

| Operation | Before ms / bytes | After ms / bytes |
| --- | ---: | ---: |
| Sample Open | 2.5579 / 3,360,504 | 2.0795 / 3,379,032 |
| Sample Open + first view | 3.5853 / 5,380,400 | 2.8606 / 5,091,672 |
| Sample Open + first view + first page | 4.0867 / 6,193,032 | 2.8110 / 5,803,240 |
| First 20-row page, repeated | 0.2096 / 812,488 | 0.0444 / 240,344 |
| Late 20-row page, repeated | 0.2199 / 778,736 | 0.0489 / 205,264 |
| No-match whole-model search | 0.2126 / 601,344 | 0.0503 / 38,488 |
| Puzzle search + name sort | 0.2470 / 828,464 | 0.0700 / 256,536 |
| Status sort | 0.2266 / 812,888 | 0.0530 / 240,696 |
| All-record ownership preview | 0.0303 / 67,904 | 0.0078 / 40,352 |
| Candidate-world ownership preview | 0.0749 / 123,296 | 0.0066 / 17,648 |
| Single selected-row preview | 0.0076 / 8,616 | 0.0002 / 920 |
| Wide Open | 1.7524 / 3,666,896 | 1.4920 / 3,365,984 |
| Maximum mixed-text Open | 35.1623 / 5,845,616 | 3.4836 / 5,847,200 |
| First Raw attribute page | 0.0084 / 29,384 | 0.0092 / 35,024 |
| Raw attribute page 31 | 0.2932 / 893,504 | 0.0081 / 35,976 |

Timings are workstation observations, never test gates or additive costs. Indexed selection and
candidate joins add about 18.5 KB to sample Open; Open + first view still allocates about 289 KB
less. First Raw-page allocation increases by 5.6 KB; late pages now remain bounded instead of
constructing all skipped fields. Mixed-text opening removes quadratic recounting without a material
allocation change. The shared collection helper still owns per-query scanning/filtering/sorting;
no plugin-local replacement or result cache bypasses it. Cached projections add bounded retained
state on the first query, and read live ownership for every status search/filter/comparison.

## Findings and validation

Fixed: four medium findings (query projection rebuilding; structural flag/reference recomputation;
Raw paging before rendering; repeated mixed-text shape counting/discarded binding presentation)
and one low finding (selected-scope scans and duplicated world planning). No local findings are
deferred. Parsing once on Open and reparsing serialized output for byte/value verification are
intentional; those integrity checks remain. All existing warnings, maturity and write boundaries
are preserved.

Eight new regression cases failed before their fixes: two model allocation/call-count checks, two
query allocation bounds, one Raw allocation bound, two planning allocation bounds and one opening
allocation bound. Characterization checks additionally cover ambiguous ownership structures,
native ordering/duplicate identities, exact continuations and values, invalid selections/error
precedence, current edit/revert membership, document isolation, and all 4,096 mixed-text values.
The mixed-text case asserts a single source-file read across opening, complete traversal, query,
planning and serialization. No wall-clock assertion was added.

Review-round Release validation with external samples: **236 passed / 0 failed / 0 skipped**, baseline
**210 passed**; **26 cases added**. Without private samples: **176 passed / 0 failed / 60 clean
skips**, including all 21 new sample-free cases. TRX counters independently inspected. API Release
build with warnings as errors: **0 warnings / 0 errors**. Conformance, byte-identical no-op output,
exact-write preservation, export, stale/tampered plans and grouped undo/redo remain covered.

Evidence logs/TRX and failing-before runs are beside the harness. Every handwritten plugin C# file
remains below 300 lines. No shared SDK, framework, web, workflow or tooling file was changed.
