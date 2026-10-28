using System.Buffers.Binary;
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
        var empty = inventory.Rows.First(row => Assert.IsType<FieldNode>(row.Cells["id"]).Value.AsUInt32() == uint.MaxValue);
        Assert.True(Assert.IsType<FieldNode>(empty.Cells["quantity"]).Spec.ReadOnly);
        var titan = materia.Rows.First(row => Assert.IsType<FieldNode>(row.Cells["id"]).Value.AsUInt32() == 14_011);
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
        using var document = new FinalFantasy7Plugin().Open(source);
        _ = document.BuildView();
        _ = document.ExpandNode("inventory/0");
        _ = document.ExpandNode("materia/0");

        var oldGil = document.GetValue("game/gil");
        var oldQuantity = document.GetValue("inventory/1/quantity");
        var oldAp = document.GetValue("materia/0/ap");
        var outcome = document.ApplyEdits(
        [
            new("game/gil", FieldValue.Of(oldGil.AsInt32() + 1L)),
            new("inventory/1/quantity", FieldValue.Of(oldQuantity.AsInt32() + 1L)),
            new("materia/0/ap", FieldValue.Of(oldAp.AsUInt32() + 1U)),
        ]);

        Assert.True(outcome.Succeeded);
        Assert.Equal(3, outcome.Changes.Count);
        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        var inventoryOffset = schema.IsRemake ? 0x34DD8 : 0x4FA70;
        var inventoryStride = schema.IsRemake ? 24 : 32;
        var gilId = schema.IsRemake ? 20U : 1U;
        var gilSlot = Enumerable.Range(0, 2048)
            .Single(i => BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(inventoryOffset + i * inventoryStride)) == gilId);
        var expectedOffsets = new HashSet<int>
        {
            inventoryOffset + gilSlot * inventoryStride + 4,
            inventoryOffset + 1 * inventoryStride + 4,
            0xBFA70,
        };
        var changed = Enumerable.Range(0, original.Length).Where(i => original[i] != edited[i]).ToArray();
        Assert.NotEmpty(changed);
        Assert.All(changed, offset => Assert.Contains(offset, expectedOffsets));

        var undo = document.ApplyEdits(
        [
            new("game/gil", oldGil),
            new("inventory/1/quantity", oldQuantity),
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
    }
}
