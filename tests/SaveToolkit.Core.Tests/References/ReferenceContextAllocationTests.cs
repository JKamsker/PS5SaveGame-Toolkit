using SaveToolkit.Core.References;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests.References;

[Collection(typeof(AllocationTestCollection))]
public sealed class ReferenceContextAllocationTests(ITestOutputHelper output)
{
    private const int Lookups = 1_000;
    private const long LookupBudget = 4_096;

    [Theory]
    [InlineData("items", null, false)]
    [InlineData("items", "1.01", false)]
    [InlineData("missing", null, false)]
    [InlineData("items", null, true)]
    [InlineData("missing", null, true)]
    public void RepeatedCatalogLookupsReusePinnedSelection(string id, string? build, bool host)
    {
        var plugin = new ItemsPlugin();
        var store = new ReferencePackStore([plugin]);
        var context = store.Pin(plugin, ItemsPlugin.TitleId);
        for (var i = 0; i < 5; i++) Lookup();

        var allocated = AllocationProbe.Measure(() =>
        {
            for (var i = 0; i < Lookups; i++) Lookup();
        });

        output.WriteLine($"{id}, build={build}, host={host}: {allocated.MinimumBytes:N0} bytes/{Lookups} lookups.");
        allocated.AssertAtMost(LookupBudget, $"Catalog lookups allocated {allocated.MinimumBytes:N0} bytes.");
        Assert.Equal(id == "items", Lookup() is not null);

        Sdk.References.IReferenceCatalog? Lookup() => host ? context.CatalogForHost(id) : context.Catalog(id, build);
    }
}
