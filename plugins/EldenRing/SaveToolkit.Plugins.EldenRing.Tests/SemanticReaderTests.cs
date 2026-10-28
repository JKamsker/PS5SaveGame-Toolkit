using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class SemanticReaderTests
{
    [Fact]
    public void ArchetypeIsReadIndependentlyFromTheNearbyGiftByte()
    {
        var fixture = new SemanticFixture();
        fixture.Save.Bytes[fixture.Player + 0xB7] = 6;
        fixture.Save.Bytes[fixture.Player + 0xBB] = 2;
        using var document = fixture.Save.Open();
        _ = document.ExpandNode("characters/0");
        Assert.Equal(6U, document.GetValue("slots/0/class-id").AsUInt32());
    }

    [Fact]
    public void InstancesDirectIdsDuplicatesUnknownsAndStorage_AreRetainedWithoutWrites()
    {
        var fixture = new SemanticFixture();
        fixture.Instance(1, 0x80010001, 0x006CF2A0);
        fixture.Instance(2, 0x90010002, 0x10001000);
        fixture.Instance(3, 0xC0010003, 0x80001000);
        fixture.Row("held", "common", 0, 0x80010001, 1, 10);
        fixture.Row("held", "common", 1, 0x80010001, 1, 11);
        fixture.Row("held", "common", 2, 0xB00050F0, 12, 12);
        fixture.Row("held", "common", 3, 0xA0001000, 1, 13);
        fixture.Row("held", "common", 4, 0xC0010003, 1, 14);
        fixture.Row("held", "common", 5, 0x8001FFFF, 1, 15);
        fixture.Row("held", "common", 6, uint.MaxValue, 0, 0);
        fixture.Row("held", "key", 0, 0xB0002198, 1, 16);
        fixture.Row("storage", "common", 0, 0x90010002, 1, 17);
        using var document = fixture.Save.Open();
        var collections = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var held = collections.Query("slots/0/inventory/held", new());
        Assert.Equal(8, held.TotalCount);
        Assert.Equal(8, held.Rows.Select(row => row.Key).Distinct().Count());
        var ids = held.Rows.Select(row => Assert.IsType<FieldNode>(row.Cells["item"]).Value.AsString()).ToArray();
        Assert.Equal(2, ids.Count(id => id == "0x006CF2A0"));
        Assert.Contains("0x400050F0", ids);
        Assert.Contains("0x20001000", ids);
        Assert.Contains("0x80001000", ids);
        Assert.Contains("handle:0x8001FFFF", ids);
        Assert.Contains("handle:0xFFFFFFFF", ids);
        Assert.Equal("0x10001000", Assert.IsType<FieldNode>(Assert.Single(collections.Query("slots/0/inventory/storage", new()).Rows).Cells["item"]).Value.AsString());
        foreach (var field in held.Rows.Select(row => Assert.IsType<FieldNode>(row.Cells["item"])))
        {
            Assert.IsType<ReferenceSpec>(field.Spec);
            Assert.True(field.Spec.ReadOnly);
            Assert.Equal(field.Value, document.GetValue(field.ValueId));
            Assert.False(document.ApplyEdits([new ValueEdit(field.ValueId, FieldValue.Of("0x400050F0"))]).Succeeded);
        }
        Assert.True(fixture.Save.Bytes.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
    }

    [Fact]
    public void DuplicateMapHandles_AreAmbiguousRatherThanSilentlyChoosingOne()
    {
        var fixture = new SemanticFixture();
        fixture.Instance(1, 0x80010001, 0x006CF2A0);
        fixture.Instance(3, 0x80010001, 0x40001000);
        fixture.Row("held", "common", 0, 0x80010001, 1, 10);
        using var document = fixture.Save.Open();
        var row = Assert.Single(((IQueryableDocument)document).Query("slots/0/inventory/held", new()).Rows);
        Assert.Equal("handle:0x80010001", Assert.IsType<FieldNode>(row.Cells["item"]).Value.AsString());
        Assert.Contains("ambiguous", Assert.IsType<TextNode>(row.Cells["resolution"]).Text);
    }

    [Theory]
    [InlineData("held-common")]
    [InlineData("held-key")]
    [InlineData("storage-common")]
    [InlineData("projectiles")]
    [InlineData("regions")]
    public void CountsOutsideBoundedCharacterRanges_AreRejected(string target)
    {
        var fixture = new SemanticFixture();
        var offset = target switch
        {
            "held-common" => fixture.Held,
            "held-key" => fixture.Held + 4 + 2688 * 12,
            "storage-common" => fixture.Storage,
            "projectiles" => fixture.ProjectileCount,
            _ => fixture.RegionCount,
        };
        fixture.Save.Write32(offset, uint.MaxValue);
        Assert.Throws<SaveFormatException>(() => fixture.Save.Open());
    }

    [Fact]
    public void CountMismatch_PreservesResidueAndWarnsInsteadOfRepairing()
    {
        var fixture = new SemanticFixture();
        fixture.Row("held", "common", 0, 0xB00050F0, 0, 10);
        fixture.Save.Write32(fixture.Held, 0);
        using var document = fixture.Save.Open();
        var collection = ((IQueryableDocument)document).Collections.Single(collection => collection.Id == "slots/0/inventory/held");
        Assert.Equal(1, collection.TotalCount);
        Assert.Contains("residue", collection.Description);
        Assert.True(fixture.Save.Bytes.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
    }

    [Theory]
    [InlineData(7001U)]
    [InlineData(uint.MaxValue)]
    public void AcquisitionCountsCannotExceedTheFixedSixteenByteRowArray(uint count)
    {
        var fixture = new SemanticFixture();
        var range = SemanticReader.Read(fixture.Save.Bytes, EldenRingParser.Parse(fixture.Save.Bytes).Slots[0])
            .Ranges.Single(range => range.Name == SemanticLayout.AcquisitionRangeName);
        fixture.Save.Write32(range.Offset, count);
        var error = Assert.Throws<SaveFormatException>(() => fixture.Save.Open());
        Assert.Contains("Acquisition count", error.Message);
    }

    [Fact]
    public void UnsampledVersions_DoNotInterpretDeeperData()
    {
        var fixture = new SyntheticSave([0], version: 151);
        fixture.Write32(fixture.Players[0] + 0x3A4, uint.MaxValue);
        using var document = fixture.Open();
        Assert.False(document.CanEdit);
        Assert.DoesNotContain(((IQueryableDocument)document).Collections, collection => collection.Id.Contains("/inventory/", StringComparison.Ordinal));
        Assert.Contains(document.ExpandNode("characters/0"), node => node is TextNode text && text.Text.Contains("restricted", StringComparison.Ordinal));
    }

    [Fact]
    public void WholeCollectionSearchFilterNumericSortAndPaging_AreDeterministic()
    {
        var fixture = new SemanticFixture();
        for (var index = 0; index < 250; index++)
            fixture.Row("held", "common", index, 0xB00050F0, (uint)index, (uint)index);
        fixture.Row("held", "key", 0, 0xB0002198, 300, 300);
        using var document = fixture.Save.Open();
        var queryable = (IQueryableDocument)document;
        var query = new QueryableCollectionQuery { SortColumn = "quantity", Descending = true, Limit = 2 };
        var page = queryable.Query("slots/0/inventory/held", query);
        Assert.Equal(251, page.FilteredCount);
        Assert.Equal([300U, 249U], page.Rows.Select(row => Assert.IsType<FieldNode>(row.Cells["quantity"]).Value.AsUInt32()));
        var next = queryable.Query("slots/0/inventory/held", query with { Offset = 2 });
        Assert.Empty(page.Rows.Select(row => row.Key).Intersect(next.Rows.Select(row => row.Key)));
        var filtered = queryable.Query("slots/0/inventory/held", query with { Filters = new Dictionary<string, string> { ["group"] = "key" } });
        Assert.Equal(1, filtered.FilteredCount);
        Assert.Single(filtered.Rows);
        Assert.Equal(200, queryable.Query("slots/0/inventory/held", new() { Limit = int.MaxValue }).Rows.Count);
        Assert.Single(queryable.Query("slots/0/inventory/held", new() { Text = "0x40002198" }).Rows);
        Assert.Empty(queryable.Query("slots/0/inventory/held", new() { Offset = int.MaxValue }).Rows);
        Assert.Throws<ArgumentException>(() => queryable.Query("slots/0/inventory/held", new() { SortColumn = "typo" }));
        Assert.Throws<ArgumentException>(() => queryable.Query("slots/0/inventory/held", new() { Cursor = "unexpected" }));
        Assert.Throws<KeyNotFoundException>(() => queryable.Query("missing", new()));
    }
}
