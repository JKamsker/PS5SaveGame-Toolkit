# Performance review (#40)

Measured on 2026-10-29 with .NET 10, Release, Windows x64. The largest supported
sample files are BL3 **1,567,384 bytes / 38 serials** and Wonderlands
**538,381 bytes / 19 serials**. Samples and extracted data remain external.
The F7 input pairs the largest character with the same-generation/backup profile.

`PerformanceMeasurements` runs alone in a fresh test process. Pack-store construction,
fallback initialization and the first root view are single first-use observations;
the other phases have one warmup and nine repetitions, reporting separate medians
for elapsed time and current-thread managed allocation. Archive extraction is excluded.
Assertions and necessary save-file reads are included on both sides. These are
characterizations, not timing assertions, browser measurements, retained-heap sizes
or console performance promises. Other processes, tiered JIT, GC and scheduling can
substantially change elapsed times; allocation differences are the stronger evidence.

The table is a historical 2026-10-29 comparison, not a fresh measurement of current HEAD.
Before: production code at `b5d6f4d`; after: `4d03e05` and `6ec24b5`, using the
same benchmark source and machine. Raw TRX results are retained under
`D:/Temp/saveeditor/benchmarks/review-perf-gearbox/` (not committed); build outputs
and disposable scratch use `D:/Temp/saveeditor/test-scratch/review-perf-gearbox/`.

## Results

Each cell is **milliseconds / allocated bytes**, before → after.

| Phase | BL3 | Wonderlands |
| --- | --- | --- |
| New F2 store, both embedded packs | 405.786 / 163,568,656 → 549.085 / 163,581,856 | 512.537 / 163,403,592 → 403.865 / 163,158,808 |
| First fallback access | 125.889 / 61,859,200 → 3.633 / 327,280 | 255.992 / 39,001,136 → 0.320 / 194,528 |
| First root view, after fallback access | 18.202 / 202,560 → 71.617 / 13,545,888 | 1.563 / 130,744 → 17.104 / 5,800,720 |
| Pin existing store | 0.001 / 3,840 → 0.004 / 3,840 | 0.002 / 3,840 → 0.003 / 3,840 |
| Open | 8.862 / 19,809,176 → 11.964 / 19,809,352 | 1.276 / 7,128,160 → 2.208 / 7,127,184 |
| Open + root | 9.505 / 19,967,224 → 10.443 / 19,967,400 | 5.537 / 7,257,504 → 1.728 / 7,257,288 |
| Open + root + first 20 rows of every collection | 22.484 / 25,562,944 → 33.763 / 25,569,504 | 13.250 / 10,510,816 → 11.635 / 10,544,256 |
| Expand first item details | 0.052 / 115,304 → 0.066 / 115,376 | 0.048 / 93,160 → 0.050 / 95,104 |
| Serialize no-op, including output verification | 27.516 / 69,360,824 → 74.234 / 69,361,160 | 11.868 / 25,918,120 → 6.088 / 25,917,528 |
| Decode all serials, uncached reader | 0.293 / 177,264 → 0.677 / 177,264 | 0.042 / 63,288 → 0.152 / 66,024 |
| Encode all original envelopes | 0.033 / 2,464 → 0.045 / 2,464 | 0.015 / 1,080 → 0.020 / 1,080 |
| F7 propose, including member validation | 77.971 / 96,093,488 → 90.090 / 96,085,928 | 9.117 / 39,674,432 → 15.694 / 39,665,016 |
| F7 open + root | 100.387 / 121,384,160 → 84.291 / 97,982,248 | 10.999 / 50,019,720 → 12.275 / 40,486,160 |
| F7 validate + open + root | 129.096 / 217,390,384 → 110.164 / 193,975,768 | 34.135 / 89,611,248 → 44.417 / 80,065,280 |

Lazy loading moves work from fallback construction into the first consumer. Adding
the first fallback and root phases gives **62,061,760 → 13,873,168 bytes** for BL3
and **39,131,880 → 5,995,248 bytes** for Wonderlands: approximately **78% / 85%**
less allocation before an item page opens. The observed combined times are
144.091 → 75.250 ms and 257.555 → 17.424 ms. Opening a serial page subsequently
loads its pinned indices/families once; this does not eliminate their eventual cost.

F7 open + root allocates approximately **19% less** for both titles. Each input is
read once instead of three times, and the document that passes byte-identical
round-trip validation is retained. The integrity check reads actual root serials
without creating the session identity index. Topology, corrupt input, no-op output,
transfers, history and fresh-instance restoration remain covered by tests.

## Review dispositions and limits

- Fixed, medium: eager all-dataset fallback (`4d03e05`). Immutable `Lazy` instances
  load individual catalogs and parsed widths once, including concurrent first access.
- Fixed, medium: F7 discarded validated documents (`6ec24b5`). Public validation
  and direct group opening retain the same round-trip requirements. Public validation
  remains independent; there is no cache of potentially unrelated group requests.
- No deferred plugin findings. F2 store construction remains a measured startup
  cost in Core, outside this plugin's edit scope. Images remain host validated/cached.
- Existing default/index F8 paging already decodes only visible inventory rows;
  searches and non-index sorting intentionally inspect the complete model. The
  existing 5,000-item bank tests cover middle pages, search, duplicate row identities,
  decode-cache reuse and byte-identical export. No speculative codec rewrite was made.
- Experimental maturity, write warnings, unknown tails and pinned binary authority
  are unchanged. No additional game-format claim or console validation is implied.

Research checked the already pinned apocalyptech readers before changing loading:
[BL3 datalib.py](https://github.com/apocalyptech/bl3-cli-saveedit/blob/d459b28a7e88f1a5ae0f16118272d8a5383b8b04/bl3save/datalib.py)
and [Wonderlands datalib.py](https://github.com/apocalyptech/ttwl-cli-saveedit/blob/1fde90dc43e7f7ee261413d85c366e0a7236dd9b/ttwlsave/datalib.py).
Both defer reference initialization until an operation requires it. Per-catalog
thread-safe lazy loading here is a C# implementation choice; no new upstream code
or data was imported, and no serial/checksum algorithm changed.

## Reproduce

From the repository root, replace the archive-directory placeholder. Run this class
alone so unrelated tests have not initialized the static fallback. All scratch and
benchmark/build output goes to D:; the tests import archives through `SampleSaves`.

```powershell
$env:SAVETOOLKIT_ARTIFACTS = '<existing sample archive directory>'
$env:SAVETOOLKIT_TEST_TEMP = 'D:/Temp/saveeditor/test-scratch'
$env:TEMP = 'D:/Temp/saveeditor/test-scratch/review-perf-gearbox'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null
dotnet test --project plugins/Gearbox/SaveToolkit.Plugins.Gearbox.Tests -c Release `
  --artifacts-path "$env:TEMP/build" --filter-class '*PerformanceMeasurements' `
  --report-xunit-trx --report-xunit-trx-filename benchmark.trx `
  --results-directory "$env:TEMP/benchmarks"
```

`SerialDatabaseLoadingTests` uses isolated fallback instances and a generous 2 MiB
allocation budget for manifest, skills-only and widths-only operations, independent
of static cache warmth. Six cases exceeded that budget before the change.
`GroupOpenPerformanceTests` reproduced three full reads per member before the fix,
then asserts one read, byte-identical outputs, corrupt-member rejection and invalid
role rejection before reading payloads. Existing group tests exercise lost-loot
insertion before inventory indexing and whole-group undo/redo.

Final validation for that performance round: **197 passed / 0 failed / 0 skipped**, compared with the baseline
**179 / 0 / 0**. The first regression run was **8 failed / 6 passed / 0 skipped**;
the additional two concurrent-initialization cases were added after the fallback fix.
The API build completed with **0 warnings / 0 errors**. All 11 changed repository
files are within `plugins/Gearbox/`; handwritten C# remains below 300 lines.

The later test-quality review (`1094dcc`, 2026-10-29) added current-thread allocation budgets to
each `PerformanceMeasurements` phase and to first/repeated asset loads. Elapsed times remain
diagnostic only. The current every-sample open test enforces a warmed 32 MiB allocation budget;
the 5,000-item bank checks use 12 MiB for view/page creation and 256 KiB for a cached page.
Those assertions do not turn the historical elapsed-time or allocation figures above into current
measurements. See the README's dated documentation audit for the subsequent full-suite result.
