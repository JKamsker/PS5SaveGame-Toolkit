using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class CurrencyResolutionTests
{
    private const string UnknownConfig = "0123456789abcdef";
    private const uint GenericItemType = 0x4E5CE3CB;
    private const uint SwingshotType = 0x388B19BC;
    private const int SectionStart = 32;
    private const int FirstRecordTypeOffset = SectionStart + 16;
    private const int GenericRecordSize = 31;
    private const int PresenceMaskOffset = 8;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownConfigOrWrongTypeDoesNotBecomeACurrencyMatch(bool wrongType)
    {
        var bytes = CurrencyDiscoveryTests.Fixture((wrongType ? RiftCurrencyView.RaritaniumConfig : UnknownConfig, 9u));
        if (wrongType) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(FirstRecordTypeOffset), SwingshotType);
        using var document = Open(bytes);
        Assert.Empty(CurrencyDiscoveryTests.CurrencyFields(document));
        var reason = Assert.IsType<TextNode>(Resolution(document, "Raritanium").Children[0]).Text;
        Assert.Contains("no supported generic-item record", reason);
        Assert.DoesNotContain("undecoded payload", reason);
        Assert.DoesNotContain("Their currency identity is unknown", reason);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var collection = Assert.Single(queryable.Collections, candidate => candidate.Id == "inventory/78cb152e/0");
        var row = Assert.Single(queryable.Query(collection.Id, new() { Limit = 1 }).Rows);
        var config = Assert.IsType<FieldNode>(row.Cells["config"]);
        Assert.Equal(wrongType ? RiftCurrencyView.RaritaniumConfig : UnknownConfig, config.Value.AsString());
        Assert.Equal(config.Value, document.GetValue(config.ValueId));
        AssertUnresolved(document, bytes);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(50)]
    public void ConflictsAcrossContainersHaveBoundedIdentitiesAndRetainPagedRecords(int matches)
    {
        var first = CurrencyDiscoveryTests.Fixture((RiftCurrencyView.BoltConfig, 12u), (RiftCurrencyView.RaritaniumConfig, 7u));
        var second = CurrencyDiscoveryTests.Fixture(Enumerable.Repeat((RiftCurrencyView.RaritaniumConfig, 8u), matches - 1).ToArray());
        var bytes = JoinInventoryBlocks(first, second);
        using var document = Open(bytes);
        var resolution = Resolution(document, "Raritanium");
        Assert.Contains($"{matches} supported generic-item records", Assert.IsType<TextNode>(resolution.Children[0]).Text);
        Assert.Contains("Actor/ownership mapping is unknown", Assert.IsType<TextNode>(resolution.Children[0]).Text);
        var identities = resolution.Children.OfType<TextNode>().Where(node => node.Monospace).ToArray();
        Assert.Equal(Math.Min(matches, 5), identities.Length);
        Assert.Contains("Container 78cb152e/0; record 0200000000000000/0", identities[0].Text);
        Assert.Contains("Container 78cb152e/1; record 0100000000000000/0", identities[1].Text);
        Assert.All(identities, node => Assert.Contains("type 0x4E5CE3CB; payload at 0x", node.Text));
        if (matches > identities.Length)
            Assert.Contains(resolution.Children.OfType<TextNode>(), node => node.Text.Contains("first 5 of 50", StringComparison.Ordinal));
        Assert.True(resolution.Children.OfType<TextNode>().Sum(node => node.Text.Length) < 2000);
        Assert.Equal(12u, CurrencyDiscoveryTests.CurrencyFields(document)[RiftCurrencyView.BoltId].Value.AsUInt32());

        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var collection = Assert.Single(queryable.Collections, candidate => candidate.Id == "inventory/78cb152e/1");
        var firstPage = queryable.Query(collection.Id, new() { Limit = 1 });
        var lastPage = queryable.Query(collection.Id, new() { Offset = matches - 2, Limit = 1 });
        Assert.Equal(matches - 1, firstPage.TotalCount);
        Assert.Single(lastPage.Rows);
        var count = Assert.IsType<FieldNode>(lastPage.Rows[0].Cells["count"]);
        Assert.Equal(8u, document.GetValue(count.ValueId).AsUInt32());
        Assert.True(count.Spec.ReadOnly);
        AssertUnresolved(document, bytes);
    }

    [Fact]
    public void OpaqueRecordsAreBoundedAndDoNotHideASeparateSupportedCurrency()
    {
        const int opaqueRecords = 40;
        var bytes = CurrencyDiscoveryTests.Fixture([
            (RiftCurrencyView.BoltConfig, 12u),
            .. Enumerable.Repeat((RiftCurrencyView.RaritaniumConfig, 9u), opaqueRecords),
        ]);
        for (var index = 1; index <= opaqueRecords; index++)
            bytes[FirstRecordTypeOffset + index * GenericRecordSize + PresenceMaskOffset] = 0x07;
        using var document = Open(bytes);
        var resolution = Resolution(document, "Raritanium");
        var reason = Assert.IsType<TextNode>(resolution.Children[0]).Text;
        Assert.Contains("no supported generic-item record", reason);
        Assert.Contains("undecoded payload layout: 40", reason);
        Assert.Contains("Their currency identity is unknown", reason);
        Assert.Equal(5, resolution.Children.OfType<TextNode>().Count(node => node.Monospace));
        Assert.Contains(resolution.Children.OfType<TextNode>(), node => node.Text.Contains("first 5 of 40", StringComparison.Ordinal));
        Assert.Equal(12u, CurrencyDiscoveryTests.CurrencyFields(document)[RiftCurrencyView.BoltId].Value.AsUInt32());
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var collection = Assert.Single(queryable.Collections, candidate => candidate.Id == "inventory/78cb152e/0");
        var page = queryable.Query(collection.Id, new() { Offset = opaqueRecords, Limit = 1 });
        Assert.Equal(opaqueRecords + 1, page.TotalCount);
        Assert.Equal("Opaque", Assert.IsType<FieldNode>(Assert.Single(page.Rows).Cells["config"]).Value.AsString());
        AssertUnresolved(document, bytes);
    }

    [Fact]
    public void UniqueMappedRecordWithoutCountExplainsOnlyTheMissingDecode()
    {
        // Defensive model case: the current wire decoder emits config/count together.
        var record = new InventoryRecord(GenericItemType, 0x30, 23, "authored/0",
            null, RiftCurrencyView.RaritaniumConfig, null, null, null, null);
        var view = new RiftCurrencyView([new(0x78CB152E, 0, 0x20, 39, 0, [record])], true);
        var nodes = view.Build((id, label, spec, value) => new FieldNode(id, label, spec, value));
        var resolution = Assert.Single(nodes.OfType<SectionNode>(), node => node.Title.StartsWith("Raritanium", StringComparison.Ordinal));
        Assert.Equal("Unresolved: the matching record has no decoded ItemCount.", Assert.IsType<TextNode>(resolution.Children[0]).Text);
        Assert.Contains("record authored/0", Assert.Single(resolution.Children.OfType<TextNode>(), node => node.Monospace).Text);
        Assert.Empty(nodes.OfType<FieldNode>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnresolvedInventoryDoesNotDisableExistingBoltCounterWrites(bool ambiguous)
    {
        var source = PlayerCorpusTests.Find("i29slot0-s-autosave", 0);
        var bytes = source.ReadAllBytes();
        var records = InventoryReader.Read(Dat1Container.Parse(bytes)).SelectMany(block => block.Records).ToArray();
        var raritanium = Assert.Single(records, record => record.Type == GenericItemType && record.ConfigBytes == RiftCurrencyView.RaritaniumConfig);
        var target = ambiguous
            ? Assert.Single(records, record => record.Type == GenericItemType && record.ConfigBytes == RiftCurrencyView.BoltConfig)
            : raritanium;
        // The supported generic layout stores its config token at payload +9.
        Convert.FromHexString(ambiguous ? RiftCurrencyView.RaritaniumConfig : UnknownConfig).CopyTo(bytes, target.Offset + 9);
        using var document = Open(bytes);
        _ = Resolution(document, "Raritanium");
        AssertUnresolved(document, bytes);
        var fields = EagerViewFields.Enumerate(document.BuildView()).ToDictionary(field => field.ValueId);
        var career = fields[RiftScalarWrites.CareerId];
        var original = document.GetValue(career.ValueId);
        Assert.True(document.CanEdit);
        Assert.False(career.Spec.ReadOnly);
        Assert.Equal(Confidence.Inferred, career.Confidence);
        Assert.Contains("Not validated on console", career.Warning);
        Assert.True(document.ApplyEdits([new(career.ValueId, FieldValue.Of(original.AsInt64() - 1))]).Succeeded);
        Assert.Equal(original.AsInt64() - 1, document.GetValue(career.ValueId).AsInt64());
        var exported = Assert.Single(document.Serialize()).Content.ToArray();
        using var reopened = Open(exported);
        _ = reopened.BuildView();
        Assert.Equal(document.GetValue(career.ValueId), reopened.GetValue(career.ValueId));
        var originalContainer = Dat1Container.Parse(bytes);
        var exportedContainer = Dat1Container.Parse(exported);
        Assert.Equal(originalContainer.SectionBytes(Assert.Single(originalContainer.Sections, section => section.Tag == InventoryReader.SectionTag)).ToArray(),
            exportedContainer.SectionBytes(Assert.Single(exportedContainer.Sections, section => section.Tag == InventoryReader.SectionTag)).ToArray());
        Assert.True(document.ApplyEdits([new(career.ValueId, original)]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    internal static SectionNode Resolution(ISaveDocument document, string currency) =>
        Assert.Single(Assert.Single(Assert.IsType<TabsNode>(document.BuildView()).Tabs, tab => tab.Title == "Currencies")
            .Children.OfType<SectionNode>(), section => section.Title.StartsWith(currency, StringComparison.Ordinal));

    private static ISaveDocument Open(byte[] bytes) =>
        new InsomniacPlugin().Open(MemorySaveSet.SingleFile("i29-s.save", bytes, "PPSA01474"));

    private static void AssertUnresolved(ISaveDocument document, byte[] bytes)
    {
        Assert.Throws<KeyNotFoundException>(() => document.GetValue(RiftCurrencyView.RaritaniumId));
        Assert.False(document.ApplyEdits([new(RiftCurrencyView.RaritaniumId, FieldValue.Of(0u))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static byte[] JoinInventoryBlocks(byte[] first, byte[] second)
    {
        byte[] bytes = [.. first, .. second.AsSpan(SectionStart)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), (uint)(bytes.Length - SectionStart));
        return bytes;
    }
}
