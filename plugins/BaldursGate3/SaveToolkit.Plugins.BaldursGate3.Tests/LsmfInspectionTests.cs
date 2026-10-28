using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class LsmfInspectionTests
{
    [Fact]
    public void OptionalUnsupportedNewAgeHasAnExplicitDiagnosticAndExactNoOp()
    {
        var blob = LsmfFixture.Entities();
        blob[7] = 9;
        var resource = LsmfFixture.Resource(blob);
        using var document = new BaldursGate3Plugin().Open(MemorySaveSet.SingleFile("Globals.lsf", resource));
        var tab = Assert.Single(Assert.IsType<TabsNode>(document.BuildView()).Tabs, tab => tab.Title == "NewAge");
        var lazy = Assert.Single(tab.Children.OfType<SectionNode>());
        var message = Assert.Single(document.ExpandNode(lazy.LazyId!).OfType<TextNode>());
        Assert.Contains("Unsupported LSMF", message.Text, StringComparison.Ordinal);
        Assert.Equal(Tone.Warning, message.Tone);
        Assert.Equal(resource, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void EntityAndComponentPagesAreLazyReadOnlyAndRejectForgedRanges()
    {
        using var document = new BaldursGate3Plugin().Open(MemorySaveSet.SingleFile("Globals.lsf",
            LsmfFixture.Resource(LsmfFixture.Entities(20_000))));
        var tab = Assert.Single(Assert.IsType<TabsNode>(document.BuildView()).Tabs, tab => tab.Title == "NewAge");
        Assert.Empty(tab.Children.OfType<TableNode>());
        var root = Assert.Single(tab.Children.OfType<SectionNode>()).LazyId!;
        var overview = document.ExpandNode(root);
        var pages = document.ExpandNode(root + "/entities");
        Assert.InRange(pages.Count, 1, 128);
        Assert.All(pages, page => Assert.NotNull(Assert.IsType<SectionNode>(page).LazyId));
        var subpages = document.ExpandNode(Assert.IsType<SectionNode>(pages[0]).LazyId!);
        Assert.InRange(subpages.Count, 1, 128);
        var table = Assert.Single(document.ExpandNode(Assert.IsType<SectionNode>(subpages[0]).LazyId!).OfType<TableNode>());
        Assert.Equal(128, table.Rows.Count);
        Assert.Equal(128, table.Rows.Select(row => row.Key).Distinct().Count());
        var field = Assert.IsType<FieldNode>(table.Rows[0].Cells["guid"]);
        Assert.True(field.Spec.ReadOnly);
        Assert.Equal(field.Value, document.GetValue(field.ValueId));
        Assert.False(document.ApplyEdits([new ValueEdit(field.ValueId, FieldValue.Of("changed"))]).Succeeded);
        Assert.Single(overview.OfType<FieldNode>()); // full unknown-byte accountability fingerprint
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode(root + "/entities/20000/1"));
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode(root + "/components/0/999999"));
    }

    [Theory]
    [InlineData(80, true)] // nominal unknown component
    [InlineData(8, false)] // opaque header value
    [InlineData(-1, false)] // unknown trailing byte
    public void F1ViewDecoderAccountsForUnknownBytesInsideAndOutsideNominalComponents(int offset, bool inComponent)
    {
        var before = LsmfFixture.Entities();
        var after = before.ToArray();
        after[offset < 0 ? after.Length - 1 : offset] ^= 0xff;
        var decoder = new PluginViewDecoder(new BaldursGate3Plugin());
        var oldTree = decoder.Decode(MemorySaveSet.SingleFile("Globals.lsf", LsmfFixture.Resource(before)), new DecodeLimits());
        var newTree = decoder.Decode(MemorySaveSet.SingleFile("Globals.lsf", LsmfFixture.Resource(after)), new DecodeLimits());
        Assert.False(oldTree.Truncated);
        Assert.False(newTree.Truncated);
        var oldValues = oldTree.Values.ToDictionary(value => value.Path, value => value.Value);
        var newValues = newTree.Values.ToDictionary(value => value.Path, value => value.Value);
        var changed = oldValues.Keys.Where(key => oldValues[key] != newValues[key]).Order(StringComparer.Ordinal).ToArray();
        string[] expected = inComponent
            ? ["lsmf/0/0/component/unknown.v91.Component/sha256", "lsmf/0/0/sha256"]
            : ["lsmf/0/0/sha256"];
        Assert.Equal(expected, changed);
    }
}
