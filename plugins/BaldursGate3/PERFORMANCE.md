# Performance and memory review (#40)

Reviewed 2026-10-29 on `review/perf-baldursgate3`, starting at `0856545`.
All five confirmed findings were medium severity and are fixed:

| Finding | Fix | Commit |
| --- | --- | --- |
| Package validation and loading decompressed every entry twice, including the 33.6 MB story payload. | Transfer the expanded validation buffers to document/writer consumers; ordinary extraction still owns an independent buffer. Mandatory v18 digest validation remains before return. | `25c7d4c` |
| Valid LSF attributes formatted failure messages for every row. | Format type/size/nesting diagnostics only on failure, retaining the original errors. | `3842549` |
| EntityId decoding and owner-to-row joins repeatedly rebuilt immutable data. | Lazy per-model occurrence cache, component-name index and per-access owner-row indexes. Duplicate occurrences, ambiguous joins, width validation and independent lookup arrays remain. | `d674711` |
| Every default ten-row live page constructed descriptors/search text for the entire collection. | Page before constructing row descriptors; compute search text only for text queries. Filters/sorts still examine the complete current model. | `a465db1` |
| Unowned inventory fallback scanned the growing output for every slot: quadratic comparisons. | Track covered container/entity pairs in a hash set. Distinct paths and ambiguous cross-container occurrences remain visible. | `bd917fe` |

## Largest-sample measurements

Windows x64, .NET SDK 10.0.400 / runtime 10.0.10, Release. Sample:
`AutoSave_0.lsv`, 8,073,350 bytes; `Globals.lsf` has 149,174 nodes and
189,357 attributes. No sample or extracted bytes are committed.

`PerformanceMeasurementTests` runs one cold pass and five warm passes in one
process. Sample import and phase-specific fixture preparation are excluded.
Allocations use `GC.GetAllocatedBytesForCurrentThread`; phases are synchronous.
Warm figures below are medians of the five passes. "First view" is Open + root
view + collection metadata + the first ten rows of every collection, without a
reference pack. It is separate from the older pack/icon benchmark in README.

| Phase | Before ms | After ms | Before allocated bytes | After allocated bytes |
| --- | ---: | ---: | ---: | ---: |
| Open | 280.82 | 155.74 | 205,680,112 | 118,578,920 |
| Root view | 4.76 | 5.85 | 3,005,752 | 3,008,096 |
| Collection metadata / first live decode | 63.70 | 45.83 | 18,064,432 | 15,764,680 |
| First ten rows of every collection | 5.88 | 1.25 | 6,894,240 | 850,256 |
| First view, median of per-pass totals | 350.57 | 211.20 | 233,644,536 | 138,201,640 |
| Twenty ten-row live-value pages | 58.06 | 0.93 | 51,014,488 | 901,984 |
| Globals LSF parse | 92.05 | 78.91 | 95,363,968 | 58,212,224 |
| Globals live NewAge decode | 65.95 | 41.86 | 17,570,872 | 15,298,400 |
| Twenty EntityId reads, including first decode | 47.71 | 1.83 | 47,955,288 | 2,397,872 |
| 1,000 owner lookups, including first row index | 7.89 | 0.11 | 13,170,904 | 84,256 |
| First story-marker expansion | 37.08 | 31.54 | 1,368 | 1,368 |

The cold first-view observation was **656.66 -> 463.65 ms** and
**234,851,120 -> 138,921,000 allocated bytes**. Warm first-view allocation fell
41%; default live paging allocation fell 98%. These are allocations, not retained
heap size or process working set. Entity/owner caches retain bounded original
data for the model/access lifetime; no global save cache is introduced.

Wall-clock timing is noisy on this shared machine. Separate after runs had warm
Open medians of 163.58 and 234.49 ms, and live decode medians of 47.94 and 97.55 ms.
Do not infer a latency guarantee from these stopwatch observations. The unchanged
story pass and root view demonstrate timing variation; no story-scan fix is claimed.
The primary comparable after run executes the measurement class alone, like baseline.

The additional synthetic 50,000-slot unowned inventory graph measured
**1,862.21 -> 155.70 ms** in isolated Release tests. It remains within the existing
100,000-slot parser budget. The regression runs outside parallel test collections.

Raw before/after JSON and full-suite/benchmark TRX are retained on D: at
`D:/Temp/saveeditor/benchmarks/review-perf-baldursgate3/`:
`before.json`, `after-isolated.json` (table above), `after.json`, `after-final.json`,
`full-suite.trx` and `benchmarks.trx`.

## Reproduce

Run the measurement class alone, with private samples available. Change the output
filename for each revision; do not compare a parallel full-suite run to an isolated run.

```powershell
$scratch = 'D:/Temp/saveeditor/test-scratch/review-perf-baldursgate3'
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$env:SAVETOOLKIT_TEST_TEMP = 'D:/Temp/saveeditor/test-scratch'
$env:TEMP = $scratch
$env:TMP = $scratch
$env:BG3_PERF_OUTPUT = "$scratch/measurement.json"
dotnet test --project plugins/BaldursGate3/SaveToolkit.Plugins.BaldursGate3.Tests -c Release --filter-class '*PerformanceMeasurementTests'
```

For a fresh baseline comparison, copy this measurement class and its named test
collection definition into a separate baseline checkout; they do not depend on the
new parser APIs. Keep that checkout's additional build/benchmark scratch on D:.
The first-view totals are calculated per iteration before taking the warm median;
they are not sums of independently selected phase medians.

## Validation and limits

- **247 passed, 0 failed, 0 skipped** with private samples; API Release build:
  **0 warnings, 0 errors**. All changes are inside `plugins/BaldursGate3/`.
- Added 25 regression/characterization/measurement cases. Nine allocation cases
  and the inventory scaling case were confirmed failing before their production fix.
  Controls cover every codec, legacy package versions, digest rejection, duplicate
  entity occurrences, ambiguous owners, width checks, all live collection pages,
  edited-value search/filter/sort, and cross-container duplicate items.
- Existing conformance, byte-identical no-op, intended-byte changes, independent
  reopen, repeated edit/undo/redo and sample identity/inventory census tests pass.
  Write maturity remains Experimental with existing warnings.
- Not a bug: mandatory full LSF/resource validation and v18 digest expansion at
  Open; the already-lazy story-marker pass; lazy raw/resource paging. The fix reuses
  validation work rather than skipping validation. Live collections still decode
  their supported source model when requested; search/sort necessarily inspect the
  full current model.
- Deferred findings: none. Remaining format/console-validation gaps are tracked in
  the existing NEWAGE/EVIDENCE docs and #31, outside this performance review.

Format research was rechecked against the existing pinned
[Norbyte package reader](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/PackageReader.cs),
[package writer](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/PackageWriter.cs)
and [NewAge research](https://github.com/DanFinchUK/bg3-savefile-parser/blob/9578ff7c46a1aa40c805f6b7beecf907f10fb3b8/FORMAT.md).
No new format interpretation or upstream implementation is imported. Existing
source/licence attribution remains in README and THIRD-PARTY-NOTICES.
