# Backend performance regression tests (#40)

This project covers API allocation/call-count regressions and the earlier codec/reference
performance review. PackBuilder is linked from the existing test helpers.

The full `scripts/test.ps1` discovers this project. To run it separately:

```powershell
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$env:SAVETOOLKIT_TEST_TEMP = 'D:/Temp/saveeditor/test-scratch'
$env:TEMP = 'D:/Temp/saveeditor/test-scratch/review-perf-backend2'
$env:TMP = $env:TEMP
$env:TMPDIR = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null
dotnet test --project tests/SaveToolkit.Api.Performance.Tests -c Release
```

Most regression gates use generated inputs, allocation ceilings or traversal counts; semantic
review also has generous time bounds described below. Synchronous normalization/planning uses current-thread allocation counters after warmup;
awaited HTTP export uses process-wide managed allocation counters in a nonparallel collection.
The ceilings detect complete scratch/archive/body copies, with headroom for framework overhead.
Other cases preserve sorting/ties, concurrent reads, mixed Kraken quanta, exact JSON byte limits,
wire encoding, immutable asset memory slices, complete error DTO fields and export contents.

`ApiWorkflowMeasurements` exercises the actual middleware, binding, endpoint filters, plugins,
JSON serialization and response consumption through WebApplicationFactory/TestServer. It covers
the three largest supplied ZIPs (FF7 Remake, Ragnarok, FF7 Rebirth), the largest individual save
(Elden Ring's 28,967,040-byte memory.dat), and an authored 50,000-element GenericJson array.
Real-sample cases skip cleanly when their private archives are absent. Full validation with the
prescribed artifact path must have zero skips. Mutations, planned reset, undo and redo are
measured on the synthetic JSON; real samples exercise import, largest recognized file open,
views, largest published collection paging/search/sort, sequential/concurrent reads and export.
Raw fallback matches are excluded when selecting a recognized gameplay file.

To retain diagnostic CSV rows (`scenario,operation,milliseconds,allocatedBytes`), set
`SAVETOOLKIT_API_MEASUREMENTS` to an absolute CSV file on D: before running tests. Parent
directories must exist. Rows append; use a fresh path for a new comparison. The companion
`.metadata.txt` records selected plugins, collection IDs/counts and sort result counts.
HTTP figures include server/client plumbing and JSON parsing; export drains the response into
Stream.Null. They exclude host setup, discovery warmup and synthetic source generation.
Timing is observational and can vary with JIT, scheduling, disk cache and other processes.

Round-two raw evidence is retained at `D:/Temp/saveeditor/review-results/perf-backend2/`.
Matched comparisons use three separate Release test processes on each side, the same test
source, and the original production files restored from baseline `cf92e68` for the before runs.
Report independent allocation/time medians; do not interpret asynchronous process counters as
per-thread costs, peak live memory, native allocations or a throughput guarantee.

Export uses a delete-on-close temporary file rather than retaining the entire compressed ZIP
in managed memory. This needs disk space for one completed archive per active export and keeps
the existing Content-Length and clean pre-header I/O failure behavior. File results transfer
asynchronously and remove scratch on success, cancellation and transfer failure. Session output
snapshots remain mandatory; the exporter reuses them instead of making another full copy.
Asset/blob/download responses also reuse their session-owned snapshot without a second clone.
The HTTP document gate still serializes plugin operations because plugins are not thread-safe;
ordinary document DTO serialization runs after the gate is released. Collection pages are
serialized once inside their operation to enforce the existing byte cap before sending headers.
No cross-request view cache is added. The temporary-file choice is covered by asynchronous-only
response tests; the [.NET 10 ZIP entry wrapper](https://github.com/dotnet/runtime/blob/v10.0.11/src/libraries/System.IO.Compression/src/System/IO/Compression/ZipCustomStreams.cs)
does not override DisposeAsync, so disposing an entry can still synchronously write output.

`SemanticReviewMeasurements` measures synchronous `DocumentSession.GetChanges` requests on real
Pragmata, Tales of Arise, Final Fantasy XVI and Baldur's Gate 3 saves. Its separate first-request
row includes opening/capturing the original. Warm requests use five warmups and independent
minima of seven measurements for elapsed time and current-thread managed allocations, using
`AllocationProbe` in `AllocationTestCollection`. Budget assertions are Release-calibrated;
Debug runs report allocation samples without enforcing the allocation/time bounds. Import,
current-document opening, edit setup and HTTP/JSON plumbing are excluded. The first-request
figure is a single diagnostic observation, not a regression gate. Actual provider page counts
are recorded separately. `Category=SamplePerformance` cases skip when samples are unavailable.

These cases have generous per-operation allocation/time ceilings. The generated 10,000-row
`SemanticReviewPerformanceTests` additionally gates reviews at 20 MB and two seconds, with
exactly 50 queries even when the view repeats a collection. The time ceilings target severe
regressions and tolerate tiered JIT/CI scheduling; diagnostic timing differences are not speed
guarantees. See [the semantic review report](SEMANTIC_REVIEW.md) for before/after measurements,
the explicit F8-only diagnostic projection and the full-view coverage limitations.

The first review's Release microbenchmark source and before/after logs are in
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
