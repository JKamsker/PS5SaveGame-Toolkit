using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class CountAndProgressWriteTests
{
    [Theory]
    [InlineData("PPSA01911", "ff7remake000")]
    [InlineData("PPSA01911", "ff7remakeplus000")]
    [InlineData("PPSA08664", "ff7rebirth000")]
    [InlineData("PPSA18936", "ff7rebirth000")]
    public void EachProgressDomainEditsOneBitReopensAndUndoRedoPreservesNeighbors(string title, string name)
    {
        var file = SampleSaves.Load(title).AllDiskFiles.First(file => file.FileName == name);
        var plugin = new FinalFantasy7Plugin();
        var document = plugin.Open(file);
        using var session = new DocumentSession("bits", file, plugin, document);
        _ = session.BuildView();
        var rows = Enumerable.Range(0, 3).SelectMany(page => session.QueryCollection("progress", new() { Offset = page * 200, Limit = 200 }).Rows)
            .GroupBy(row => ((TextNode)row.Cells["category"]).Text).Select(group => group.First()).ToArray();
        Assert.NotEmpty(rows);
        var original = file.ReadAllBytes();
        foreach (var row in rows)
        {
            var field = (FieldNode)row.Cells["value"];
            Assert.Contains("softlocks", field.Description);
            var parts = row.Key.Split('/');
            var offset = Convert.ToInt32(parts[2], 16);
            var bit = int.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
            var target = field.Value.AsInt32() ^ 1;
            Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of(2L))]).Succeeded);
            Assert.True(session.ApplyEdits([new(field.ValueId, JsonSerializer.SerializeToElement(target))]).Ok);
            var edited = Assert.Single(document.Serialize()).Content.ToArray();
            Assert.Equal((byte)(original[offset] ^ (1 << bit)), edited[offset]);
            ByteChangeAssertions.OnlyRangeChanged(original, edited, offset, 1);
            using var reopened = plugin.Open(MemorySaveSet.SingleFile(name, edited, title));
            var page = ((IQueryableDocument)reopened).Query("progress", new()
            {
                Filters = new Dictionary<string, string> { ["address"] = ((TextNode)row.Cells["address"]).Text },
            });
            Assert.Equal(target, ((FieldNode)Assert.Single(page.Rows).Cells["value"]).Value.AsInt32());
            CheckHistory(session, document, original, edited);
        }
    }

    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void ExistingMappedCountCategoriesHaveExactByteScopeAndReversibleBounds(string title)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        var file = SampleSaves.Load(title).AllDiskFiles.First(file => file.FileName == schema.Prefix + "000");
        var plugin = new FinalFantasy7Plugin();
        var document = plugin.Open(file);
        using var session = new DocumentSession("counts", file, plugin, document);
        _ = session.BuildView();
        var rows = Enumerable.Range(0, 11).SelectMany(page => session.QueryCollection("inventory", new() { Offset = page * 200, Limit = 200 }).Rows)
            .Where(row => row.Cells["quantity"] is FieldNode { Spec.ReadOnly: false })
            .GroupBy(row => ((TextNode)row.Cells["category"]).Text).Select(group => group.First()).ToArray();
        Assert.True(rows.Length > 1);
        var original = file.ReadAllBytes();
        foreach (var row in rows)
        {
            var field = (FieldNode)row.Cells["quantity"];
            var spec = (IntegerSpec)field.Spec;
            var target = field.Value.AsInt32() > 0 ? field.Value.AsInt32() - 1 : 1;
            Assert.Contains("game may reject", field.Description);
            Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of(-1L))]).Succeeded);
            Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of(spec.Max + 1))]).Succeeded);
            Assert.True(session.ApplyEdits([new(field.ValueId, JsonSerializer.SerializeToElement(target))]).Ok);
            var edited = Assert.Single(document.Serialize()).Content.ToArray();
            var slot = int.Parse(row.Key.Split('/')[1]);
            var layout = GameplayLayout.For(schema);
            ByteChangeAssertions.OnlyRangeChanged(original, edited, layout.InventoryOffset + slot * layout.InventoryStride + GameplayLayout.QuantityDelta, 4);
            using var reopened = plugin.Open(MemorySaveSet.SingleFile(file.FileName, edited, title));
            _ = ((IQueryableDocument)reopened).Query("inventory", new() { Filters = new Dictionary<string, string> { ["slot"] = slot.ToString() } });
            Assert.Equal(target, reopened.GetValue(field.ValueId).AsInt32());
            CheckHistory(session, document, original, edited);
        }
    }

    [Fact]
    public void ConflictingGilAliasesAndInvalidBitBatchesAreAtomic()
    {
        var file = SampleSaves.Load("PPSA08664").AllDiskFiles.First(file => file.FileName == "ff7rebirth000");
        using var document = new FinalFantasy7Plugin().Open(file);
        _ = document.BuildView();
        _ = document.ExpandNode("inventory/0");
        Assert.False(document.ApplyEdits([new("game/gil", FieldValue.Of(2L)), new("inventory/10/quantity", FieldValue.Of(3L))]).Succeeded);
        var rows = ((IQueryableDocument)document).Query("progress", new()).Rows;
        var first = (FieldNode)rows[0].Cells["value"];
        var second = (FieldNode)rows[1].Cells["value"];
        Assert.False(document.ApplyEdits([new(first.ValueId, FieldValue.Of(first.Value.AsInt32() ^ 1)),
            new(second.ValueId, FieldValue.Of(-1L))]).Succeeded);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static void CheckHistory(DocumentSession session, ISaveDocument document, byte[] original, byte[] edited)
    {
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Undo().Ok);
    }
}
