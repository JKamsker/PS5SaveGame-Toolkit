using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Plugins.FinalFantasy16;
using SaveToolkit.Plugins.FinalFantasy16.Tests;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

const int Warmups = 5;
const int Iterations = 21;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var plugin = new FinalFantasy16Plugin();
using var archive = ZipFile.OpenRead(SampleSaves.FindArchive("PPSA10665")!);
var entry = archive.Entries.Where(entry => entry.Name.StartsWith("game-save-data", StringComparison.Ordinal))
    .OrderByDescending(entry => entry.Length).First();
using var input = entry.Open();
using var memory = new MemoryStream();
input.CopyTo(memory);
var sample = FaithTestData.File(memory.ToArray());
Console.WriteLine($"Runtime={Environment.Version}; warmups={Warmups}; measured={Iterations}; tiered={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default"}; sample={entry.FullName}; bytes={sample.Length}");
var coldBytes = GC.GetAllocatedBytesForCurrentThread();
var coldTime = Stopwatch.GetTimestamp();
var store = new ReferencePackStore([plugin]);
var references = store.Pin(plugin, "PPSA10665");
using (var cold = plugin.Open(sample, references)) _ = cold.BuildView();
Console.WriteLine($"Cold packs+open+view: {Stopwatch.GetElapsedTime(coldTime).TotalMilliseconds:F3} ms; {GC.GetAllocatedBytesForCurrentThread() - coldBytes} B");
Measure("warm pack load", () => new ReferencePackStore([plugin]));
MeasureFile("largest", sample, references);
MeasureFile("capacity mapped", FaithTestData.File(CapacitySave(mapped: true)), references);
MeasureFile("capacity unknown", FaithTestData.File(CapacitySave(mapped: false)), references);

void MeasureFile(string label, SaveToolkit.Sdk.Saves.ISaveFile file, IReferenceContext context)
{
    Measure(label + " open", () => plugin.Open(file, context));
    Measure(label + " open+first view", () => { using var document = plugin.Open(file, context); return document.BuildView(); });
    using var document = plugin.Open(file, context);
    _ = document.BuildView();
    var queryable = (IQueryableDocument)document;
    Console.WriteLine($"{label}: {queryable.Collections.Single().TotalCount} main rows");
    foreach (var (name, query) in new (string, QueryableCollectionQuery)[]
    {
        ("page 1 row", new() { Limit = 1 }),
        ("page offset100 50rows", new() { Offset = 100, Limit = 50 }),
        ("search miss", new() { Text = "no-such-item", Limit = 1 }),
        ("name sort", new() { SortColumn = "name", Descending = true, Limit = 1 }),
        ("quantity sort", new() { SortColumn = "quantity", Descending = true, Limit = 1 }),
        ("id filter", new() { Filters = new Dictionary<string, string> { ["id"] = "5" }, Limit = 1 }),
    }) Measure(label + " " + name, () => queryable.Query(FaithInventoryView.CollectionId, query));
    var planner = (IActionPlanningDocument)document;
    var parameters = new Dictionary<string, FieldValue> { [FaithQuantityPlanning.QuantityParameter] = FieldValue.Of(1) };
    foreach (var (name, scope) in new (string, ActionScope)[]
        { ("plan empty", new([])), ("plan one", new(["inventory/0"])), ("plan all", new()) })
        Measure(label + " " + name, () => planner.PlanAction(FaithQuantityPlanning.ActionId, parameters, scope));
    // Compare hashes across variants without writing any sample-derived views or bytes to disk.
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(document.BuildView()));
    foreach (var column in queryable.Collections.Single().Columns)
        foreach (var descending in new[] { false, true })
            hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(queryable.Query(FaithInventoryView.CollectionId,
                new() { SortColumn = column.Key, Descending = descending, Limit = 3 })));
    foreach (var scope in new ActionScope[] { new([]), new(["inventory/0"]), new() })
        hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(planner.PlanAction(FaithQuantityPlanning.ActionId, parameters, scope)));
    hash.AppendData(document.Serialize().Single().Content.Span);
    Console.WriteLine($"{label} behavior sha256={Convert.ToHexStringLower(hash.GetHashAndReset())}");
}

static void Measure(string name, Func<object> action)
{
    for (var i = 0; i < Warmups; i++) Run();
    var measurements = Enumerable.Range(0, Iterations).Select(_ => Run()).ToArray();
    var ms = measurements.Select(m => m.Ms).Order().ElementAt(Iterations / 2);
    var bytes = measurements.Select(m => m.Bytes).Order().ElementAt(Iterations / 2);
    Console.WriteLine($"{name}: {ms:F3} ms; {bytes} B");
    (double Ms, long Bytes) Run()
    {
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        var result = action();
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var delta = GC.GetAllocatedBytesForCurrentThread() - allocated;
        if (result is IDisposable disposable) disposable.Dispose();
        GC.KeepAlive(result);
        return (elapsed, delta);
    }
}

static byte[] CapacitySave(bool mapped)
{
    var payload = new byte[FaithSchema.ItemLength];
    const int Metadata = FaithInventory.Capacity * 20;
    const int Keys = Metadata + 16;
    for (var slot = 0; slot < FaithInventory.Capacity; slot++)
    {
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(slot * 16), mapped ? 5 : 60000 + slot);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(slot * 16 + sizeof(int)), slot + 2);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(Keys + slot * sizeof(ulong)), (ulong)slot << 32 | (uint)slot);
    }
    BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(Metadata), FaithInventory.Capacity);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(Metadata + sizeof(ulong)), FaithInventory.Capacity);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(Keys + FaithInventory.Capacity * sizeof(ulong)), FaithInventory.Capacity);
    return FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, payload));
}
