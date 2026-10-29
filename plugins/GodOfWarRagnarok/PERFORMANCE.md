# Performance review (#40)

2026-10-29, Windows x64, .NET 10.0.11, Release. Baseline commit:
`8861d560ab5f645a228790c13e5431c2d3cf2d7e`. Private sample: primary
`GOWRSAVE1`, generation 0, 3,217,731 bytes, the largest imported record.

## Measurement

Reproducible harnesses and raw results are retained outside the checkout at
`D:/Temp/saveeditor/review-results/perf-godofwarragnarok/`:

- `harness/Perf.csproj`, `before.txt`: initial five warmups and 21 measured iterations.
- `paired/Perf.csproj`, `paired.txt`: the original plugin snapshot and changed plugin loaded
  side by side, using the same sample and SDK/Core/Testing assemblies. Twenty warmups for
  opening, 1,000 for queries/actions, then 41 alternating measured iterations per implementation.
- `baseline-source.zip`: tracked plugin source at the baseline commit, without private samples.
  Its scratch project changes only assembly identity and the absolute SDK reference for the harness.
- Red/green/final TRX directories preserve deterministic regression evidence.

Both harnesses use synchronous `GC.GetAllocatedBytesForCurrentThread` deltas and median
`Stopwatch` observations. Sample import, pack loading and query setup are excluded. Opening includes
the input copy, full parser and first `BuildView`; pages contain up to 20 rendered rows. Selected
apply includes a reduction, preview, revalidation and restore. Values below are paired medians;
timings depend on machine load, JIT tier and GC, and are never test pass/fail conditions.

| Operation | Before ms | After ms | Before allocated B | After allocated B |
| --- | ---: | ---: | ---: | ---: |
| Open + first view, direct fallback | 9.9209 | 10.1764 | 6,044,384 | 5,816,184 |
| Open + first view, pinned packs | 5.7640 | 5.3583 | 6,005,488 | 5,782,592 |
| Resource page, offset 200 | 0.2381 | 0.0336 | 294,480 | 108,648 |
| Resource search, padded Hacksilver text | 0.2785 | 0.0558 | 266,496 | 17,312 |
| Resource descending quantity sort, offset 200 | 0.3074 | 0.1034 | 304,944 | 114,600 |
| Resource exact quantity filter, zero | 0.0990 | 0.0282 | 270,880 | 107,152 |
| Directory numeric word sort, offset 600 | 0.1583 | 0.0792 | 165,832 | 48,528 |
| Selected unchanged-row restore preview | 0.0173 | 0.0004 | 18,824 | 576 |
| Document unchanged-row restore preview | 0.0149 | 0.0110 | 13,952 | 11,872 |
| Selected changing restore application | 0.1177 | 0.0031 | 619,176 | 5,016 |

The direct-open timing above is slightly higher; no direct-open latency improvement is claimed.
The separate `paired-no-tiering.txt` run records optimized-JIT diagnostics. Opening allocations and
lookup call counts are the deterministic improvement. Short warmups initially exposed excessive
numeric comparator work; the final query projects each typed sort key once per row.

Run from PowerShell, with the original checkout available at the harness project-reference paths:

```powershell
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$env:SAVETOOLKIT_TEST_TEMP = 'D:/Temp/saveeditor/test-scratch'
$env:TEMP = 'D:/Temp/saveeditor/test-scratch/review-perf-godofwarragnarok'
$env:TMP = $env:TEMP
dotnet run --project D:/Temp/saveeditor/review-results/perf-godofwarragnarok/paired/Perf.csproj --configuration Release
dotnet test --project plugins/GodOfWarRagnarok/SaveToolkit.Plugins.GodOfWarRagnarok.Tests --configuration Release
```

## Changes and regression contracts

- Reference resolutions, including absent mappings, names and attribution are cached per document.
  Pinned contexts are immutable. Resolution precedence and separate-context isolation remain tested;
  100 repeated known/unknown lookups make zero further catalog calls and allocate at most 1 KiB.
- Queries retain immutable string projections and read current quantity words for predicates and
  sorting. Native pages reuse physical order. Only sorted/filtered model references and typed sort
  keys are materialized; rich cells and bindings are constructed for the requested page only.
  There is no query-result cache to invalidate after edits or mutable filter dictionaries.
- Six synthetic one-row query cases over 1,024 duplicate-ID rows have a 64 KiB allocation ceiling.
  The previous implementation allocated 691–991 KiB in these reproductions. The ceiling permits
  linear arrays of references/typed keys for sorting, while rejecting full rich-row projections.
  Full traversals check every resource column in both directions and ascending native-key ties.
  All seven schemas check complete predicates, numeric ordering, paging and byte preservation;
  live quantity edits/restores include full-u32 values and exact decimal-text filtering.
- Selected restore planning resolves only selected native identities, rejects duplicate/unknown
  keys and preserves physical order. Applying a revalidated plan binds only changed targets.
  Empty/one-row previews have a 4 KiB ceiling; a one-target apply has a 12 KiB ceiling and leaves
  unrelated fields unpublished. Its previous allocation was 2,089,040 B in the synthetic case.
  Exact output bytes, serialized-output reopening and stale-plan rejection are asserted.
- The largest sample has a 5,950,000 B open/first-view ceiling. Its resource page/search/sort and
  directory-sort ceilings are respectively 128,000/32,000/128,000/56,000 B. Sample tests skip cleanly
  when private samples are unavailable; all new synthetic cases run without samples.

No decompression is performed by these paths. Parsing on `Open` and validation on `Serialize` are
intentional. No quadratic lookup defect was established. CRC probes and all integrity evidence
remain unchanged; neither a match nor a negative search establishes authentication or repair.
Experimental maturity, import ceilings, write warnings and unknown bytes remain intact.

Existing format research was rechecked against the pinned
[Continue? reader](https://github.com/Alexsander720/continue/blob/51e19b2a292c60f9acdf3c13c561809657104ea2/plugin/py_modules/ddsaves/god_of_war_ragnarok.py).
No new layout, checksum, compression or gameplay claim was introduced.

## Validation

The performance-round baseline had 170 passing cases. That round's final Release validation recorded **218 passed, zero failed and
zero skipped** with private samples, including all 156 archive-record byte-identical round trips,
the recent robustness and CRC migration characterizations and existing write/history tests.
No original test case was removed. With the artifacts variable explicitly unset in the same shell,
**170 pass and 48 cleanly skip** (the original 43 sample cases and five new sample allocation cases).
All 43 added synthetic cases execute without samples. API Release build with `--warnaserror` has
zero warnings and errors. All handwritten plugin C# remains under 300 lines.

These are historical performance-round measurements and test totals, not current-suite counts.
Subsequent maintainability and documentation validation is recorded in [README.md](README.md#bounds-and-tests).
