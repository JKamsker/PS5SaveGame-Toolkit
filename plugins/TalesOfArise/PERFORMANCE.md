# Performance review (#40, 2026-10-29)

Branch: `review/perf-talesofarise`. Baseline: `77136a2` (312 tests; includes the
TalesOfArise robustness review through `549e4ce`).
All three findings are medium severity and fixed; none are deferred.

## Measurements

The historical external harness is preserved at
`D:/Temp/saveeditor/review-results/perf-talesofarise/harness/` (`Harness.csproj`,
`Program.cs`). Its project references and linked `AriseTestContainer` fixture use
absolute paths into the original review worktree (`t3-89aad582`), not whichever
checkout is current. No sample bytes are written
by the harness. The imported archive uses the shared process-owned D: scratch.

Release .NET 10.0.11, Windows 10.0.26200, three warmups followed by eleven
synchronous iterations. Each column is independently the median of its eleven
observations. Allocation uses `GC.GetAllocatedBytesForCurrentThread`; elapsed
time uses `Stopwatch` and is diagnostic, never an assertion. The input is
`SaveData_002.sav`, 760,856 bytes; all six supplied gameplay files have that size.
Archive import, input loading/MemorySaveSet creation, reference-pack loading and
static initialization happen before measurement. Open includes its own file read
and first `BuildView`, including graphs/maps when references are pinned. These are
plugin timings, excluding HTTP, JSON transport and browser rendering.

| Operation | Before ms | After ms | Before allocated B | After allocated B |
| --- | ---: | ---: | ---: | ---: |
| Decode | 6.962 | 4.995 | 2,659,640 | 1,351,784 |
| Open + first view, no reference pack | 7.608 | 4.479 | 3,621,312 | 2,303,936 |
| Open + first view, pinned references | 10.598 | 7.813 | 6,221,392 | 4,903,840 |
| Encode including decode/self-check | 14.818 | 9.719 | 4,763,248 | 2,703,032 |
| Evidence offset page | 3.590 | 0.021 | 4,260,032 | 20,128 |
| Evidence descending numeric sort | 4.008 | 1.077 | 4,260,192 | 566,824 |
| Evidence text search | 12.991 | 5.490 | 11,268,120 | 282,464 |
| Inventory first page | 0.192 | 0.209 | 199,360 | 201,120 |
| Equipment rich search | 0.357 | 0.400 | 956,112 | 956,112 |
| Equipment details sort | 0.755 | 0.720 | 1,546,512 | 1,546,512 |
| Document quantity planning | 0.157 | 0.206 | 156,984 | 156,984 |
| Apply one row, generated full inventory | 4.757 | 0.480 | 8,328,720 | 276,584 |

Evidence cases use the largest section, `QuestSaveDataEx` (34,136 words): 20 rows
at offset 12,000 for paging/sorting, and text `0000` at offset zero for search.
Inventory/catalogue pages contain 20 rows. The generated action fixture has 934
recognised consumables; only one unloaded row is selected. Fixture/document/plan
creation is excluded from its apply measurement. Apply still includes fresh plan
revalidation, binding, atomic edits and disposal. The small inventory-page
allocation increase comes from the extracted quantity-binding closure; it is
about 1.8 KB for 20 rows, while avoiding megabytes of unrelated action rendering.

Before rerunning, copy the harness into your own D: scratch and update all three
paths in `Harness.csproj` (plugin, Testing and the linked fixture) to the intended
checkout. Retain its assembly name so the fixture can use the plugin's internal APIs.
The following command assumes that prepared copy is under `$taskScratch/harness`:

```powershell
$taskScratch = 'D:/Temp/saveeditor/test-scratch/docs/accuracy-talesofarise'
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$env:SAVETOOLKIT_TEST_TEMP = $taskScratch
$env:TEMP = $taskScratch
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force $env:TEMP | Out-Null
dotnet run --project "$taskScratch/harness/Harness.csproj" -c Release
```

`before.txt` and `after.txt` retain the complete reported measurements; intermediate
`after-cipher.txt` and `after-words.txt` isolate the first two changes. Timings vary
with system load; allocation reductions and deterministic test guards are the
regression evidence.

## Changes and guards

- `5a82eab`: one AES ECB transform per evolving-key section, retaining 64-byte XOR
  feedback and section key evolution. Decode no longer clones the source payload
  before making its isolated destination. Tests first failed at 90,648 B versus
  624 B for 1,024 versus one feedback chunks, and 2,659,640 B for decode. Current
  guards permit only 2 KiB allocation growth for chunk count, less than 1,600 KiB
  for decode, and less than 3 MiB for verified encode. Independent cipher vectors,
  whole-plaintext sample digests, shuffled chains, unknown-byte preservation and
  byte-identical no-op output remain the compatibility oracles.
- `2ae57a4`: unfiltered offset paging visits only requested words. Filtered offset
  order needs no sort; numeric sorting reads current bytes, retains ascending
  offset ties, and builds no stale index. Search/filter formatting uses a stack
  buffer and avoids per-word closures/enumerator allocation. Three allocation
  regressions first failed at 4.26–11.36 MB. Guards are 128 KiB for an offset page
  and 1 MiB for sorted/searched pages. Twelve other authored cases pin exact
  membership, both directions, ties, combined filters, live edits and page edges.
- `0acaf68`: prepare only non-derived scalar bindings in the revalidated action
  plan. Shared quantity binding no longer requires rich row construction,
  reference inspection, names or icons. The failing-first generated regression
  observed 1,868 catalogue `Find` calls for one selected row; it now asserts zero.
  A second regression caps one-row apply at 512 KiB; the call-count case verifies
  an unloaded tail target and undo. A dense document-plan characterization caps allocations at
  1 MiB and asserts zero reference lookups. Existing ActionPlans diagnostics,
  warning order, stale-plan rejection, exposed numeric aliases and Core grouped
  undo/redo coverage remain green.

Collection and planning costs outside these findings remain bounded: inventory
scans 934 fixed records; document planning uses a key dictionary; structural
validation checks only 30 schema sections. Rich catalogue searches intentionally
inspect descriptions/attributes across bounded catalogue pages, and details sort
needs complete matching membership. Their measured costs do not establish another
defect. No repeated decryption occurs in paging or planning; encode's decrypt and
plaintext comparison are deliberate integrity checks and remain enabled. The
first-view graphs are bounded and cached per document. No broader lazy-view or
catalogue index migration is claimed.

## Validation and sources

Baseline Release suite: **312 passed / 0 failed / 0 skipped**. Final Release suite:
**332 passed / 0 failed / 0 skipped**, twenty added cases. All 147 generated
corruption cases remain green. API Release build with `--warnaserror`:
**0 warnings / 0 errors**. TRX files, red/green evidence and build output are in
`D:/Temp/saveeditor/review-results/perf-talesofarise/`. All authored changes are
inside `plugins/TalesOfArise/`; Experimental maturity and write warnings remain.

Format research remains the pinned
[save-converters notes](https://github.com/ItsAllAboutTheCode/save-converters/blob/663239063e7c1cc8550f4248f772ee9dd47ed5b0/save_convert/tales_of/arise/save_NOTES.md),
already attributed in the README: feedback is chained in 64-byte chunks and keys
evolve at section boundaries. No upstream implementation was copied or executed.
