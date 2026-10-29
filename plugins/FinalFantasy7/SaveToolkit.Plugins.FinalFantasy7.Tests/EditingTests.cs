using System.Buffers.Binary;
using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class EditingTests
{
    [Fact]
    public void UnprovenRowsRemainReadOnly()
    {
        const string title = "PPSA08664";
        var schema = Ff7Schema.ForTitle(title)!;
        var source = SampleSaves.Load(title).AllDiskFiles.First(f => f.FileName == "ff7rebirth009");
        using var document = new FinalFantasy7Plugin().Open(source);
        var inventory = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode("inventory/1")));
        var materia = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode("materia/0")));
        var empty = inventory.Rows.First(row => uint.Parse(Assert.IsType<FieldNode>(row.Cells["id"]).Value.AsString(), CultureInfo.InvariantCulture) == uint.MaxValue);
        Assert.True(Assert.IsType<FieldNode>(empty.Cells["quantity"]).Spec.ReadOnly);
        var titan = materia.Rows.First(row => uint.Parse(Assert.IsType<FieldNode>(row.Cells["id"]).Value.AsString(), CultureInfo.InvariantCulture) == 14_011);
        Assert.True(Assert.IsType<FieldNode>(titan.Cells["quantity"]).Spec.ReadOnly);
        Assert.Equal("Unknown ID 0xFFFFFFFF", ItemNames.Lookup(schema, false, uint.MaxValue));
    }

    [Fact]
    public void RetailEditsPatchOnlyCuratedBytesAndUndoRestoresTheOriginal()
    {
        const string title = "PPSA08664";
        var schema = Ff7Schema.ForTitle(title)!;
        var source = SampleSaves.Load(title).AllDiskFiles.First(f => f.FileName == schema.Prefix + "000");
        var original = source.ReadAllBytes();
        var itemSlot = Enumerable.Range(0, GameplayFields.InventoryCount).First(slot =>
            BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(0x4FA70 + slot * 32)) == 100); // Potion.
        using var document = new FinalFantasy7Plugin().Open(source);
        _ = document.BuildView();
        _ = document.ExpandNode($"inventory/{itemSlot / GameplayFields.RowsPerPage}");
        _ = document.ExpandNode("materia/0");

        var oldGil = document.GetValue("game/gil");
        var quantityId = $"inventory/{itemSlot}/quantity";
        var oldQuantity = document.GetValue(quantityId);
        var oldAp = document.GetValue("materia/0/ap");
        ValueEdit[] edits =
        [
            new("game/gil", FieldValue.Of(oldGil.AsInt32() + 1L)),
            new(quantityId, FieldValue.Of(oldQuantity.AsInt32() + 1L)),
            new("materia/0/ap", FieldValue.Of(oldAp.AsUInt32() + 1U)),
        ];
        var outcome = document.ApplyEdits(edits);

        Assert.True(outcome.Succeeded);
        var oldValues = new[] { oldGil, oldQuantity, oldAp };
        Assert.Equal(edits.Length, outcome.Changes.Count);
        for (var i = 0; i < edits.Length; i++)
        {
            var change = Assert.Single(outcome.Changes, change => change.ValueId == edits[i].ValueId);
            Assert.Equal(oldValues[i], change.OldValue);
            Assert.Equal(edits[i].Value, change.NewValue);
            Assert.Equal(edits[i].Value, document.GetValue(edits[i].ValueId));
        }
        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        const int inventoryOffset = 0x4FA70;
        const int inventoryStride = 32;
        const int apOffset = 0xBFA70;
        const uint gilId = 1;
        var gilSlot = Enumerable.Range(0, 2048)
            .Single(i => BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(inventoryOffset + i * inventoryStride)) == gilId);
        var expected = original.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(inventoryOffset + gilSlot * inventoryStride + sizeof(uint)), edits[0].Value.AsInt32());
        BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(inventoryOffset + itemSlot * inventoryStride + sizeof(uint)), edits[1].Value.AsInt32());
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(apOffset), edits[2].Value.AsUInt32());
        Assert.Equal(expected, edited);

        using var reopened = new FinalFantasy7Plugin().Open(MemorySaveSet.SingleFile(source.FileName, edited, title));
        _ = reopened.BuildView();
        _ = reopened.ExpandNode($"inventory/{itemSlot / GameplayFields.RowsPerPage}");
        _ = reopened.ExpandNode("materia/0");
        Assert.All(edits, edit => Assert.Equal(edit.Value, reopened.GetValue(edit.ValueId)));

        var undo = document.ApplyEdits(
        [
            new("game/gil", oldGil),
            new(quantityId, oldQuantity),
            new("materia/0/ap", oldAp),
        ]);
        Assert.True(undo.Succeeded);
        Assert.True(original.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
    }

    [Fact]
    public void InvalidEditIsRejectedAtomically()
    {
        const string title = "PPSA01911";
        var schema = Ff7Schema.ForTitle(title)!;
        var source = SampleSaves.Load(title).AllDiskFiles.First(f => f.FileName == schema.Prefix + "000");
        var original = source.ReadAllBytes();
        using var document = new FinalFantasy7Plugin().Open(source);
        _ = document.BuildView();
        _ = document.ExpandNode("inventory/0");
        var oldGil = document.GetValue("game/gil");
        var oldQuantity = document.GetValue("inventory/1/quantity");

        var result = document.ApplyEdits(
        [
            new("game/gil", FieldValue.Of(oldGil.AsInt32() + 1L)),
            new("inventory/1/quantity", FieldValue.Of(10_000L)),
        ]);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Changes);
        Assert.Equal(oldGil, document.GetValue("game/gil"));
        Assert.Equal(oldQuantity, document.GetValue("inventory/1/quantity"));
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
