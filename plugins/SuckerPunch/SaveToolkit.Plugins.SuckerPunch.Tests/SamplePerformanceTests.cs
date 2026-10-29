using System.Buffers.Binary;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class SamplePerformanceTests
{
    private const long MaximumOpenAndViewAllocation = 2 * 1024 * 1024;
    private const long MaximumTenRowPageAllocation = 96 * 1024;

    [Theory]
    [InlineData("PPSA02225", 4)]
    [InlineData("PPSA26344", 8)]
    public void LargestDeclaredSampleHasBoundedFirstViewAndPageAllocations(string title, int endOffset)
    {
        var plugin = new SuckerPunchPlugin();
        var file = SampleSaves.Load(title).AllDiskFiles.Where(file => plugin.Probe(file).IsMatch)
            .OrderByDescending(file => BinaryPrimitives.ReadUInt32LittleEndian(file.ReadAllBytes().AsSpan(endOffset)))
            .ThenBy(file => file.RelativePath, StringComparer.Ordinal).First();
        var references = new ReferencePackStore([plugin]).Pin(plugin, title);
        for (var index = 0; index < 5; index++)
        {
            using var warm = plugin.Open(file, references);
            _ = warm.BuildView();
        }
        var allocated = AllocationProbe.MeasureOpen(() => plugin.Open(file, references), out var opened, document => _ = document.BuildView());
        using var document = opened;
        var view = document.BuildView();
        Assert.NotNull(view);
        allocated.AssertLessThan(MaximumOpenAndViewAllocation, $"Open and first view allocated {allocated.MinimumBytes:N0} bytes.");
        var queryable = (IQueryableDocument)document;
        var collection = Assert.Single(queryable.Collections);
        var query = new QueryableCollectionQuery { Limit = 10 };
        _ = queryable.Query(collection.Id, query);
        allocated = AllocationProbe.Measure(() => queryable.Query(collection.Id, query with { Offset = 200 }), out var page);
        Assert.Equal(10, page.Rows.Count);
        allocated.AssertLessThan(MaximumTenRowPageAllocation, $"Ten-row page allocated {allocated.MinimumBytes:N0} bytes.");
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }
}
