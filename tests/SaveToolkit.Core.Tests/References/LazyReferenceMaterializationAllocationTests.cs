using System.Globalization;
using System.Text.Json;
using SaveToolkit.Sdk.References;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests.References;

[Collection(typeof(AllocationTestCollection))]
public sealed class LazyReferenceMaterializationAllocationTests(ITestOutputHelper output)
{
    private const int RecordCount = 5_000;
    private const long FirstCatalogBudget = 8 * 1024 * 1024;
    private const string PackId = "lazy.allocation";
    private const string ResourcePath = "refpacks/lazy.allocation/items.json";

    [Fact]
    public void FirstCatalogAccessDoesNotRepeatValidatedRecordDiagnostics()
    {
        var rows = Enumerable.Range(0, RecordCount).Select(index => new
        {
            id = index.ToString(CultureInfo.InvariantCulture),
            name = $"Synthetic item {index}",
            category = "consumable",
            description = "Synthetic stackable item.",
            source = "https://example.org/reference",
            attributes = new Dictionary<string, string> { ["type"] = "consumable", ["stacking"] = "yes" },
        });
        var json = JsonSerializer.Serialize(new { records = rows });
        var source = CountingPackResourceAssembly.Create(new PackBuilder { Id = PackId }.Records("items.json", json));
        EmbeddedReferenceContext? context = null;

        // Startup validation is outside every sample. Every measured lookup uses a fresh context,
        // so a warm catalog cache cannot hide repeated validation or first-materialization costs.
        var allocation = AllocationProbe.Measure(() => context!.Catalog("items"), out var catalog,
            collectBetweenRuns: true,
            beforeRun: () => context = EmbeddedReferenceContext.Load(source).Pin("test.items", null));

        output.WriteLine($"First materialization of {RecordCount:N0} authored records: {allocation}.");
        allocation.AssertAtMost(FirstCatalogBudget,
            "Reopening immutable, startup-validated records must not rebuild validation diagnostics.");
        Assert.NotNull(catalog);
        Assert.Equal(RecordCount, catalog.Count);
        var first = catalog.Find("0")!;
        var last = catalog.Find((RecordCount - 1).ToString(CultureInfo.InvariantCulture))!;
        Assert.Same(first.Category, last.Category);
        Assert.Same(first.Attributes["type"], last.Attributes["type"]);
        Assert.Equal(2 * (AllocationProbe.DefaultMeasuredRuns + 1), source.Reads(ResourcePath));
    }
}
