# XorJson performance review (#40)

Reviewed on `review/perf-xorjson`, against baseline `df70261`. All source and test
changes are inside `plugins/XorJson/`. No format, numeric-literal, no-op round-trip,
write-warning or maturity contract changed.

## Reproduction

The standalone harness and raw logs are retained at
`D:/Temp/saveeditor/perf-xorjson/` (outside the repository). `Program.cs` is the
same for the baseline and changed builds. The final baseline was rebuilt from a
detached D: worktree at `df70261`, rather than inferred from intermediate edits.
Cleanup was blocked by automatic approval review ("blocked by policy"); the temporary baseline worktree and task TEMP folder remain for orchestrator cleanup. If later removed, recreate the baseline with
`git worktree add --detach D:/Temp/saveeditor/perf-xorjson/baseline df70261`.
The existing `baseline-harness/Harness.csproj` references that checkout.
`Harness.csproj` references this implementation worktree; adjust its absolute
ProjectReference paths if moving the harness.

```powershell
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$env:SAVETOOLKIT_TEST_TEMP='D:/Temp/saveeditor/test-scratch'
$env:TEMP='D:/Temp/saveeditor/test-scratch/review-perf-xorjson'
$env:TMP=$env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null
dotnet run --project D:/Temp/saveeditor/perf-xorjson/baseline-harness/Harness.csproj -c Release
dotnet run --project D:/Temp/saveeditor/perf-xorjson/Harness.csproj -c Release
dotnet test --project plugins/XorJson/SaveToolkit.Plugins.XorJson.Tests -c Release
dotnet test --project plugins/XorJson/SaveToolkit.Plugins.XorJson.Tests
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
```

Windows x64, .NET 10.0.11, Release. Each operation uses eight warmups, then the
median of 31 synchronous measurements of thread-local allocated bytes and elapsed
time. Input creation/import and reference-pack loading/pinning are excluded.
The largest supplied input is the 40,062-byte character sample. Open + first
view includes construction of every top-level tab, graph/map shell and collection
metadata with a pinned reference context; it excludes browser/HTTP rendering.

Query numbers below are warm (the requested projection/sort has already been
used). The first search may still project every row, and the first sort requires
O(n log n) work; only the last sort column/direction is cached. Search and exact
filtering scan the complete model to report accurate counts. Native-order paging
creates only the requested rows. Structural edits, scalar edits and memento
restoration invalidate row/path/sort/identity caches.

## Final same-harness comparison

Raw logs: `baseline-final-harness.txt` and `after-final-harness.txt` on D:.
Allocations include the returned objects. Timing is diagnostic and varies with
JIT tiering, shared-machine activity and runtime caches; it is never a test gate.

| Operation | Before bytes | After bytes | Before median ms | After median ms |
| --- | ---: | ---: | ---: | ---: |
| Largest sample: Open | 1,445,760 | 1,445,848 | 1.0432 | 1.0718 |
| Largest sample: Open + first view | 2,581,432 | 1,655,928 | 3.0961 | 1.4177 |
| 10,000 scalars: Open + view + metadata | 10,629,632 | 6,338,776 | 14.7470 | 7.8730 |
| 10,000 scalars: one-row native page | 1,125,024 | 4,192 | 0.9294 | 0.0025 |
| 10,000 scalars: search `999`, one row | 407,504 | 4,264 | 0.9629 | 0.6658 |
| 10,000 scalars: lexical value sort, offset 100 | 1,205,416 | 4,320 | 1.4561 | 0.0027 |
| 10,000 scalars: details-path sort, offset 100 | 4,237,336 | 4,320 | 4.1504 | 0.0028 |
| 10,000 scalars: exact details-path filter | 3,837,216 | 4,696 | 3.1298 | 0.3118 |
| 10,000 scalars: one selected bulk preview | 836,888 | 1,824 | 0.6876 | 0.0015 |
| 5,000 inventory records: final 20 rows | 271,576 | 30,496 | 3.9247 | 0.0289 |
| Sample group proposals | 9,313,496 | 4,976,264 | 4.9750 | 4.0699 |
| Sample group validation | 6,456,608 | 4,974,632 | 3.5242 | 3.7274 |

Opening the plain sample alone is essentially unchanged (88 bytes of additional
per-document cache ownership). The opening/first-view allocation reduction is
35.9%. Warm native pages and selected previews reduce allocations by over 99%.
Group validation reduces allocations, but its timing difference is inconclusive.

## Regression evidence and preservation

The original scope suite had 205 tests. The final suite has **223 passed, zero
failed, zero skipped**, in both Release and Debug. API build: zero warnings/errors.

- Both 1 MiB encoded-object opening allocation tests failed at about 5.25 MB
  before the prefix fix; both now fit the 4.33 MB budget. Candidate object/BOM
  prefixes are checked without whole-file buffers. Plausible candidates still
  parse fully; ambiguous arrays continue to reject.
- Five initial collection allocation guards failed before the lazy projection
  fix. Metadata/first-view construction now fits 128 KiB and warm one-row
  native/search/value-sort/path-sort requests fit 32 KiB. The additional path
  filter guard failed at 3,036,168 bytes before path caching and also fits 32 KiB.
- A fresh selected-row preview failed at 1,717,232 bytes before the lookup fix;
  it now fits 32 KiB without first building a full token dictionary.
- Counted inventory enumeration failed at 500,500 reads for 1,000 descriptions;
  it now requires exactly 1,000, including repeated/reverse descriptions.
- Two proposals previously read their shared anchor seven times; they now read
  it twice. Explicit group validation drops from three reads to two per member.
  Every checked member still serializes/reparses and compares its original bytes.
- Reference-backed metadata performs zero catalog record lookups; the first
  one-row inventory page performs exactly two (projection + published field).
- Query characterization covers lexical text ordering, stable ties in
  both directions, case-insensitive combined filtering, counts, page limits/end
  pages, retired/canonical/duplicate selected keys, stale plans, numeric spelling,
  scalar writes, structural removal and memento restoration.
- The standalone harness compares complete serialized view/page fingerprints.
  The root view and all eight sample collections match the baseline exactly.
  Root SHA-256: `36275F4FF043ABE405B791437728AE48EAE357FFD5E609176FEADADAD9B648C9`.
- Existing exact-literal, corruption, conformance, scalar/structural history,
  export, warning and byte-identical no-op tests remain enabled and pass.

Allocation tests use a non-parallel xUnit collection so unrelated numeric parser
tests cannot contend for the SDK's shared regex runner cache. The other test
collections keep their normal parallel execution. The sample guard isolates
plugin opening/first-view work with no reference pack; catalog behavior has
separate call-count guards and the harness measures the pinned-context path.

No reported findings are deferred. Full output parsing inside `Serialize()` is
intentional integrity validation, so it remains in place. Reference-pack startup
loading is owned by Core and was excluded from these plugin-only measurements.

## Measurement references

- Microsoft: [GC.GetAllocatedBytesForCurrentThread](https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread)
  measures managed allocations on the current thread; native allocations are excluded.
- xUnit: [selective collection parallelism](https://xunit.net/docs/running-tests-in-parallel#selectively-opting-out-of-parallelism)
  documents isolation of the allocation measurement collection.
