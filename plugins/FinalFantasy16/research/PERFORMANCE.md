# Final Fantasy XVI performance review (#40)

The harness compares the original plugin at
`6e9f1d6285788c8b71749a36b31edf53a93c53a3` with the current plugin. It compiles
both against the same current SDK/Testing projects. The baseline plugin source
and resources come from `git archive`; only the scratch project reference is
adapted. Production source remains in the C: checkout.

Run from the repository root in PowerShell with .NET 10 and the private samples:

```powershell
./plugins/FinalFantasy16/research/measure-performance.ps1 -Runs 3
```

Optional parameters: `-OutputDirectory` (must be on D:), `-ArtifactsDirectory`,
and `-BaselineCommit`. Every invocation creates a fresh GUID directory. It
copies `Performance.csproj` and `MeasurePerformance.cs` there, archives the
baseline there, and runs all benchmark bin/obj/output on D:. It restores its
environment variables afterwards and retains sources/logs for reproduction.
The script sets the prescribed external artifacts, test scratch, TEMP and TMP
paths. It never extracts private sample files or writes their contents to disk.

## Measurement protocol

- Synchronous Release runs use `GC.GetAllocatedBytesForCurrentThread` and
  `Stopwatch`, with 5 warmups and 21 measured iterations per operation. Each
  process reports medians. The final comparison uses the median of three
  independently started process medians, alternating before/after variants.
- `DOTNET_TieredCompilation=0` avoids comparing early unoptimized cases with
  later promoted cases. Measurements are local observations, not deadlines.
  Final runs take place after tests/builds finish; other machine activity may
  still influence elapsed time. Allocation regressions are the durable checks.
- The largest retail ZIP entry is selected by uncompressed byte length. It is
  1,075,780 bytes, with 46 main-bank records. It is not the 172-row autosave.
  Two generated 1,200-row fixtures cover repeated mapped ID 5 and distinct
  unknown IDs. Their dense allocator keys are validated by the real parser.
- Warm cases exclude ZIP reading, fixture creation and reference-store loading.
  Open includes the input copy, CRC/framing/inventory parsing, writable-field
  map and unloaded primary bindings. Open + first view also builds every root
  tab. This measures the plugin view protocol, not HTTP serialization or browser
  DOM rendering. Each combined iteration opens a fresh document.
- The largest sample's offset-100 page is empty (only 46 rows); its figure is
  retained in raw logs but omitted from performance claims. Capacity fixtures
  have genuine 50-row pages at that offset.
- Cold pack load + open + first view is reported separately, including first-use
  JIT/embedded fallbacks. Warm pack-store reload is also separate. Shared Core
  resource buffering was not changed; these are context, not claimed fixes.
- SHA-256 fingerprints compare first views, three-row samples of all nine
  sorts in both directions, empty/single/document action plans and no-op bytes.
  The script rejects a mismatch. Only hashes are logged, not sample-derived
  view content or save bytes. Full behavior remains covered by plugin tests.

## Final measurements

Windows x64, .NET 10.0.11, three before/after process pairs after builds/tests
finished. Values below are medians of process medians; bytes are allocated per
operation. Evidence directory:
`D:/Temp/saveeditor/reviews/perf-finalfantasy16/comparisons/a4ed05af54ed4e1f9a2ed08e2153e5e2/`.
All three fixture fingerprints match in all six processes. Earlier default-tier
and concurrent-validation exploratory runs are retained separately and are not
used in this table.

| Operation | Median ms, before → after | Allocated bytes, before → after |
| --- | --- | --- |
| Largest sample: open | 2.686 → 1.978 | 2,826,024 → 1,207,128 |
| Largest sample: open + first view | 4.401 → 2.497 | 7,159,328 → 3,125,296 |
| Largest sample: empty preview | 1.936 → <0.001 | 1,080,112 → 200 |
| Largest sample: one-row preview | 1.962 → 1.792 | 1,083,856 → 5,168 |
| Capacity mapped: open + first view | 18.006 → 1.010 | 49,695,520 → 2,061,632 |
| Capacity mapped: one-row native page | 2.528 → 0.003 | 3,186,432 → 15,304 |
| Capacity mapped: 50-row page at offset 100 | 4.061 → 0.163 | 6,101,472 → 705,280 |
| Capacity mapped: missing-text search | 7.234 → 0.440 | 17,771,712 → 116,680 |
| Capacity mapped: descending name sort, one row | 2.710 → 0.032 | 3,191,232 → 44,680 |
| Capacity mapped: descending quantity sort, one row | 2.607 → 0.018 | 3,186,536 → 39,984 |
| Capacity mapped: ID filter, one row | 2.713 → 0.029 | 3,253,728 → 25,200 |
| Capacity mapped: document preview | 3.493 → 0.430 | 4,283,512 → 577,240 |
| Capacity unknown: open + first view | 22.928 → 0.915 | 28,081,536 → 2,667,960 |
| Capacity unknown: one-row native page | 4.479 → 0.002 | 6,463,648 → 9,160 |
| Capacity unknown: missing-text search | 6.997 → 0.293 | 9,669,208 → 155,080 |
| Capacity unknown: document preview | 5.424 → 0.403 | 7,576,312 → 606,040 |

Changed previews still checksum the complete payload; their latency remains
visible on the largest file despite almost eliminating their candidate-copy
allocations. Shared CRC implementation changes are outside this review.

## Regression evidence

The baseline complete suite had 196 passing cases. The final suite has 245,
including 49 new performance/characterization cases. With private samples it
has zero failures/skips; with artifacts unset it has 197 passes and 48 expected
missing-sample skips. API Release build with `--warnaserror`: zero warnings/errors.

Test-first reproductions:

- Catalog: 3/3 failed (repeated pinned/fallback lookups and attribution allocation).
  Additional cases cover shared document resolution and a bounded item cache.
- Queries: 5/6 failed after the catalog fix; one-row native pages allocated about
  208 KiB and numeric-only filtering still requested item metadata. The evidence/
  flags/decorative-column characterization passed before the query change.
- Planning: 4/7 failed. Adding 4 MiB of opaque payload added exactly 4 MiB of
  preview allocations, including empty/unchanged scopes. Three arbitrary-order
  independent CRC characterizations passed before the change.
- Warnings: 12/12 failed (all resource/category branches allocated per row).
- Opening: 1/1 failed after the other fixes (12,530,240 bytes for an unrendered
  capacity inventory). The regression requires less than 4 MiB, no reference
  resolution before a view, and working unloaded identity/quantity bindings.

Final query coverage includes all nine columns in both directions across all
1,200 rows, numeric ascending-slot ties, complete counts, evidence/flags search,
localized overrides, and existing edited/undone values and full filtered actions.
Preview tests independently patch bytes/CRC in arbitrary selection order, include
upgrade aliases, and prove purity. No regression asserts wall-clock time.
The payload-scaling assertion caps positive allocation growth with a 64 KiB
allowance for BCL/tiering/hash-table variation. An initial symmetric 4 KiB
comparison incorrectly rejected a larger fixture allocating 7,640 bytes less;
the corrected bound still rejects the original exact 4 MiB copy increase.

## Remaining framework opportunities

`BoundSaveDocument.Field` still allocates a view node to register a primary
binding; a non-rendering registration API would remove those temporary nodes.
`ReferenceContext.Catalog` repeats pack selection/usage tracking, mitigated here
by pinning each adapter lookup once. Core's 81,920-byte per-resource read buffer
contributes to shared cold pack loading. These are described on #40 and left
outside this plugin scope. CRC/framing revalidation in `Serialize` is intentional
integrity work, not an accidental repeated-parse defect.

Cleanup: automatic approval review rejected recursive deletion of
`D:/Temp/saveeditor/test-scratch/review-perf-finalfantasy16` with "blocked by
policy". That tool/test scratch remains for the orchestrator. Benchmark sources,
baseline snapshots and measurement/TRX logs under the review-results directory
are intentionally retained as evidence.
