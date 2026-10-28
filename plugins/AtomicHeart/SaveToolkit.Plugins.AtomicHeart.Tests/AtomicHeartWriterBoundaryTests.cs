using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartWriterBoundaryTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(131072)]
    public void StoredWriterHandlesGrowthShrinkageAndAdditionalChunkBoundaries(int delta)
    {
        // The framing writer accepts opaque bytes; its caller additionally reparses the grammar.
        var originalPlain = PlainFixture.Create();
        var original = SlotFixture.Create(plain: originalPlain);
        var outer = SaveExtensionFile.Parse(original);
        var plain = new byte[originalPlain.Length + delta];
        originalPlain.AsSpan(0, Math.Min(plain.Length, originalPlain.Length)).CopyTo(plain);
        for (var i = originalPlain.Length; i < plain.Length; i++) plain[i] = (byte)(i % 251);
        BinaryPrimitives.WriteInt32LittleEndian(plain, plain.Length - sizeof(int));
        var output = SlotDataWriter.Write(original, outer, originalPlain, plain);
        Assert.Equal(plain, WriteTestData.IndependentDecode(original, output));
        Assert.Equal(plain.Length, SaveExtensionFile.Parse(output).Chunks.Sum(c => c.UncompressedBytes));
        if (delta == 0) Assert.Equal(original, output);
    }

    [Fact]
    public void CombinedReferenceAndAmountEditsReparseWithoutLosingOtherEdits()
    {
        var file = WriteTestData.Sample();
        var context = WriteTestData.References();
        using var document = WriteTestData.Plugin.Open(file, context);
        var rows = WriteTestData.Table(document).Rows;
        var weapon = rows.Single(r => WriteTestData.Field(r, "asset").Value.AsString() == "DA_Item_Shved");
        var edits = rows.Select(r => WriteTestData.Field(r, "amount")).Where(f => !f.Spec.ReadOnly)
            .Select(f => new ValueEdit(f.ValueId, FieldValue.Of(0))).ToList();
        edits.Add(new(WriteTestData.Field(weapon, "asset").ValueId, FieldValue.Of("DA_Item_ShotgunKS23")));
        var outcome = document.ApplyEdits(edits);
        Assert.True(outcome.Succeeded);
        var output = WriteTestData.Output(document);
        _ = WriteTestData.IndependentDecode(file.ReadAllBytes(), output);
        using var reopened = WriteTestData.Open(output);
        foreach (var edit in edits) Assert.Equal(edit.Value, reopened.GetValue(edit.ValueId));
        Assert.True(document.ApplyEdits(outcome.Changes.Select(c => new ValueEdit(c.ValueId, c.OldValue)).ToArray()).Succeeded);
        Assert.Equal(file.ReadAllBytes(), WriteTestData.Output(document));
    }
}
