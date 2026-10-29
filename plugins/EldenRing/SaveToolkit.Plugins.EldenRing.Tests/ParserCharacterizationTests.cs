using System.Security.Cryptography;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class ParserCharacterizationTests
{
    private const string LegacySupportedDigest = "295d4a51e39b0963081dbeff031f5effef22da9e15b0b92d085f125ad11b5a71";

    [Theory]
    [InlineData(81U, "4dcf26fa38c1ad56ba88d981de6824902589c4a6b9721431af8997545077caff")]
    [InlineData(150U, "73d05bbdef20f1873c79eda31c8b05cfc126bb7495933ed84e367ef1c82fc0bb")]
    [InlineData(151U, "7fbf94b91d5f28e5b3a1de325645db401f2fde595a524cb2c9c3bac8f8e624f1")]
    public void VersionedSlotsKeepTheirCompleteViewProtocolAndNoopBytes(uint version, string expectedDigest)
    {
        var fixture = new SyntheticSave([0, 9], version);
        using var document = fixture.Open();
        ViewNode[] views = [document.BuildView(), new SectionNode("First", document.ExpandNode("characters/0")),
            new SectionNode("Last", document.ExpandNode("characters/9")),
            new SectionNode("Inactive", document.ExpandNode("characters/4"))];
        if (version == 150) AssertOnlyEditingColumnsWereAdded(document, views);
        // Includes every protocol string, ID and metadata item; no-op bytes remain identical.
        var digest = Digest(views);
        Assert.Equal(expectedDigest, digest);
        WriteTestSupport.EqualBytes(fixture.Bytes, document);
    }

    private static void AssertOnlyEditingColumnsWereAdded(ISaveDocument document, ViewNode[] views)
    {
        var root = Assert.IsType<TabsNode>(views[0]);
        var inventory = Assert.Single(root.Tabs, tab => tab.Title == "Inventory / storage");
        var tables = inventory.Children.Select(node => Assert.IsType<QueryableTableNode>(node)).ToArray();
        Assert.Equal(["slots/0/inventory/held", "slots/0/inventory/storage", "slots/9/inventory/held", "slots/9/inventory/storage"],
            tables.Select(table => table.CollectionId));
        foreach (var table in tables)
        {
            Assert.Equal(["item", "category", "sourceGroup", "quantity", "editing", "group", "handle", "index", "resolution", "details", "refill"],
                table.Columns.Select(column => column.Key));
            Assert.Equal(new QueryableColumn("editing", "Editing")
            {
                Description = "Filter by Editable quantity, Protected or Read-only version. Details explain the opening-time restrictions.",
            }, Assert.Single(table.Columns, column => column.Key == "editing"));
            var row = Assert.Single(((IQueryableDocument)document).Query(table.CollectionId, new() { IncludeEmpty = true, Limit = 1 }).Rows);
            Assert.True(row.IsEmpty);
            Assert.Equal("Protected", Assert.IsType<TextNode>(row.Cells["editing"]).Text);
            var quantity = Assert.IsType<FieldNode>(row.Cells["quantity"]);
            Assert.True(quantity.Spec.ReadOnly);
            Assert.Equal(Confidence.Unknown, quantity.Confidence);
        }

        // Remove exactly the verified additive column; all original IDs, values, ordering,
        // read-only flags, descriptions and attribution still match the pre-change fingerprint.
        var previousInventory = inventory with
        {
            Children = tables.Select(table => (ViewNode)(table with
            {
                Columns = table.Columns.Where(column => column.Key != "editing").ToArray(),
            })).ToArray(),
        };
        ViewNode[] previousViews = [root with
        {
            Tabs = root.Tabs.Select(tab => tab == inventory ? previousInventory : tab).ToArray(),
        }, .. views.Skip(1)];
        Assert.Equal(LegacySupportedDigest, Digest(previousViews));
    }

    private static string Digest(ViewNode[] views) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(views)));

    [Theory]
    [InlineData(0, "flag", "invalid active-profile flag.")]
    [InlineData(9, "flag", "invalid active-profile flag.")]
    [InlineData(0, "version", "active profile has an empty character block.")]
    [InlineData(9, "version", "active profile has an empty character block.")]
    [InlineData(0, "name", "character name/level disagrees with its profile, or the layout is unsupported.")]
    [InlineData(9, "name", "character name/level disagrees with its profile, or the layout is unsupported.")]
    public void SlotFailuresKeepExactMessagesAndPrecedence(int index, string firstFailure, string message)
    {
        var fixture = new SyntheticSave([0, 9]);
        // Later errors must not replace the first slot/field failure.
        fixture.Write32(fixture.Players[9] + SyntheticSave.Attributes, 100);
        fixture.Write32(fixture.Players[index] + SyntheticSave.Attributes, 100);
        fixture.Bytes[fixture.Profile(index)] ^= 1;
        if (firstFailure is "flag" or "version") fixture.Write32(SyntheticSave.Slot(index), 0);
        if (firstFailure == "flag") fixture.Bytes[fixture.ActiveOffset + index] = 2;
        var error = Assert.Throws<SaveFormatException>(() => fixture.Open());
        Assert.Equal($"Slot {index}: {message}", error.Message);
    }
}
