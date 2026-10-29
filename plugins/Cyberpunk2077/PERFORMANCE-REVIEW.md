# Performance review — 2026-10-29 (#40)

Scope: `plugins/Cyberpunk2077`, branch `review/perf-cyberpunk2077`.
Baseline: `0bcc815`. Fixes: `ca5d9ae` (compact merged facts), `91a5fce` (stable collection paging).
Largest supplied `sav.dat`: **1,302,721 bytes**, expanded **5,607,178 bytes**.

## Findings and changes

Two **medium** performance defects were fixed:

- Opening a document built dictionaries, provenance strings and getter closures for all **20,978**
  merged fact rows before the user requested any. `FactCatalogInspection` now retains compact
  identities and shared immutable `ReferenceRecord`s. Only requested rows get attributed cells
  and live nullable value getters. Metadata/counts remain available immediately.
- Default collection pages repeatedly ran an empty-filter predicate and sorted all stable native
  keys. `InspectionCollection` now creates one lazy key index per collection and directly slices
  ordinary pages, including descending and out-of-range requests. Filtered key-order pages reuse
  that index. Live values, filters, explicit value sorts and hidden-empty counts are recalculated.
  The index is document-local and lives only as long as its immutable collection model.

No confirmed findings remain deferred. RED package duplication was inspected and measured but is
**not a material performance defect on the supplied corpus**: the scriptable package is only
**15,252 bytes**. The baseline isolated reader allocated **61,288 bytes** (median **0.04 ms**);
progression decoding allocated **467,984 bytes** (median **0.24 ms**). Those paths were left alone.
This plugin reads CSAV and RED packages, not standalone CR2W files. This review adds no new wire
format assumptions, writer behavior, dependencies, shared caches or framework changes.

## Measurements

Temporary project, source, binaries and raw measurements are retained on D: at
`D:/Temp/saveeditor/benchmarks/review-perf-cyberpunk2077`.

Release/net10.0, .NET SDK **10.0.400**, x64 Windows, same machine. Build the baseline console project
before changing the plugin; build the same source against the fixes into a separate directory.
`Bench.csproj` references this worktree's plugin and Testing projects; its benchmark-only assembly
name uses the existing test friend declaration. Nothing is added to the production assembly surface.
The two output folders preserve their own plugin DLLs. No build or test runs during timed processes.

Sample import, disk read, and `MemorySaveSet` creation precede timing. Measure host pack load once,
then pin the same embedded packs. Each process measures one cold open/root/page; six untimed
open/root warmups precede nine measured documents. Each document measures open, root, ten repeated
roots and ten 50-row default fact pages, then verifies byte-identical no-op serialization outside
timing. Each page batch includes that document's first key-index construction. Isolated constructors
are measured separately. `Stopwatch` and `GC.GetTotalAllocatedBytes(true)` surround each operation.
Three fresh process pairs run baseline then current; warm medians pool **27** operations per variant.
Cold medians use **3** operations. Background runtime activity and other workloads can affect time
and allocation counters; these are local observations, not latency guarantees.

| Metric | Before: median time / allocated bytes | After: median time / allocated bytes |
| --- | --- | --- |
| Cold open + first root, excluding packs | 228.86 ms / 62,568,328 | 134.67 ms / 26,279,696 |
| Warm open | 112.88 ms / 61,351,968 | 54.68 ms / 26,099,016 |
| Warm open + first root | 113.04 ms / 61,416,304 | 54.95 ms / 26,168,104 |
| Warm first root alone | 0.24 ms / 64,336 | 0.23 ms / 69,088 |
| Ten default 50-row fact pages | 105.96 ms / 27,364,520 | 10.61 ms / 2,669,384 |
| Isolated merged catalog construction | 107.08 ms / 43,586,008 | 27.39 ms / 8,247,336 |

Warm open + root allocates **57.4% less**, and the ten-page batch allocates **90.2% less**.
The first root remains bounded; the improvement is mainly in constructing the document model.
No root-only speedup is claimed. Host embedded packs still load eagerly once, outside plugin scope.
Their unchanged code measured 232.67 / 203.75 ms in the paired run; that difference is timing noise.

Cold open + root raw times: baseline **289.81 / 228.86 / 194.22 ms**;
current **187.72 / 134.67 / 121.20 ms**. Complete per-operation data is in
`baseline-{1,2,3}.jsonl` and `current-{1,2,3}.jsonl`.

A separate retained-memory probe loads/pins packs and warms one document first, forces collection,
then opens/builds one live document and measures `GC.GetTotalMemory(true)` again. Keep the document,
root, input and context alive across the probe; repeat in three fresh matched process pairs.
Median additional retained managed memory falls from **42,454,264** to **19,971,640 bytes** (**53.0%**).
Raw baseline values: **42,455,008 / 42,454,048 / 42,454,264**; current:
**19,971,640 / 19,971,616 / 20,108,176**. Probe binaries/source and `memory-*-*.txt` are on D:.
This is a managed-heap delta, not process working set or peak memory.

## Regression and compatibility evidence

Before fixes, new regressions fail on unchanged production code:

- Catalog construction allocated **44,601,664 bytes**, exceeding the authored **16 MiB** budget.
  The budget excludes shared pack loading and the imported payload; no elapsed-time assertion.
- Three ascending pages plus one descending page evaluated **4,000** keys for a **1,000**-row model.
  The regression requires one lazy index (**1,000** evaluations), correct offset/direction, and
  exactly the **17** requested materialized rows. View construction must not build the index.

Additional characterization covers duplicate saved hashes with separate table identities,
canonical whitespace, unknown hashes, per-row attribution/confidence, absent null versus saved
zero, live getter restoration, and fresh search/filter/numeric-sort results after value changes.
Existing sample edit/undo, export, warnings, corruption, nullable and empty-slot tests remain green.

An independent D: verifier uses public plugin APIs with identical dependencies, substituting only
the baseline/current plugin DLL. Both produce identical serialized root/metadata/all-page hashes
for **25,266 rows across ten collections**, with empty rows explicitly included:
`1e39310326c84589c2bcbe94b8ef1da29b4575995a48291c9220badaeb316056`.
All **174 lazy sections** also match:
`51b997f59ad9522f2f6fa522fd7d65dfbfd498af468c5d167bc73c38df8567bb`.
Both independently preserve byte-identical no-op output. Verification source is retained as
`verify/VerificationProgram.txt`, with results in `verify/{baseline,current}.txt`.

Baseline suite: **168 passed / 0 failed / 0 skipped**.
Final complete Release suite: **172 passed / 0 failed / 0 skipped**.
`dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj`: **0 warnings / 0 errors**.
All handwritten C# files remain under 300 lines; changes stay entirely within the plugin folder.
No sample data, benchmark binaries or scratch files are committed. No push or merge.

Every test/build/measurement command exports the required environment in that same shell:

```powershell
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$env:SAVETOOLKIT_TEST_TEMP='D:/Temp/saveeditor/test-scratch'
$env:TEMP='D:/Temp/saveeditor/test-scratch/review-perf-cyberpunk2077'
$env:TMP=$env:TEMP
dotnet test --project plugins/Cyberpunk2077/SaveToolkit.Plugins.Cyberpunk2077.Tests/SaveToolkit.Plugins.Cyberpunk2077.Tests.csproj -c Release
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj --verbosity quiet
```

Format research was checked against the already-attributed pinned
[WolvenKit scriptable package parser](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Save/Parser/ScriptableSystemsContainerParser.cs).
No upstream code was downloaded/executed or copied by this performance change.
