# Performance review (#40)

Measured 2026-10-29 on Windows x64, .NET 10.0.11, Release, comparing
`e5223e3` with the performance fixes on `review/perf-insomniac`.
The supported corpus has 76 Rift Apart records. Its largest file is
`i29slot0-s-snapshot-108976056/i29-s.save`, 577,928 bytes, with 285,264
scalar DDL values. Spider-Man 2 is not declared by this plugin and has no
identified sample in the supplied corpus; these measurements do not establish
support or performance for that title.

## Method and reproduction

The standalone harness and project are retained at
`D:/Temp/saveeditor/review-results/perf-insomniac/harness/Program.cs` and
`Perf.csproj`; `run-comparison.ps1` in the parent directory reproduces both
revisions. All harness output, imports, scratch and benchmark builds stay on D:;
referenced source projects retain their normal in-tree bin/obj directories.
No sample bytes are committed.

The harness references this checkout's plugin and Testing projects. Its assembly
name is `SaveToolkit.Plugins.Insomniac.Tests` to use the existing internal test
access, without reflection or changes to production visibility. Run from the
checkout, with the brief's external sample path assigned:

```powershell
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$env:SAVETOOLKIT_TEST_TEMP = 'D:/Temp/saveeditor/test-scratch'
$env:TEMP = 'D:/Temp/saveeditor/test-scratch/review-perf-insomniac'
$env:TMP = $env:TEMP
$env:DOTNET_TieredCompilation = '0'
dotnet run --project D:/Temp/saveeditor/review-results/perf-insomniac/harness/Perf.csproj -c Release
```

Each synchronous operation has three warmups and eleven measured repetitions
(five for reference-pack construction). Timings and allocations report their
separate medians. Allocation measurement uses
`GC.GetAllocatedBytesForCurrentThread`; timings use `Stopwatch.GetTimestamp`.
Tiered compilation is disabled identically for both comparison runs. Timings
are diagnostic host observations, not statistical claims or test gates.

Sample discovery/import, fixture construction and reference-pack pinning are
outside document/query measurements. Open + first view creates and disposes a
new document each time, so document-local caches are cold. First-query runs
prepare a new registry before every measured query; repeated-query runs reuse
one registry. The authored fixture has 20,000 reverse-ordered native keys,
Even/Odd text, numeric values modulo 101, and ten-row pages. Native ordering
is now established at registration; that cost is included in document Open,
but excluded from the individual synthetic query timers.

## Results

| Operation | Before bytes | After bytes | Before ms | After ms |
| --- | ---: | ---: | ---: | ---: |
| Largest sample DDL parse | 42,309,792 | 14,844,184 | 8.201 | 6.395 |
| Open with pinned packs | 47,727,256 | 20,131,544 | 21.886 | 10.420 |
| Open + first view with pinned packs | 48,024,448 | 20,430,648 | 22.692 | 12.089 |
| Rebuild view on the same document | 286,256 | 286,256 | 3.980 | 3.720 |
| Sample upgrade page (1,650 rows; page of 50) | 277,432 | 85,056 | 0.104 | 0.023 |
| Sample upgrade search | 600,472 | 184,656 | 0.409 | 0.154 |
| Sample upgrade repeated sort | 290,976 | 85,056 | 0.386 | 0.024 |
| Bolt plan | 968 | 968 | 0.001 | <0.001 |
| 20k first native page | 2,324,880 | 4,096 | 1.161 | 0.014 |
| 20k repeated native page | 2,324,880 | 4,096 | 1.292 | 0.004 |
| 20k first search | 3,644,944 | 4,128 | 3.411 | 0.718 |
| 20k repeated search | 3,164,944 | 4,128 | 2.265 | 0.547 |
| 20k first column sort | 2,485,224 | 724,672 | 1.949 | 6.882 |
| 20k repeated column sort | 2,485,224 | 4,096 | 1.631 | 0.002 |
| 20k first search + sort | 3,725,216 | 724,704 | 3.399 | 8.690 |
| 20k repeated search + sort | 3,245,216 | 4,128 | 2.261 | 0.572 |

Open + first view allocates about 57% less. The first requested column order
now sorts the complete collection once, replacing LINQ's old partial page sort;
the cold 20k sort is slower. Subsequent pages reuse it. Each collection retains
only one additional row-reference array, replacing it when the sort changes;
filters/search text are never cached. Filtering still scans the complete model
to report exact counts, without a matching-row array or per-row allocations.
No unbounded query cache or per-column cache is introduced.

## Guardrails and evidence limits

Allocation tests cover a 100,003-value packed DDL array (5 MB budget), a truncated
524,288-value declaration (32 KiB rejection budget), cold/repeated native pages,
search, filters, numeric sort and empty pages (32 KiB page budget), and the first
20k column sort (1 MB budget). Exact page keys, numeric/text ordering, ordinal
ties, counts, registration replacement and cell-build counts accompany these
bounds. Largest-sample Open + first view has a 24 MB budget; 100 bolt previews
have a 4 KiB-per-preview budget and must preserve the counter.

The initial seven allocation cases produced six failures on the old code. The
completed twelve-case allocation suite produces ten failures / two passes on
the old code: truncated-count allocation and small bolt planning are existing
correct behavior. No wall-clock assertion was added.

DDL list capacities use validated aggregate counts capped by remaining bytes
and minimum wire widths. Reads, validation order and error text stay intact.
Pre-body DAT1 size validation and decoder validation-before-output remain in
place; their tests now assert exact existing messages. Parsing once at Open is
shared with views; Serialize intentionally reparses edited bytes to validate
the output. Fog values remain fully validated even when evidence output is
bounded by host limits. Existing Experimental maturity and write warnings remain.

Format research rechecked the already-cited
[Rivet SaveData revision](https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/Rivet/Data/SaveData.cs),
[ALERT DAT1](https://github.com/Tkachov/ALERT/wiki/DAT1) and
[ALERT DDL](https://github.com/Tkachov/ALERT/wiki/DDL).
No external implementation was copied or executed, and no new wire-format,
checksum or title-support interpretation was introduced.

## Validation

- Baseline complete corpus: 265 passed, 0 failed, 0 skipped.
- Final complete corpus: 279 passed, 0 failed, 0 skipped; fourteen added cases.
- Final artifacts-unset run: 204 passed, 0 failed, 75 expected sample skips.
- API Release build with `--warnaserror`: 0 warnings, 0 errors.
- All 76 existing no-op records, edit/reopen/history and corruption tests pass.
- TRX counters, logs, original-code failing regressions and both measurement
  reports are retained under the D: evidence directory above.
