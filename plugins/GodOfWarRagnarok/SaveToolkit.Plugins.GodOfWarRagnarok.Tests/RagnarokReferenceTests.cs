using SaveToolkit.Core.References;
using SaveToolkit.Sdk.References;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokReferenceTests
{
    [Fact]
    public void EmbeddedPackIsVersionedAttributedAndHasUnknownLimits()
    {
        var plugin = new GodOfWarRagnarokPlugin();
        var store = new ReferencePackStore([plugin]);
        var context = store.Pin(plugin, "PPSA08330");
        var catalog = Assert.IsAssignableFrom<IReferenceCatalog>(context.Catalog(RagnarokReferences.CatalogId));

        Assert.Equal(31, catalog.Count);
        Assert.Contains("currency", catalog.Categories);
        Assert.Contains("Valhalla resource", catalog.Categories);
        Assert.Equal("1.0.0", catalog.Pack.Version);
        Assert.Equal(ReferencePackOrigin.Embedded, catalog.Pack.Origin);
        Assert.Equal("CC-BY-SA-3.0", catalog.Dataset.Attribution.License);
        Assert.Equal("Hacksilver", catalog.Find("9E5240D27406E85A")!.Name);
        Assert.Null(catalog.Find("9E5240D27406E85A")!.Limit);
        Assert.Contains("godofwar.fandom.com", catalog.Find("9E5240D27406E85A")!.AttributionText);
    }

    [Fact]
    public void PinnedCatalogNamesReachTheReadOnlyDocument()
    {
        var plugin = new GodOfWarRagnarokPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA08330");
        var bytes = new byte[696];
        Write(bytes, 8, 31);
        Write(bytes, 12, 0x00513040);
        using var document = plugin.Open(MemorySaveSet.SingleFile("userpreferences", bytes, "PPSA08330"), context);
        Assert.False(document.CanEdit);
        Assert.Contains(context.Packs, pack => pack.Id == "gow-ragnarok.ps5.en");
    }

    private static void Write(byte[] bytes, int offset, uint value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
