# Performance — F8 semantic review (#40 / SDK-01)

Baseline: local main `b96a2686`, including `AllocationProbe`, SDK API review guards,
the batched reference route and plugin updates. Both sides use the same sample fixtures and .NET SDK 10.0.400,
Release, on the owner's Windows workstation. Private archives are read from
`SAVETOOLKIT_ARTIFACTS`; all test extraction, logs and results use
`D:/Temp/saveeditor/test-scratch/rev-perf-semantic-review/`.

Each warm row measures one synchronous `DocumentSession.GetChanges` call after
five warmups: independent minima of seven wall-time/current-thread managed
allocation measurements. Original capture is retained across these requests.
Warm allocation budgets use `SaveToolkit.Testing.AllocationProbe` in
`AllocationTestCollection`; result/page-count validation runs outside the measured
interval. Release enforces the budgets; Debug reports allocation samples.
Numbers exclude import, current-document opening, edit setup, HTTP transport and
JSON response serialization. First-review rows include original opening/capture
and are single diagnostic observations. Workstation scheduling and tiered JIT
affect timings; allocation differences provide the more repeatable comparison.
Before runs restore the two traversal/review production files from that baseline;
optimized sources are then restored and rebuilt for after runs.

## Coverage and limits

The full, unchanged plugin views exhaust retained-view/traversal budgets before
querying any F8 pages on these samples. All four correctly return `complete:false`
with explicit collection truncation warnings, even with zero changes. An empty
partial log therefore does **not** prove that there are no changes. This behavior
is preserved; plugin view construction remains the dominant cost.
The budgets bound retained review fields and serialized F8 page coverage, rather
than all transient allocations inside plugin `BuildView`/`ExpandNode` calls.
Managed allocated bytes are cumulative; they are not peak resident memory.

A separate, explicitly labelled **F8 projection** retains writable eager
quantities and exposes the largest collection plus inventory/value collections,
putting inventory/value collections first. Its pages and lazy row details still
come from the real plugin; it omits the unrelated raw root view. It is a diagnostic
fixture, not the actual API view or a proposed product behavior change.

| Plugin | Largest exercised collection | Warm pages before/after | Coverage of F8 projection |
| --- | --- | --- | --- |
| Pragmata | 6,988 retail world records, plus eager quantities | 15 / 15 | Partial: retained-view/traversal cap |
| TalesOfArise | 34,136 evidence rows, plus 934 inventory slots | 51 / 51 | Partial: global 10,000-row cap |
| FinalFantasy16 | 172 allocated retail inventory records | 1 / 1 | Complete |
| BaldursGate3 | 2,335 live values, plus inventory/other live values | 17 / 17 | Complete |

Edits are selected at even intervals across available writable fields. Pragmata,
FF16 and BG3 use 100 distinct fields. Tales has only 20 writable integer fields
in its full view and one in the projection: its 100-operation case repeats real
mutations over those fields. The supplied FF16 inventory fits one page. These
sample constraints are not presented as 100 distinct cross-page changes. The
generated regression fixture separately changes 100 distinct rows across 50
pages. The revert case restores all selected fields to their original values;
every resulting log is empty, with partial-coverage warnings retained where
appropriate. HTTP individual revert/navigation/revision behavior is verified by
the strengthened API contract test.

Boundary checks cover 9,999/10,000/10,001 rows, exactly 100 pages versus a remaining
row, and serialized UTF-8 byte budgets one byte below/at/above the exact page
size (including escaped Unicode). Existing checks cover zero/global budgets,
empty slots, cursor/offset paging, duplicate projections, malformed/failed pages,
view memory, lazy cycles, partial originals and proven additions/removals.
Every cut returns `complete:false` with warnings; exact terminal boundaries can
return complete without issuing a redundant page request.

## Changes

- Share navigation paths and collection/row labels across each row's cells.
- Move child traversal outside the recursive visit function to avoid a closure
  allocation for each visited node.
- Compare the bounded field dictionaries directly instead of allocating a union
  set of all field identities. Preserve original-first change ordering, alias
  precedence, labels, targets, revert eligibility and partial-coverage rules.

No plugin, web, SDK or API contract changes are part of the performance patch.
Existing collection deduplication and linear field matching were already present;
no current-view cache or new completeness shortcut was introduced.

All diagnostic F8 projections allocate less in every warm case. For 100 edits,
the reductions are 3.3% Pragmata, 24.9% Tales, 14.9% FF16 and 21.9% BG3.
Full-view FF16 allocations fluctuate by a few kilobytes. Timings are mixed:
three of the four 100-edit projections are slower in this matched run, while
FF16 is faster. These measurements establish allocation savings and bounded
operation costs; they do not establish a universal speedup.

## Reproduction

Use the environment setup from the task, then set `SAVETOOLKIT_API_MEASUREMENTS`
to a fresh absolute CSV path on D: and run:

```powershell
dotnet test --project tests/SaveToolkit.Api.Performance.Tests -c Release --filter-class '*SemanticReviewMeasurements'
./scripts/test.ps1 -Configuration Release -ResultsDirectory "$env:TEMP/results"
python -B docs/tests/test_documentation.py --code-ref main
```

The companion CSV `.metadata.txt` records collection sizes, completeness,
warnings, actual change counts, first-request original/current queries and warm
page counts. Retained evidence files are `final-before.csv`, `final-after.csv`,
their metadata, `final-before.log` and `final-release.log` under the scratch root.

## Warm measurements

MB means 1,000,000 managed bytes. Page counts and completeness are identical
before/after; exact allocation byte counts are retained in the CSV evidence.

### Full plugin views

| Plugin | Case | ms before → after | MB before → after | Pages |
| --- | --- | ---: | ---: | ---: |
| Pragmata | No edits | 247.186 → 194.495 | 572.352 → 571.679 | 0 |
| Pragmata | 1 edit | 336.604 → 240.468 | 572.352 → 571.679 | 0 |
| Pragmata | 100 edits | 395.615 → 376.048 | 572.354 → 571.681 | 0 |
| Pragmata | Edits reverted | 325.632 → 294.575 | 572.352 → 571.679 | 0 |
| TalesOfArise | No edits | 100.366 → 104.600 | 40.311 → 38.910 | 0 |
| TalesOfArise | 1 edit | 118.669 → 76.272 | 40.311 → 38.911 | 0 |
| TalesOfArise | 100 edits | 66.603 → 57.129 | 40.329 → 38.929 | 0 |
| TalesOfArise | Edits reverted | 66.257 → 67.967 | 40.315 → 38.915 | 0 |
| FinalFantasy16 | No edits | 7.602 → 8.024 | 19.634 → 19.618 | 0 |
| FinalFantasy16 | 1 edit | 5.262 → 6.184 | 19.610 → 19.618 | 0 |
| FinalFantasy16 | 100 edits | 6.136 → 6.404 | 19.617 → 19.622 | 0 |
| FinalFantasy16 | Edits reverted | 5.697 → 5.737 | 19.609 → 19.614 | 0 |
| BaldursGate3 | No edits | 92.480 → 156.369 | 75.401 → 74.001 | 0 |
| BaldursGate3 | 1 edit | 91.807 → 154.649 | 75.401 → 74.001 | 0 |
| BaldursGate3 | 100 edits | 124.438 → 137.091 | 75.402 → 74.002 | 0 |
| BaldursGate3 | Edits reverted | 118.142 → 178.683 | 75.401 → 74.001 | 0 |

### F8 diagnostic projection

| Plugin | Case | ms before → after | MB before → after | Pages |
| --- | --- | ---: | ---: | ---: |
| Pragmata | No edits | 135.201 → 114.061 | 116.845 → 112.968 | 15 |
| Pragmata | 1 edit | 143.982 → 290.154 | 116.845 → 112.968 | 15 |
| Pragmata | 100 edits | 110.924 → 135.088 | 116.852 → 112.975 | 15 |
| Pragmata | Edits reverted | 117.260 → 131.237 | 116.845 → 112.968 | 15 |
| TalesOfArise | No edits | 109.355 → 94.363 | 32.752 → 24.954 | 51 |
| TalesOfArise | 1 edit | 56.111 → 79.223 | 31.364 → 23.539 | 51 |
| TalesOfArise | 100 edits | 50.953 → 61.353 | 31.364 → 23.539 | 51 |
| TalesOfArise | Edits reverted | 73.296 → 43.138 | 31.363 → 23.539 | 51 |
| FinalFantasy16 | No edits | 2.009 → 1.836 | 2.981 → 2.534 | 1 |
| FinalFantasy16 | 1 edit | 2.289 → 1.696 | 2.982 → 2.535 | 1 |
| FinalFantasy16 | 100 edits | 1.932 → 1.730 | 2.999 → 2.552 | 1 |
| FinalFantasy16 | Edits reverted | 2.009 → 1.899 | 2.981 → 2.534 | 1 |
| BaldursGate3 | No edits | 16.489 → 13.833 | 16.072 → 12.543 | 17 |
| BaldursGate3 | 1 edit | 11.831 → 12.242 | 16.069 → 12.541 | 17 |
| BaldursGate3 | 100 edits | 12.336 → 14.349 | 16.087 → 12.558 | 17 |
| BaldursGate3 | Edits reverted | 14.902 → 12.839 | 16.069 → 12.540 | 17 |

## First review including original opening/capture

Single observations; current document setup is excluded. Pages show current + original.

| Plugin / view | ms before → after | MB before → after | Pages |
| --- | ---: | ---: | ---: |
| Pragmata / full-view | 643.057 → 1553.799 | 1207.053 → 1206.380 | 0 + 0 |
| Pragmata / F8-projection | 1413.565 → 1110.731 | 1324.212 → 1310.535 | 15 + 15 |
| TalesOfArise / full-view | 234.940 → 336.034 | 91.762 → 90.374 | 0 + 0 |
| TalesOfArise / F8-projection | 826.338 → 1078.666 | 301.019 → 286.713 | 51 + 51 |
| FinalFantasy16 / full-view | 20.769 → 21.985 | 40.971 → 40.955 | 0 + 0 |
| FinalFantasy16 / F8-projection | 141.699 → 160.438 | 241.259 → 240.345 | 1 + 1 |
| BaldursGate3 / full-view | 554.064 → 690.802 | 296.734 → 295.377 | 0 + 0 |
| BaldursGate3 / F8-projection | 1091.280 → 1450.966 | 783.445 → 776.542 | 17 + 17 |

## Validation

- Full `./scripts/test.ps1 -Configuration Release` with samples: 21 projects,
  7,375 tests; 7,368 passed, 7 skipped, zero failures.
- All eight real-sample performance cases passed. Without samples, all eight
  skip cleanly. The generated allocation/time gates, boundary cases and HTTP
  response-shape/navigation/revert tests pass in the full run.
- Build: zero warnings and zero errors. Documentation: all 35 checks pass.
- All temporary files and measurement evidence use the configured D: scratch;
  private samples remain external and unchanged. No push or remote merge.

The seven full-suite skips require either an extracted native bundle (three
packaging configuration cases) or the fictional PPSA99999 sample (four synthetic
conformance cases). None belongs to the real-sample performance cases.

Skipped full-suite cases:
- `SaveToolkit.Core.Tests.ApiBundleConfigurationTests.PackagedApphost_UsesUrlsAndStorageOverrides(source: "appsettings")`
- `SaveToolkit.Core.Tests.ApiBundleConfigurationTests.PackagedApphost_UsesUrlsAndStorageOverrides(source: "environment")`
- `SaveToolkit.Core.Tests.ApiBundleConfigurationTests.PackagedApphost_UsesUrlsAndStorageOverrides(source: "commandLine")`
- `SaveToolkit.Core.Tests.SyntheticFuzzConformanceTests.Edit_SurvivesSerializeAndReopen`
- `SaveToolkit.Core.Tests.SyntheticFuzzConformanceTests.Probe_ClaimsSampleFiles`
- `SaveToolkit.Core.Tests.SyntheticFuzzConformanceTests.Serialize_WithoutEdits_IsByteIdentical`
- `SaveToolkit.Core.Tests.SyntheticFuzzConformanceTests.Open_BuildsValidSerializableView`
