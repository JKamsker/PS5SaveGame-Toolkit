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

    [Fact]
    public void CommunityExpansionIsPinnedLicensedAndKeepsUnmappedNamesUnknown()
    {
        var plugin = new GodOfWarRagnarokPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA08330");
        var candidates = Assert.IsAssignableFrom<IReferenceCatalog>(context.Catalog("gow-ragnarok-community-ids"));
        Assert.Equal(36, candidates.Count);
        Assert.Equal("GPL-3.0", candidates.Dataset.Attribution.License);
        Assert.Contains("273f032b4c31c017fed5c1588f209b8faf87ba6c", candidates.Dataset.Attribution.SourceUrl);
        Assert.All(candidates.Search(new() { Limit = 200 }).Items, record =>
        {
            Assert.StartsWith("Unknown ", record.Name);
            Assert.Null(record.Limit);
        });

        var collectibles = Assert.IsAssignableFrom<IReferenceCatalog>(context.Catalog("gow-ragnarok-collectibles"));
        Assert.Equal(2, collectibles.Count);
        var raven = collectibles.Find("7ED58EF1EF802748")!;
        Assert.Contains("inferred", raven.Name);
        Assert.Contains("51e19b2a292c60f9acdf3c13c561809657104ea2", raven.Source);
        Assert.Null(raven.Limit);
        var fallback = new RagnarokReferences(IReferenceContext.Empty).Find(0x482780EFF18ED57E)!;
        Assert.Equal(raven.Name, fallback.Name);
        Assert.Equal(raven.AttributionText, fallback.AttributionText);
    }

    private static void Write(byte[] bytes, int offset, uint value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
