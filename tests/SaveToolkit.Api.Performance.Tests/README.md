# Backend performance regression tests (#40)

This project covers the Codecs, API and Core/References review scope. It lives under the API
directory to keep the review's changes inside its authorized paths. The API excludes this
directory from its own compile/content items. PackBuilder is linked from the existing test
helpers without changing that file.

Run explicitly: `scripts/test.ps1` currently discovers only projects under `tests/` and `plugins/`.

```powershell
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$env:SAVETOOLKIT_TEST_TEMP = 'D:/Temp/saveeditor/test-scratch'
$env:TEMP = 'D:/Temp/saveeditor/test-scratch/review-perf-backend'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null
dotnet test --project tests/SaveToolkit.Api.Performance.Tests -c Release
```

The 17 cases use generated inputs, with no samples or skipped cases. Allocation tests warm the
operation first, measure synchronous current-thread allocations, and use generous ceilings to
detect full scratch/catalog/body allocations. Elapsed time is deliberately not a test assertion.
Other cases preserve sorting/ties, concurrent reads, mixed Kraken quanta, exact JSON byte limits,
wire encoding, and immutable asset memory slices/response metadata.

The review's Release microbenchmark source and before/after logs are in
`D:/Temp/saveeditor/scratch/perf-backend`. Local measurements are not throughput guarantees.
The final run measured 544 B per warm 250,000-record catalog page, 512 B per stored 32-byte
Kraken decode, and 4,255,068 B to reject a generated 25 MiB collection page. The latter previously
allocated 26,655,004 B in the same run. An accepted 50-row JSON response changed from 9,192 B
to 9,288 B; measured latency was approximately 0.05 ms for both implementations.

Catalog indexes are lazy and shared by pinned documents: each requested key/direction retains
one array of record references (about 2 MB at 250,000 records), with six possible orders.
The first request still sorts, and temporarily allocates its sort workspace. Narrow filtered
searches retain their previous behavior and do not build an index. The collection response cap
bounds accumulated output; JsonSerializer may additionally buffer an individual large value.
Only immutable reference-pack assets skip copying. Plugin document buffers can remain mutable,
so document asset/blob/download results keep their snapshots.
