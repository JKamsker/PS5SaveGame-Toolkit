using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class FactCatalogPerformanceTests
{
    private const long ConstructionBudget = 16 * 1024 * 1024;

    [Fact]
    public void CompleteFactCatalogKeepsCompactRecordsUntilRowsAreRequested()
    {
        var (_, _, context) = ReferencePackFixture.Embedded();
        var references = new CyberpunkReferences(context, 2210);
        var file = ProgressionSampleTests.Files().MaxBy(file => file.Length)!;
        var container = CsavContainer.Parse(file.ReadAllBytes());
        var tree = CsavTree.Parse(container);
        var facts = tree.Nodes.Where(node => node.Name == "FactsTable").ToDictionary(node => node.Id,
            node => FactsReader.Read(container.Payload.AsSpan(node.Offset, node.Size)));
        var bound = 0;
        var allocated = AllocationProbe.Measure(() => FactCatalogInspection.Build(facts, references, bindRead: (id, label, spec, get) =>
        {
            bound++;
            return new FieldNode(id, label, spec, get());
        }), out var catalog);

        Assert.Equal(20_978, catalog.Metadata.TotalCount);
        Assert.Equal(20_978, catalog.View().TotalCount);
        Assert.Equal(0, bound);
        // Excludes the host's shared reference packs and the imported payload. A compact index
        // must not allocate a dictionary and formatted provenance for every catalog-only fact.
        allocated.AssertInRange(0, ConstructionBudget);
        var page = catalog.Query(new QueryableCollectionQuery { Limit = 3 });
        Assert.Equal(3, page.Rows.Count);
        Assert.Equal(3, bound);
        Assert.All(page.Rows, row => Assert.All(row.Cells.Values.OfType<SectionNode>(), cell =>
            Assert.Contains(cell.Children, child => child is TextNode { Text.Length: > 0 })));
    }

    [Fact]
    public void CompactRowsPreserveDuplicateIdentityCanonicalWhitespaceUnknownHashesAndLiveGetters()
    {
        const uint whitespaceHash = 2357536991;
        var (_, _, context) = ReferencePackFixture.Embedded();
        var references = new CyberpunkReferences(context, 2210);
        var current = new Dictionary<(int Table, int Index), uint> { [(1, 0)] = 0, [(2, 0)] = 7, [(2, 1)] = 9 };
        var getters = new Dictionary<string, Func<FieldValue>>();
        var catalog = FactCatalogInspection.Build(new Dictionary<int, IReadOnlyList<SaveFact>>
        {
            [1] = [new(whitespaceHash, 0)],
            [2] = [new(whitespaceHash, 7), new(uint.MaxValue, 9)],
        }, references, (table, index) => current[(table, index)], (id, label, spec, get) =>
        {
            getters[id] = get;
            return new FieldNode(id, label, spec, get());
        });
        Assert.Empty(getters);
        var duplicates = catalog.Query(new QueryableCollectionQuery
            { Filters = new Dictionary<string, string> { ["hash"] = $"0x{whitespaceHash:X8}" } });
        Assert.Equal(["fact-catalog/saved/1/0", "fact-catalog/saved/2/0"], duplicates.Rows.Select(row => row.Key));
        Assert.All(duplicates.Rows, row =>
        {
            Assert.Equal(" q000_done", Assert.IsType<SectionNode>(row.Cells["name"]).Title);
            Assert.Contains("Significant surrounding whitespace", Assert.IsType<SectionNode>(row.Cells["evidence"]).Title);
            Assert.Contains("Source:", Assert.IsType<SectionNode>(row.Cells["name"]).Description);
        });
        var getter = getters["fact-catalog/saved/1/0/value"];
        Assert.Equal(FieldValue.Of(0L), getter());
        current[(1, 0)] = 42;
        Assert.Equal(FieldValue.Of(42L), getter());
        current[(1, 0)] = 0;
        Assert.Equal(FieldValue.Of(0L), getter());
        var unknown = Assert.Single(catalog.Query(new QueryableCollectionQuery
            { Filters = new Dictionary<string, string> { ["hash"] = "0xFFFFFFFF" } }).Rows);
        Assert.Equal("Unknown", Assert.IsType<SectionNode>(unknown.Cells["name"]).Badge);
        var absent = Assert.Single(catalog.Query(new QueryableCollectionQuery
            { Filters = new Dictionary<string, string> { ["presence"] = "Catalog only / absent from save" }, Limit = 1 }).Rows);
        Assert.True(getters[absent.Key + "/value"]().IsNull);
    }
}
