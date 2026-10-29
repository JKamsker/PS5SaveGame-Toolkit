using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

/// <summary>Literal fixture offsets keep writer checks independent of the production counter index.</summary>
public sealed class RagnarokSyntheticWriteTests
{
    private const int TimeOffset = 0x190;
    private const int QuantityOffset = 0x51C;
    private const string TimeId = "metadata/play-seconds";
    private const string QuantityId = "wallet/00004895C643225F/resource/5AE80674D240529E/0/quantity";

    [Theory]
    [InlineData(TimeOffset, TimeId, 0u)]
    [InlineData(TimeOffset, TimeId, 1u)]
    [InlineData(TimeOffset, TimeId, uint.MaxValue)]
    [InlineData(QuantityOffset, QuantityId, 0u)]
    [InlineData(QuantityOffset, QuantityId, 1u)]
    [InlineData(QuantityOffset, QuantityId, uint.MaxValue)]
    public void ImportedUnsignedBoundariesRoundTripAndRestoreExactly(int offset, string id, uint imported)
    {
        var original = Fixture(offset, imported);
        using var document = Open(original);
        BindFields(document);
        Assert.Equal((Int128)imported, document.GetValue(id).AsInteger());
        var reduced = imported == 0 ? 0u : imported - 1;
        var result = document.ApplyEdits([new(id, FieldValue.Of((long)reduced))]);
        Assert.True(result.Succeeded);
        Assert.Contains(RagnarokWrites.IntegrityWarning, result.Warnings);
        Assert.Equal(new ValueChange(id, FieldValue.Of((long)imported), FieldValue.Of((long)reduced)), Assert.Single(result.Changes));
        var expected = original.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(offset), reduced);
        var changed = Assert.Single(document.Serialize());
        Assert.Equal("PPSA08330/sdimg_TEST/GOWRSAVE0", changed.RelativePath);
        Assert.Equal(expected, changed.Content.ToArray());

        using var reopened = Open(changed.Content.ToArray());
        BindFields(reopened);
        Assert.Equal((Int128)reduced, reopened.GetValue(id).AsInteger());
        Assert.Equal(expected, Assert.Single(reopened.Serialize()).Content.ToArray());
        Assert.True(document.ApplyEdits([new(id, FieldValue.Of((long)imported))]).Succeeded);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(TimeOffset, TimeId, 0u)]
    [InlineData(TimeOffset, TimeId, uint.MaxValue)]
    [InlineData(QuantityOffset, QuantityId, 0u)]
    [InlineData(QuantityOffset, QuantityId, uint.MaxValue)]
    public void InvalidUnsignedValuesRejectTheWholeBatchInEitherOrder(int offset, string id, uint imported)
    {
        var original = Fixture(offset, imported);
        using var document = Open(original);
        BindFields(document);
        var otherId = id == TimeId ? QuantityId : TimeId;
        var otherOriginal = document.GetValue(otherId);
        Assert.True(otherOriginal.AsInteger() > 0);
        var valid = new ValueEdit(otherId, FieldValue.Of(0L));
        foreach (var invalid in new[] { FieldValue.Of(-1L), FieldValue.Of((ulong)imported + 1),
                     FieldValue.Of(Int128.MaxValue), FieldValue.Of("0"), FieldValue.Of(0.0), FieldValue.Of(false), FieldValue.Null })
        foreach (var invalidFirst in new[] { false, true })
        {
            var rejectedEdit = new ValueEdit(id, invalid);
            var result = document.ApplyEdits(invalidFirst ? [rejectedEdit, valid] : [valid, rejectedEdit]);
            Assert.False(result.Succeeded);
            Assert.Equal(id, Assert.Single(result.Errors).ValueId);
            Assert.Empty(result.Changes);
            Assert.Equal((Int128)imported, document.GetValue(id).AsInteger());
            Assert.Equal(otherOriginal, document.GetValue(otherId));
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void MixedValidBatchReportsExactChangesAndWritesOnlyItsTwoWords()
    {
        var original = Fixture(TimeOffset, uint.MaxValue);
        using var document = Open(original);
        BindFields(document);
        var result = document.ApplyEdits([new(TimeId, FieldValue.Of(0L)), new(QuantityId, FieldValue.Of(1L))]);
        Assert.True(result.Succeeded);
        Assert.Equal(new[]
        {
            new ValueChange(TimeId, FieldValue.Of((long)uint.MaxValue), FieldValue.Of(0L)),
            new ValueChange(QuantityId, FieldValue.Of(1234L), FieldValue.Of(1L)),
        }, result.Changes);
        var expected = original.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(TimeOffset), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(QuantityOffset), 1);
        Assert.Equal(expected, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static byte[] Fixture(int offset, uint imported)
    {
        var bytes = RagnarokTestSupport.LegacyGameplay();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(TimeOffset), 10);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), imported);
        return bytes;
    }

    private static ISaveDocument Open(byte[] bytes) =>
        new GodOfWarRagnarokPlugin().Open(MemorySaveSet.SingleFile("GOWRSAVE0", bytes, "PPSA08330"));

    private static void BindFields(ISaveDocument document)
    {
        _ = document.BuildView();
        var wallet = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode("wallet/0")));
        var field = Assert.IsType<FieldNode>(Assert.Single(wallet.Rows).Cells["quantity"]);
        Assert.Equal(QuantityId, field.ValueId);
        Assert.False(field.Spec.ReadOnly);
    }
}
