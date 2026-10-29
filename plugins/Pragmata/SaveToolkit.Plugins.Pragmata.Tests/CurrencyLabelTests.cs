using System.Buffers.Binary;
using System.Globalization;
using SaveToolkit.Core.References;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class CurrencyLabelTests
{
    private const string Source = "https://github.com/jestelf/PragmataTrainer/blob/570400495c8d29106b84caea43de2ba8aee7bfff/src/PragmataTrainer.cs";
    private static readonly IReadOnlyDictionary<uint, string> Names = new Dictionary<uint, string>
    {
        [0x84c2f1f3] = "Lunafilament",
        [0x0b30ca95] = "Upgrade Components",
        [0x7d9b1f63] = "Pure Lunum",
    };

    [Theory]
    [InlineData("data000.bin")]
    [InlineData("data001Slot.bin")]
    [InlineData("data002Slot.bin")]
    [InlineData("data003Slot.bin")]
    [InlineData("data004Slot.bin")]
    [InlineData("data005Slot.bin")]
    [InlineData("data006Slot.bin")]
    [InlineData("data007Slot.bin")]
    [InlineData("data008Slot.bin")]
    [InlineData("data009Slot.bin")]
    [InlineData("data010Slot.bin")]
    public void EachGameplaySampleLabelsOnlySourceSupportedUniqueIdsAndPreservesBytes(string filename)
    {
        var plugin = new PragmataPlugin();
        var context = Context(plugin);
        var file = SampleEvidenceTests.Files().Single(file => file.FileName == filename);
        using var document = (PragmataDocument)plugin.Open(file, context);
        var table = Inventory(document);
        Assert.Equal(90, table.Rows.Count);
        Assert.Equal(90, document.Layout.Items.Select(item => document.Read(item.Find(KnownHashes.ItemId)!).AsUInt32()).Distinct().Count());
        foreach (var row in table.Rows)
        {
            var field = Assert.IsType<FieldNode>(row.Cells["name"]);
            var id = uint.Parse(field.Value.AsString(), CultureInfo.InvariantCulture);
            if (Names.TryGetValue(id, out var name)) AssertNamed(field, id, name);
            else
            {
                Assert.Equal($"Unmapped item 0x{id:x8}", field.Label);
                Assert.Equal(Confidence.Unknown, field.Confidence);
            }
        }
        foreach (var (id, name) in Names)
        {
            var row = Assert.Single(table.Rows, row => ((FieldNode)row.Cells["name"]).Value.AsString() == id.ToString(CultureInfo.InvariantCulture));
            AssertNamed((FieldNode)row.Cells["name"], id, name);
            Assert.True(((FieldNode)row.Cells["quantity"]).Value.AsInt32() >= 0);
        }
        Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(0x84c2f1f3U, "Lunafilament")]
    [InlineData(0x0b30ca95U, "Upgrade Components")]
    [InlineData(0x7d9b1f63U, "Pure Lunum")]
    public void SyntheticLabelsCarryAttributionWithoutChangingQuantityBoundsOrRestoreEligibility(uint id, string name)
    {
        var original = Synthetic(id, openingQuantity: 10);
        var plugin = new PragmataPlugin();
        using var named = new PragmataDocument("data000.bin", original, Context(plugin));
        using var raw = new PragmataDocument("data000.bin", original, IReferenceContext.Empty);
        var namedRow = Assert.Single(Inventory(named).Rows);
        var rawRow = Assert.Single(Inventory(raw).Rows);
        AssertNamed(Assert.IsType<FieldNode>(namedRow.Cells["name"]), id, name);
        Assert.Equal(Confidence.Unknown, ((FieldNode)rawRow.Cells["name"]).Confidence);
        var namedQuantity = Assert.IsType<FieldNode>(namedRow.Cells["quantity"]);
        Assert.Equal(IntegerSpec.Int32, namedQuantity.Spec);
        Assert.Equal(((FieldNode)rawRow.Cells["quantity"]).Spec, namedQuantity.Spec);
        Assert.Null(named.References.Item(id)!.Limit);
        Assert.Equal(original, named.Serialize().Single().Content.ToArray());

        // Display metadata must not change the existing opening-baseline policy.
        foreach (var quantity in new[] { -1, 0, 9, 10, 11 })
        {
            Assert.True(named.ApplyEdits([new(namedQuantity.ValueId, FieldValue.Of(quantity))]).Succeeded);
            Assert.True(raw.ApplyEdits([new(namedQuantity.ValueId, FieldValue.Of(quantity))]).Succeeded);
            var namedPlan = named.PlanAction(ImportedQuantityTests.ActionId, ImportedQuantityTests.NoParameters, new());
            var rawPlan = raw.PlanAction(ImportedQuantityTests.ActionId, ImportedQuantityTests.NoParameters, new());
            Assert.Empty(namedPlan.Errors);
            Assert.Equal(rawPlan.Changes.ToArray(), namedPlan.Changes.ToArray());
            Assert.Equal(rawPlan.SkippedRows.ToArray(), namedPlan.SkippedRows.ToArray());
            Assert.Equal(quantity is 0 or 9 ? 1 : 0, namedPlan.Changes.Count);
        }
        Assert.True(named.ApplyEdits([new(namedQuantity.ValueId, FieldValue.Of(10))]).Succeeded);
        Assert.Equal(original, named.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void CatalogKeepsEveryUnsupportedCandidateUnknownAndWithoutLimits()
    {
        var plugin = new PragmataPlugin();
        var catalog = Context(plugin).Catalog(ReferenceReadouts.ItemsCatalog)!;
        var records = catalog.Search(new ReferenceQuery { Limit = 200 }).Items;
        Assert.Equal(90, records.Count);
        Assert.Equal(3, records.Count(record => record.Attributes["confidence"] == "Inferred"));
        foreach (var record in records)
        {
            var id = uint.Parse(record.Key, CultureInfo.InvariantCulture);
            Assert.Null(record.Limit);
            if (Names.ContainsKey(id)) continue;
            Assert.Equal("Unknown", record.Attributes["confidence"]);
            Assert.Equal("unmapped", record.Category);
            Assert.Equal($"Unmapped item 0x{id:x8}", record.Name);
            Assert.Equal("https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/21", record.Source);
        }
        Assert.Contains(Source, plugin.Info.References);
    }

    private static IReferenceContext Context(PragmataPlugin plugin)
    {
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        return store.Pin(plugin, "PPSA02530");
    }

    private static void AssertNamed(FieldNode field, uint id, string name)
    {
        Assert.Equal(name, field.Label);
        Assert.Equal(Confidence.Inferred, field.Confidence);
        Assert.Equal(id.ToString(CultureInfo.InvariantCulture), field.Value.AsString());
        Assert.Contains(Source, field.Description!, StringComparison.Ordinal);
        Assert.Contains("jestelf", field.Description!, StringComparison.Ordinal);
        Assert.Contains("MIT", field.Description!, StringComparison.Ordinal);
        Assert.Contains($"0x{id:x8}", field.Description!, StringComparison.Ordinal);
    }

    private static TableNode Inventory(PragmataDocument document) => ((TabsNode)document.BuildView()).Tabs
        .Single(tab => tab.Title == "Inventory").Children.OfType<TableNode>().Single();

    private static byte[] Synthetic(uint id, int openingQuantity)
    {
        var container = new DsssContainer(SyntheticInventory.Create(1));
        var layout = new SaveLayout(container.Plaintext);
        var plaintext = container.Plaintext.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(plaintext.AsSpan(layout.Items[0].Find(KnownHashes.ItemId)!.Offset), id);
        BinaryPrimitives.WriteInt32LittleEndian(plaintext.AsSpan(layout.Items[0].Find(KnownHashes.Quantity)!.Offset), openingQuantity);
        return container.Rebuild(plaintext);
    }
}
