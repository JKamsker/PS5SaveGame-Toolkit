using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class LayoutTests
{
    [Fact]
    public void TenSlots_WithVariableItemSizes_AreDetectedAtTheirOwnBoundaries()
    {
        var fixture = new SyntheticSave(Enumerable.Range(0, SyntheticSave.SlotCount).ToArray());
        using var document = fixture.Open();
        var view = Assert.IsType<TabsNode>(document.BuildView());
        var table = Assert.IsType<TableNode>(view.Tabs[0].Children[^1]);
        Assert.Equal(SyntheticSave.SlotCount, table.Rows.Count);
        Assert.True(document.CanEdit);
        foreach (var index in fixture.Players.Keys)
        {
            var fields = document.ExpandNode($"characters/{index}").OfType<FieldNode>().ToArray();
            Assert.Equal($"Hero {index}", document.GetValue($"slots/{index}/name").AsString());
            Assert.Equal(SyntheticSave.InitialRunes, document.GetValue($"slots/{index}/runes").AsUInt32());
            Assert.Single(fields, field => !field.Spec.ReadOnly);
        }
    }

    [Theory]
    [InlineData(81U, false)]
    [InlineData(82U, false)]
    [InlineData(150U, true)]
    [InlineData(262U, false)]
    public void VersionedItemCountsAndUnicodeNames_AreParsed_OnlySampledVersionIsWritable(uint version, bool writable)
    {
        const string name = "褪せ人🌙";
        var fixture = new SyntheticSave([9], version);
        fixture.Rename(9, name);
        using var document = fixture.Open();
        var fields = document.ExpandNode("characters/9").OfType<FieldNode>().ToArray();
        Assert.Equal(name, document.GetValue("slots/9/name").AsString());
        Assert.Equal(writable, document.CanEdit);
        Assert.Equal(!writable, fields.Single(field => field.ValueId == "slots/9/runes").Spec.ReadOnly);
        Assert.Equal(Confidence.Verified, fields.Single(field => field.ValueId == "slots/9/name").Confidence);
        Assert.True(fixture.Bytes.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
    }

    [Fact]
    public void ProfileVersionWithoutWriteEvidence_IsReadOnly()
    {
        var fixture = new SyntheticSave([0]);
        fixture.Write32(SyntheticSave.UserData, 91);
        using var document = fixture.Open();
        Assert.False(document.CanEdit);
        Assert.All(document.ExpandNode("characters/0").OfType<FieldNode>(), field => Assert.True(field.Spec.ReadOnly));
        Assert.False(document.ApplyEdits([new ValueEdit("slots/0/runes", FieldValue.Of(101L))]).Succeeded);
    }

    [Fact]
    public void InactiveProfiles_IgnoreDeletedCharacterResidueAndInvalidUtf16()
    {
        const int deletedIndex = 3;
        var fixture = new SyntheticSave([0]);
        fixture.Write32(SyntheticSave.Slot(deletedIndex), 150);
        fixture.Bytes.AsSpan(fixture.Profile(deletedIndex), SyntheticSave.NameBytes).Fill(0xFF);
        using var document = fixture.Open();
        Assert.IsType<TextNode>(Assert.Single(document.ExpandNode($"characters/{deletedIndex}")));
        Assert.Throws<KeyNotFoundException>(() => document.GetValue($"slots/{deletedIndex}/name"));
        Assert.True(fixture.Bytes.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
    }

    [Fact]
    public void EmptyAllocation_HasTenInactiveProfilesAndNoWritableFields()
    {
        var fixture = new SyntheticSave([]);
        using var document = fixture.Open();
        var tabs = Assert.IsType<TabsNode>(document.BuildView());
        var table = Assert.IsType<TableNode>(tabs.Tabs[0].Children[^1]);
        Assert.Equal(SyntheticSave.SlotCount, table.Rows.Count);
        Assert.False(document.CanEdit);
        Assert.All(table.Rows, row => Assert.Equal("Inactive", Assert.IsType<TextNode>(row.Cells["status"]).Text));
    }

    [Fact]
    public void MenuLength_IsUsedRatherThanAFixedSummaryOffset()
    {
        var fixture = new SyntheticSave([0, 8], menuSize: 0x1810);
        using var document = fixture.Open();
        _ = document.BuildView();
        Assert.Equal("Hero 8", document.GetValue("slots/8/name").AsString());
    }

    [Theory]
    [InlineData("PPSA03169")]
    [InlineData("PPSA04608")]
    [InlineData("PPSA04609")]
    [InlineData("PPSA04610")]
    public void Probe_RequiresSupportedRetailTitleAndCorrectFileRole(string title)
    {
        var fixture = new SyntheticSave([0]);
        var plugin = new EldenRingPlugin();
        Assert.Equal(ProbeScore.Title, plugin.Probe(fixture.File(title)).Score);
        Assert.Equal(ProbeScore.Title, plugin.Probe(MemorySaveSet.SingleFile("sce_sdmemory/memory.dat", fixture.Bytes, title)).Score);
        Assert.False(plugin.Probe(MemorySaveSet.SingleFile("other.dat", fixture.Bytes, title)).IsMatch);
        Assert.False(plugin.Probe(fixture.File("PPSA04616")).IsMatch);
        Assert.False(plugin.Probe(fixture.File("PPSA99999")).IsMatch);
        fixture.Bytes[0] ^= 1;
        Assert.False(plugin.Probe(fixture.File(title)).IsMatch);
    }
}
