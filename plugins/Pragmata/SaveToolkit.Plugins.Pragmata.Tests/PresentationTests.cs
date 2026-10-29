using System.Globalization;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class PresentationTests
{
    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    public void InventoryLabelsDescribeStoredRecordsAndRetainNativeNamesAndWarnings(int itemCount)
    {
        using var document = new PragmataDocument("data000.bin", SyntheticInventory.Create(itemCount));
        var inventory = ((TabsNode)document.BuildView()).Tabs.Single(tab => tab.Title == "Inventory");
        IReadOnlyList<TableRow> rows;
        if (itemCount == 200)
        {
            var table = Assert.Single(inventory.Children.OfType<TableNode>());
            Assert.Equal("Stored inventory records", table.Title);
            Assert.Contains("_InventoryItems", table.Description!, StringComparison.Ordinal);
            rows = table.Rows;
        }
        else
        {
            var table = Assert.Single(inventory.Children.OfType<QueryableTableNode>());
            Assert.Equal("Stored inventory records", table.Title);
            Assert.Contains("_InventoryItems", table.Description!, StringComparison.Ordinal);
            Assert.Equal("inventory/items", table.CollectionId);
            var metadata = document.Collections.Single(collection => collection.Id == table.CollectionId);
            Assert.Equal(table.Title, metadata.Title);
            Assert.Equal(table.Description, metadata.Description);
            Assert.Contains("game meanings and legal limits remain unverified", metadata.Description!, StringComparison.Ordinal);
            rows = document.Query(table.CollectionId, new QueryableCollectionQuery { Limit = 1 }).Rows;
        }

        var notice = inventory.Children.OfType<TextNode>().First();
        Assert.StartsWith("Not validated on console. The game may reject or overwrite the edited save.", notice.Text, StringComparison.Ordinal);
        Assert.Contains("CityHash64 and Murmur3 are repaired; the RSA trailer is preserved but cannot be validated or regenerated.", notice.Text, StringComparison.Ordinal);
        Assert.Contains(inventory.Children.OfType<TextNode>(), note => note.Tone == Tone.Warning &&
            note.Text.Contains("Player-facing item mappings and legal stack limits are unverified.", StringComparison.Ordinal));
        var actions = inventory.Children.OfType<ActionNode>().ToArray();
        Assert.Equal(2, actions.Length);
        var action = Assert.Single(actions, node => node.ActionId == PragmataViews.QuantityAction);
        var rowActions = rows.SelectMany(row => Assert.IsType<SectionNode>(row.Cells["action"]).Children.OfType<ActionNode>()).ToArray();
        Assert.Equal(rows.Count * 2, rowActions.Length);
        foreach (var control in rowActions.Where(node => node.ActionId == PragmataViews.QuantityAction).Prepend(action))
        {
            Assert.Equal("inventory/set-quantities", control.ActionId);
            Assert.Equal("Set stored quantities", control.Label);
            var parameter = Assert.Single(control.Parameters!);
            Assert.Equal("quantity", parameter.ValueId);
            Assert.Equal("Target quantity", parameter.Label);
            Assert.Equal(Confidence.Inferred, parameter.Confidence);
            Assert.Equal(PragmataDocument.EditWarning, parameter.Warning);
            Assert.Contains("storage range", parameter.Description!, StringComparison.Ordinal);
            Assert.Contains("Legal limits and game effects remain unverified.", control.Description!, StringComparison.Ordinal);
            Assert.Contains("Does not add items or set unlock flags.", control.Description!, StringComparison.Ordinal);
        }
        foreach (var control in rowActions.Where(node => node.ActionId == PragmataViews.RestoreQuantityAction)
            .Prepend(Assert.Single(actions, node => node.ActionId == PragmataViews.RestoreQuantityAction)))
        {
            Assert.Equal("Restore imported quantities", control.Label);
            Assert.Empty(control.Parameters);
            Assert.True(control.SupportsPlanning);
            Assert.Contains("Inferred", control.Description!, StringComparison.Ordinal);
            Assert.Contains("immutable workspace opening amounts", control.Description!, StringComparison.Ordinal);
            Assert.StartsWith(PragmataDocument.EditWarning, control.Description!, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("data000.bin", 9)]
    [InlineData("data00-1.bin", 2)]
    public void EachTabStartsWithOneExperimentalNoticeAndIntegrityDetailsStayInHint(string fileName, int tabCount)
    {
        var file = SampleEvidenceTests.Files().Single(f => f.FileName == fileName);
        using var document = new PragmataDocument(file.RelativePath, file.ReadAllBytes());
        var view = Assert.IsType<TabsNode>(document.BuildView());
        Assert.Equal(tabCount, view.Tabs.Count);
        foreach (var tab in view.Tabs)
        {
            var notice = Assert.IsType<TextNode>(tab.Children[0]);
            Assert.Equal(PragmataDocument.EditWarning, notice.Text);
            Assert.Equal(Tone.Warning, notice.Tone);
            var nodes = tab.Children.SelectMany(Flatten).ToArray();
            Assert.Single(nodes.OfType<TextNode>(), n => n.Text == PragmataDocument.EditWarning);
            Assert.All(nodes.OfType<FieldNode>().Where(field => !field.Spec.ReadOnly),
                field => Assert.Equal(PragmataDocument.EditWarning, field.Warning));
        }

        var stats = view.Tabs.Single(t => t.Title == "Overview").Children.OfType<StatsNode>().Single();
        var integrity = stats.Items.Single(s => s.Label == "Integrity");
        Assert.Equal("Verified", integrity.Value);
        Assert.Matches(@"^Murmur3 \+ \d+ CityHash64 blocks \+ embedded key/IV verified$", integrity.Hint!);
        if (document.Layout.Header is null) Assert.Equal("Pragmata · System data", document.Title);
        else
        {
            var inventory = view.Tabs.Single(t => t.Title == "Inventory");
            Assert.Contains(inventory.Children.OfType<TextNode>(), note => note.Tone == Tone.Warning && note.Text.Contains("legal stack limits", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void FieldsKeepCompactProvenanceAndConfidenceIncludingLazyRawFields()
    {
        using var document = EditingTests.Gameplay();
        var difficulty = document.Layout.Header!.Find(KnownHashes.Difficulty)!;
        var field = document.Field(difficulty);
        Assert.StartsWith($"{difficulty.Path} · u32 @0x{difficulty.Offset:x} · {HashNames.Provenance(difficulty.Hash)}", field.Description!, StringComparison.Ordinal);
        Assert.Contains(PragmataDocument.EditWarning, field.Description!, StringComparison.Ordinal);
        Assert.Equal(Confidence.Inferred, field.Confidence);
        Assert.Equal(PragmataDocument.EditWarning, field.Warning);
        Assert.Equal(Confidence.Inferred, document.Field(document.Layout.Header.Find(KnownHashes.Playtime)!).Confidence);

        var view = Assert.IsType<TabsNode>(document.BuildView());
        var rawHeader = view.Tabs.Single(t => t.Title == "Raw").Children.OfType<SectionNode>()
            .Single(s => s.LazyId!.StartsWith($"tree/{document.Layout.Header.Path}/", StringComparison.Ordinal));
        var raw = document.ExpandNode(rawHeader.LazyId!).OfType<FieldNode>().Single(f => f.ValueId == $"raw/{difficulty.Path}");
        Assert.Equal(field.Description, raw.Description);
        Assert.Equal(field.Confidence, raw.Confidence);
        Assert.Equal(PragmataDocument.EditWarning, raw.Warning);
    }

    [Fact]
    public void EditablePrimitiveAndReferenceProjectionsPublishWarningsWithoutChangingStorage()
    {
        var original = SyntheticInventory.Create(1);
        using var document = new PragmataDocument("data000.bin", original);
        var item = document.Layout.Items[0];
        var unknown = item.Children.Single(node => node.Hash == SyntheticInventory.FloatHash);
        var fields = new[]
        {
            document.Field(item.Find(KnownHashes.Quantity)!),
            document.Field(unknown, raw: true),
            document.References.Primitive(unknown),
            document.References.ItemField(item.Find(KnownHashes.ItemId)!),
        };
        Assert.All(fields, field =>
        {
            Assert.False(field.Spec.ReadOnly);
            Assert.Equal(PragmataDocument.EditWarning, field.Warning);
            Assert.Contains(PragmataDocument.EditWarning, field.Description!, StringComparison.Ordinal);
        });
        Assert.Equal(Confidence.Inferred, fields[0].Confidence);
        Assert.All(fields.Skip(1), field => Assert.Equal(Confidence.Unknown, field.Confidence));
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void GameplayTitleShowsCurrentPlaytimeWithInvariantDecimalFormatting()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            using var document = EditingTests.Gameplay();
            Assert.Equal("Pragmata · Slot 0 · 25.7 h", document.Title);
            var playtime = document.Layout.Header!.Find(KnownHashes.Playtime)!;
            Assert.True(document.ApplyEdits([new(playtime.Path, FieldValue.Of(5_400_000_000UL))]).Succeeded);
            Assert.Equal("Pragmata · Slot 0 · 1.5 h", document.Title);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private static IEnumerable<ViewNode> Flatten(ViewNode node)
    {
        yield return node;
        IEnumerable<ViewNode> children = node switch
        {
            SectionNode section => section.Children,
            TableNode table => table.Rows.SelectMany(row => row.Cells.Values),
            _ => [],
        };
        foreach (var child in children.SelectMany(Flatten)) yield return child;
    }
}
