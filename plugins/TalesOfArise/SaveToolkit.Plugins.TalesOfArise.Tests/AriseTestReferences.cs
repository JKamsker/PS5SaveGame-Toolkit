using SaveToolkit.Core.References;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

internal static class AriseTestReferences
{
    public static ReferenceContext CreateContext(TalesOfArisePlugin? plugin = null)
    {
        plugin ??= new TalesOfArisePlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        return store.Pin(plugin, AriseTestContainer.TitleId);
    }

    public static IEnumerable<ReferenceRecord> AllRecords(IReferenceCatalog catalog)
    {
        for (var offset = 0; offset < catalog.Count; offset += ReferenceQuery.MaxLimit)
        {
            foreach (var record in catalog.Search(new() { Offset = offset, Limit = ReferenceQuery.MaxLimit }).Items)
            {
                yield return record;
            }
        }
    }
}
