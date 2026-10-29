# SuckerPunch performance review (#40)

Measured on 2026-10-29, Windows x64, .NET 10 Release. Baseline is the parent of
`25cbf51`; final includes `25cbf51` and `c126a44`. Sample import is outside measurement.
This is a historical comparison of those revisions, not a benchmark or test count for current HEAD.
Both runs use the same pinned embedded reference store, samples and harness. Normal
bin/obj stays in the source worktree; harness, reports, imported samples and TEMP/TMP
stay on D:. No sample bytes are committed.

The harness selects the largest **declared payload**, rather than assuming the file
named memory is largest. All claimed files have fixed physical sizes:

| Title | Corpus | Selected payload | File bytes | Declared bytes | Equipment rows |
| --- | --- | --- | ---: | ---: | ---: |
| Tsushima PPSA02225 | 25 files | `sdimg_sce_bu_suckerpunchUser8/dis.dat` | 555,008 | 380,388 | 314 |
| Yōtei PPSA26344 | 13 updated files, 11 nonempty | `sdimg_suckerpunchUser4/dis.dat` | 616,448 | 417,135 | 1,037 |

Each non-cold operation has 100 warmups and 101 measured iterations. Allocation is
`GC.GetAllocatedBytesForCurrentThread`; reported times and allocations are independent
medians. Times are diagnostic and vary with other work on the machine. No test has
an elapsed-time threshold. The separate one-shot cold F2 load varied from 82 to
106 ms / about 3 MB; the shared loader was not changed.

## Results

| Title / operation | Before ms | After ms | Before bytes | After bytes |
| --- | ---: | ---: | ---: | ---: |
| Tsushima Open | 0.9290 | 1.2185 | 779,560 | 779,664 |
| Tsushima Open + first view | 1.6058 | 1.2720 | 1,330,296 | 1,330,336 |
| Tsushima default page, offset 200 / limit 10 | 0.1313 | 0.0107 | 31,592 | 14,272 |
| Tsushima repeated Armor search / category sort | 0.1330 | 0.0150 | 35,368 | 32,544 |
| Tsushima repeated offset sort descending | 0.1347 | 0.0164 | 68,528 | 36,080 |
| Tsushima alternating search/sort criteria | 0.0869 | 0.0310 | 35,368 | 33,744 |
| Tsushima first collection query on new document | 0.1419 | 0.1198 | 68,528 | 58,264 |
| Tsushima document quantity plan, 40 changes | 0.1814 | 0.0190 | 685,704 | 13,040 |
| Tsushima empty-selection plan | 0.0012 | 0.0016 | 544 | 576 |
| Yōtei Open | 0.4107 | 0.4251 | 889,088 | 889,128 |
| Yōtei Open + first view | 0.9483 | 0.9424 | 1,545,480 | 1,545,520 |
| Yōtei default page, offset 200 / limit 10 | 0.1369 | 0.0081 | 59,480 | 22,712 |
| Yōtei repeated byte-A sort descending | 0.0982 | 0.0082 | 52,232 | 22,728 |
| Yōtei alternating Half/Body search and category sort | 0.0721 | 0.1257 | 28,944 | 30,232 |
| Yōtei first collection query on new document | 0.1251 | 0.4102 | 65,312 | 65,640 |
| Yōtei document quantity plan, 17 changes | 0.1545 | 0.0088 | 621,656 | 5,184 |
| Yōtei empty-selection plan | 0.1500 | 0.0013 | 616,944 | 472 |

The action plans no longer clone or scan the whole save. A word replacement adjusts
the existing validated additive checksum by its old/new **byte sums**, including the
second ammo count. This is not integer-value subtraction: byte carries matter. Writers
still recompute and validate complete checksums and reparse exported bytes as before.

Collections retain one filtered/sorted raw-row index per document. Tsushima resolves
its immutable equipment-to-reference projection on first query; numeric offsets do
not require formatted sort strings. Changed text/filter/sort criteria replace the
index; offset/limit reuse it. Filter dictionaries are copied so caller mutation
cannot silently reuse stale rows. No rendered fields or unbounded query history are
cached; every returned page registers fresh bindings.

**Tradeoff:** fully sorting a reusable Yōtei result costs more than the previous LINQ
partial page sort on a new/changed query (about +0.29 ms for the first query here).
Subsequent pages avoid full filtering/sorting, and the result is retained only until
criteria change. The first view remains bounded and does not initialize these indexes.
Open-time allocation is essentially unchanged; these timings do not establish an
open-latency improvement. Repeated scans in validation/serialization are retained
because they check integrity, not treated as redundant parsing.

## Verification

These results belong to the performance-review revisions above. For the current accuracy-audit
validation and commands, see the [README](README.md#validation-and-maintenance).

- Baseline: **313 passed / 0 failed / 0 skipped**.
- Two failing-first preview allocation cases: **2 failed / 6 passed**.
- Three failing-first page allocation/reference lookup cases: **3 failed**.
- Final: **330 passed / 0 failed / 0 skipped**, including all baseline test names.
- All existing exact-byte no-op, corruption, conformance, warnings, stale/forged-plan,
  and history tests remain intact. Updated Yōtei sample coverage remains green.
- New synthetic tests assert 32 KiB preview bounds and 12 KiB cached one-row page
  bounds; a 512-identifier counting context asserts at most 10 reference lookups on
  subsequent 10-row pages (baseline: 522). All supported sorts/directions, ties,
  full paging, changing search, copied/mutated filters and published bindings are
  checked against authored models. Checksum oracles independently cover 0/1/257,
  carries, mirrored ammo, skipped bounds, purity, exact output and subsequent plans.
- Largest-sample tests assert 2 MiB Open + first-view and 96 KiB ten-row page bounds.
  They skip cleanly without private samples; synthetic regressions need no samples.
- API Release build with `--warnaserror`: **0 warnings / 0 errors**.
- Changed C# files stay below 300 lines. Scope is exclusively `plugins/SuckerPunch/`.

The review rechecked the existing
[KOVRlN technical notes](https://github.com/KOVRlN/ghostoftsushima-save-converter/blob/main/TECHNICAL.md)
and [Apollo count anchors](https://github.com/bucanero/apollo-patches/blob/main/PS4/CUSA11456.savepatch).
No upstream code, new field semantics, reference data, maturity or write scope changed.

## Reproduce on D:

Evidence is retained at `D:/Temp/saveeditor/review-results/perf-suckerpunch/`:
`before-final.txt`, `after-final.txt`, baseline/red/final TRX reports and API build log.
The executable harness is `harness/Harness.csproj` and `harness/Program.cs`.
The initial 31-iteration reports are retained separately; use the 101-iteration
final reports for the table above.

To recreate the harness, save the C# below as `Program.cs` in that D: harness
directory and create `Harness.csproj` using the PowerShell below. Run from the
desired revision in the source worktree. Use separate source checkouts or force a
rebuild when changing revisions: timestamp-preserving source restoration can make
MSBuild reuse the previous binary.

```powershell
$repo = (Get-Location).Path
$harness = 'D:/Temp/saveeditor/review-results/perf-suckerpunch/harness'
New-Item -ItemType Directory -Force -Path $harness | Out-Null
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$repo/plugins/SuckerPunch/SaveToolkit.Plugins.SuckerPunch/SaveToolkit.Plugins.SuckerPunch.csproj" />
    <ProjectReference Include="$repo/src/SaveToolkit.Testing/SaveToolkit.Testing.csproj" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath "$harness/Harness.csproj"
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$env:SAVETOOLKIT_TEST_TEMP = 'D:/Temp/saveeditor/test-scratch'
$env:TEMP = 'D:/Temp/saveeditor/test-scratch/review-perf-suckerpunch'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null
dotnet run --project "$harness/Harness.csproj" -c Release
dotnet test --project plugins/SuckerPunch/SaveToolkit.Plugins.SuckerPunch.Tests -c Release
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj -c Release --warnaserror
```

```csharp
using System.Buffers.Binary;
using System.Diagnostics;
using SaveToolkit.Core.References;
using SaveToolkit.Plugins.SuckerPunch;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

var plugin = new SuckerPunchPlugin();
Measure("Cold F2 store", () => _ = new ReferencePackStore([plugin]), warmup: 0, count: 1);
var store = new ReferencePackStore([plugin]);
foreach (var title in new[] { "PPSA02225", "PPSA26344" })
{
    var samples = SampleSaves.Load(title).AllDiskFiles.Where(f => plugin.Probe(f).IsMatch)
        .Select(f => (File: f, End: BinaryPrimitives.ReadUInt32LittleEndian(f.ReadAllBytes().AsSpan(title == "PPSA02225" ? 4 : 8))))
        .OrderByDescending(f => f.End).ThenBy(f => f.File.RelativePath, StringComparer.Ordinal).ToArray();
    var file = samples[0].File;
    var context = store.Pin(plugin, title);
    Console.WriteLine($"{title}: {samples.Length} files; {file.RelativePath}; {file.Length} bytes; declared {samples[0].End}");
    Measure("Open", () => { using var doc = plugin.Open(file, context); });
    Measure("Open + first view", () => { using var doc = plugin.Open(file, context); _ = doc.BuildView(); });
    using var doc = plugin.Open(file, context);
    _ = doc.BuildView();
    Measure("Rebuild view", () => _ = doc.BuildView());
    var queryable = (IQueryableDocument)doc;
    var id = queryable.Collections.Single().Id;
    foreach (var query in new[] {
        new QueryableCollectionQuery { Limit = 10 },
        new QueryableCollectionQuery { Limit = 10, Offset = 200 },
        new QueryableCollectionQuery { Limit = 10, Text = "Armor", SortColumn = "category" },
        new QueryableCollectionQuery { Limit = 10, SortColumn = title == "PPSA02225" ? "location" : "a", Descending = true }
    })
    {
        var page = queryable.Query(id, query);
        Measure($"Query offset={query.Offset} text={query.Text} sort={query.SortColumn} desc={query.Descending} matches={page.FilteredCount}", () => _ = queryable.Query(id, query));
    }
    var searchA = new QueryableCollectionQuery { Limit = 10, Text = title == "PPSA02225" ? "Armor" : "Half", SortColumn = "category" };
    var searchB = searchA with { Text = title == "PPSA02225" ? "Sword" : "Body", Descending = true };
    var alternate = false;
    Measure("Changed search/sort criteria", () => {
        alternate = !alternate;
        _ = queryable.Query(id, alternate ? searchA : searchB);
    });
    ISaveDocument? fresh = null;
    var firstQuery = new QueryableCollectionQuery { Limit = 10, SortColumn = title == "PPSA02225" ? "location" : "a", Descending = true };
    Measure("First collection query (new document)", () => _ = ((IQueryableDocument)fresh!).Query(id, firstQuery), setup: () => {
        fresh?.Dispose();
        fresh = plugin.Open(file, context);
        _ = fresh.BuildView();
    });
    fresh?.Dispose();
    var planner = (IActionPlanningDocument)doc;
    var action = title == "PPSA02225" ? "inventory/set-quantity" : "yotei/inventory/set-quantity";
    var parameters = new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(1L) };
    var scope = new ActionScope();
    var plan = planner.PlanAction(action, parameters, scope);
    Console.WriteLine($"Plan: {plan.Changes.Count} changes, {plan.SkippedRows.Count} skipped, {plan.Errors.Count} errors");
    Measure("Plan document", () => _ = planner.PlanAction(action, parameters, scope));
    Measure("Plan empty selection", () => _ = planner.PlanAction(action, parameters, new ActionScope([])));
}
static void Measure(string label, Action operation, int warmup = 100, int count = 101, Action? setup = null)
{
    for (var i = 0; i < warmup; i++) { setup?.Invoke(); operation(); }
    var times = new double[count];
    var allocations = new long[count];
    for (var i = 0; i < count; i++)
    {
        setup?.Invoke();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        operation();
        times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        allocations[i] = GC.GetAllocatedBytesForCurrentThread() - before;
    }
    Array.Sort(times); Array.Sort(allocations);
    Console.WriteLine($"{label}: {times[count / 2]:F4} ms / {allocations[count / 2]:N0} B (median n={count})");
}

```
