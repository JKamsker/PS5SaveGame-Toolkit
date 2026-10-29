using System.Buffers.Binary;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

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
        var before = GC.GetAllocatedBytesForCurrentThread();
        using var document = plugin.Open(file, references);
        var view = document.BuildView();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.NotNull(view);
        Assert.True(allocated < MaximumOpenAndViewAllocation, $"Open and first view allocated {allocated:N0} bytes.");
        var queryable = (IQueryableDocument)document;
        var collection = Assert.Single(queryable.Collections);
        var query = new QueryableCollectionQuery { Limit = 10 };
        _ = queryable.Query(collection.Id, query);
        before = GC.GetAllocatedBytesForCurrentThread();
        var page = queryable.Query(collection.Id, query with { Offset = 200 });
        allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(10, page.Rows.Count);
        Assert.True(allocated < MaximumTenRowPageAllocation, $"Ten-row page allocated {allocated:N0} bytes.");
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }
}
