using System.Buffers.Binary;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokGearReferenceTests
{
    [Fact]
    public void PublicPackPreservesAttributionAmbiguitiesAndUnknownLimits()
    {
        var context = RagnarokTestSupport.PinnedReferences();
        var catalog = context.Catalog(RagnarokGearReferences.CatalogId, "PS5 revision 0x00513040")!;
        Assert.Equal(2092, catalog.Count);
        var direct = new RagnarokGearReferences(IReferenceContext.Empty);
        Assert.Equal(new DateOnly(2026, 10, 29), catalog.Dataset.Attribution.Retrieved);
        Assert.Contains("No licence published", catalog.Dataset.Attribution.License);
        for (var offset = 0; offset < catalog.Count; offset += 200)
        foreach (var record in catalog.Search(new() { Offset = offset, Limit = 200 }).Items)
        {
            Assert.Null(record.Limit);
            Assert.Null(record.AssetId);
            Assert.Contains("No licence published", record.AttributionText);
            var id = BinaryPrimitives.ReadUInt64LittleEndian(Convert.FromHexString(record.Key));
            Assert.Equal(record.Name, direct.Find(id)!.Name);
            Assert.Equal(record.Attributes["level"], direct.Find(id)!.Attributes["level"]);
        }
        // Duplicate visual/equipment descriptions stay visible, rather than choosing an equip role.
        var conflicting = direct.Find(0xFAE9A62BC9D22900)!;
        Assert.Contains(" / ", conflicting.Name);
        Assert.Equal("Unknown", conflicting.Attributes["level"]);
        Assert.Equal("Unknown", direct.Find(0x07BC5744005395C1)!.Attributes["level"]);
    }

    [Fact]
    public void HostedAndDirectPagesPublishIdenticalDefinitionsLevelsAndProvenance()
    {
        var file = RagnarokTestSupport.GameplayFile(RagnarokGearTests.Fixture());
        var plugin = new GodOfWarRagnarokPlugin();
        using var direct = plugin.Open(file);
        using var hosted = plugin.Open(file, RagnarokTestSupport.PinnedReferences(plugin));
        var first = Assert.IsAssignableFrom<IQueryableDocument>(direct).Query("gear", new());
        var second = Assert.IsAssignableFrom<IQueryableDocument>(hosted).Query("gear", new());
        foreach (var (a, b) in first.Rows.Zip(second.Rows))
        {
            foreach (var column in new[] { "definition", "level" })
            {
                var left = Assert.IsType<FieldNode>(a.Cells[column]);
                var right = Assert.IsType<FieldNode>(b.Cells[column]);
                Assert.Equal(left.Value, right.Value);
                Assert.Equal(left.Description, right.Description);
                Assert.True(left.Spec.ReadOnly);
                Assert.True(right.Spec.ReadOnly);
            }
        }
    }
}
