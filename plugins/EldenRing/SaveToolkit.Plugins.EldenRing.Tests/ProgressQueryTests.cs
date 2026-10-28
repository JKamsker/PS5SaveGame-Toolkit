using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class ProgressQueryTests
{
    [Fact]
    public void AllProgressRowsArePagedSearchableAndFilterableBeforePaging()
    {
        var fixture = new SyntheticSave([0]);
        var slot = EldenRingParser.Parse(fixture.Bytes).Slots[0];
        var range = SemanticReader.Read(fixture.Bytes, slot).Ranges.Single(range => range.Name == SemanticLayout.EventRangeName);
        fixture.Bytes[range.Offset + 0xCBE] = 1 << 3; // Source Church of Elleh bit, independent of eventId/8.
        var plugin = new EldenRingPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA03169");
        using var document = plugin.Open(fixture.File(), references);
        var browser = (IQueryableDocument)document;
        var keys = new HashSet<string>();
        for (var offset = 0; offset < 499; offset += 200)
        {
            var page = browser.Query("slots/0/progress", new() { Offset = offset, Limit = int.MaxValue, SortColumn = "event" });
            Assert.Equal(499, page.TotalCount);
            Assert.InRange(page.Rows.Count, 1, 200);
            Assert.All(page.Rows, row => Assert.True(keys.Add(row.Key)));
        }
        Assert.Equal(499, keys.Count);
        var graces = browser.Query("slots/0/progress", new() { Filters = new Dictionary<string, string> { ["category"] = "grace" }, Limit = 1 });
        Assert.Equal(314, graces.FilteredCount);
        var row = Assert.Single(browser.Query("slots/0/progress", new() { Text = "Church of Elleh" }).Rows);
        Assert.True(Assert.IsType<FieldNode>(row.Cells["raw"]).Value.AsBool());
        var reference = Assert.IsType<FieldNode>(row.Cells["name"]);
        Assert.Equal("grace:ChurchofElleh", reference.Value.AsString());
        Assert.Equal(ProgressReferences.CatalogId, Assert.IsType<ReferenceSpec>(reference.Spec).CatalogId);
        Assert.True(reference.Spec.ReadOnly);
        Assert.False(document.ApplyEdits([new(reference.ValueId, FieldValue.Of("boss:GodricktheGrafted"))]).Succeeded);
        var descending = browser.Query("slots/0/progress", new() { SortColumn = "event", Descending = true, Limit = 2 });
        var events = descending.Rows.Select(row => uint.Parse(Assert.IsType<TextNode>(row.Cells["event"]).Text,
            System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.True(events[0] >= events[1]);
        Assert.Throws<ArgumentException>(() => browser.Query("slots/0/progress", new() { SortColumn = "bad" }));
        Assert.Empty(browser.Query("slots/0/progress", new() { Offset = int.MaxValue }).Rows);
        WriteTestSupport.EqualBytes(fixture.Bytes, document);
    }

    [Theory]
    [InlineData(0, "Vagabond")]
    [InlineData(1, "Warrior")]
    [InlineData(6, "Confessor")]
    [InlineData(255, null)]
    public void ClassIdentityUsesSharedReadOnlyReferenceSpecAndKeepsRawFallback(byte id, string? expected)
    {
        var fixture = new SyntheticSave([0]);
        fixture.Bytes[fixture.Players[0] + 0xB7] = id;
        var plugin = new EldenRingPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA03169");
        using var document = plugin.Open(fixture.File(), references);
        var field = Assert.Single(document.ExpandNode("characters/0").OfType<FieldNode>(), field => field.ValueId == "slots/0/class-reference");
        Assert.Equal(ProgressReferences.ClassCatalogId, Assert.IsType<ReferenceSpec>(field.Spec).CatalogId);
        Assert.Equal(id.ToString(System.Globalization.CultureInfo.InvariantCulture), field.Value.AsString());
        Assert.True(field.Spec.ReadOnly);
        Assert.Equal((uint)id, document.GetValue("slots/0/class-id").AsUInt32());
        Assert.Contains(expected ?? "Unknown class", field.Description);
        WriteTestSupport.EqualBytes(fixture.Bytes, document);
    }

    [Fact]
    public void MissingProgressPackHasHonestEmptyCollectionAndSourceNotice()
    {
        var fixture = new SyntheticSave([0]);
        using var document = fixture.Open();
        Assert.Contains(document.ExpandNode("progress/0"), node => node is TextNode text && text.Text.Contains("No compatible", StringComparison.Ordinal));
        Assert.Empty(((IQueryableDocument)document).Query("slots/0/progress", new()).Rows);
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode("progress/0/not-a-region"));
        WriteTestSupport.EqualBytes(fixture.Bytes, document);
    }

    [Fact]
    public void ReadOnlyProfileStillAllowsCandidateBrowsingWithoutMutatingActions()
    {
        var fixture = new SyntheticSave([0]);
        fixture.Write32(SyntheticSave.UserData, 91);
        var plugin = new EldenRingPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA03169");
        using var document = plugin.Open(fixture.File(), references);
        Assert.False(document.CanEdit);
        var page = ((IQueryableDocument)document).Query("slots/0/progress", new() { Text = "Church of Elleh" });
        var field = Assert.IsType<FieldNode>(Assert.Single(page.Rows).Cells["raw"]);
        Assert.True(field.Spec.ReadOnly);
        Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of(true))]).Succeeded);
        WriteTestSupport.EqualBytes(fixture.Bytes, document);
    }

    [Theory]
    [InlineData(-1, 1, 0, 0)]
    [InlineData(0, 5, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 1, 1, 0)]
    [InlineData(0, 1, -1, 0)]
    [InlineData(0, 1, 0, 8)]
    [InlineData(0, 1, 0, -1)]
    [InlineData(int.MaxValue, 1, 0, 0)]
    public void ProgressBitReaderRejectsMalformedOrTruncatedBounds(int start, int length, int offset, int bit) =>
        Assert.Throws<SaveFormatException>(() => ProgressViews.Read(new byte[4], new("test", start, length), offset, bit));

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void BitReaderUsesExactLsbNumberingAndLastByte(int bit)
    {
        byte[] bytes = [0, (byte)(1 << bit)];
        Assert.True(ProgressViews.Read(bytes, new("test", 1, 1), 0, bit));
        Assert.False(ProgressViews.Read(bytes, new("test", 1, 1), 0, bit == 0 ? 7 : 0));
    }
}
