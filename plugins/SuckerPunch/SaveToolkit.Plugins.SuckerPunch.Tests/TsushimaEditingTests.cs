using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;
using static SaveToolkit.Plugins.SuckerPunch.Tests.SuckerPunchTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class TsushimaEditingTests
{
    [Fact]
    public void EverySnapshotEditsResourcesAndAmmoWithoutChangingOtherBytes()
    {
        var plugin = new SuckerPunchPlugin();
        var files = SampleSaves.Load(TsushimaTitle).AllDiskFiles.Where(file => plugin.Probe(file).IsMatch).ToArray();
        Assert.Equal(25, files.Length);
        foreach (var file in files)
        {
            var original = file.ReadAllBytes();
            using var document = Open(file);
            Assert.True(document.CanEdit);
            var supplies = Quantity(document, "Supplies");
            var arrow = Quantity(document, "ArrowNormal");
            var edits = new List<ValueEdit>();
            if (!supplies.Spec.ReadOnly)
            {
                edits.Add(Edit(SuppliesId, supplies.Value.AsInt64() + 1));
            }

            if (!arrow.Spec.ReadOnly)
            {
                edits.Add(Edit(ArrowId, arrow.Value.AsInt64() == 0 ? 1 : 0));
            }

            var outcome = document.ApplyEdits(edits);
            Assert.True(outcome.Succeeded);
            var output = Assert.Single(document.Serialize());
            var bytes = output.Content.ToArray();
            Assert.Equal(Checksum(bytes), Word(bytes, 8));
            Assert.Equal(file.RelativePath, output.RelativePath);
            var expected = (byte[])original.Clone();
            var anchor = expected.AsSpan().IndexOf("\x11\0\0\0LegendsInvestment"u8);
            foreach (var edit in edits)
            {
                var name = edit.ValueId.Split('/')[1];
                var nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
                var offset = anchor + expected.AsSpan(anchor).IndexOf(nameBytes) + nameBytes.Length + sizeof(uint);
                BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(offset), edit.Value.AsUInt32());
                if (name == "ArrowNormal")
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(offset + sizeof(uint)), edit.Value.AsUInt32());
                }
            }

            RepairChecksum(expected);
            Assert.Equal(expected, bytes);
            using var reopened = Open(WithBytes(bytes));
            foreach (var edit in edits)
            {
                Assert.Equal(edit.Value, reopened.GetValue(edit.ValueId));
            }

            if (Word(original, 8) != Word(bytes, 8))
            {
                var checksumChange = Assert.Single(outcome.Changes, change => change.ValueId == "container/word2");
                Assert.Equal(FieldValue.Of((ulong)Word(original, 8)), checksumChange.OldValue);
                Assert.Equal(FieldValue.Of((ulong)Word(bytes, 8)), document.GetValue("container/word2"));
            }
        }
    }

    [Fact]
    public void UndoRestoresOriginalBytesAndChecksum()
    {
        var file = Slot("suckerpunchUser1");
        using var document = Open(file);
        var original = file.ReadAllBytes();
        var outcome = document.ApplyEdits([Edit(SuppliesId, 86), Edit(ArrowId, 7)]);
        Assert.True(outcome.Succeeded);
        Assert.True(document.ApplyEdits(outcome.Changes.Where(change => change.ValueId != "container/word2")
            .Reverse().Select(change => new ValueEdit(change.ValueId, change.OldValue)).ToArray()).Succeeded);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(32768)]
    public void InvalidBatchIsAtomic(long badQuantity)
    {
        var file = Slot("suckerpunchUser1");
        using var document = Open(file);
        var result = document.ApplyEdits([Edit(SuppliesId, 86), Edit(ArrowId, badQuantity)]);
        Assert.False(result.Succeeded);
        Assert.Empty(result.Changes);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void LockedAndInconsistentAmmoCannotBeEdited()
    {
        using var locked = Open(Slot("suckerpunchUser0"));
        Assert.True(Quantity(locked, "ArrowNormal").Spec.ReadOnly);
        Assert.False(locked.ApplyEdits([Edit(ArrowId, 1)]).Succeeded);
        var bytes = Slot("suckerpunchUser1").ReadAllBytes();
        const int mirrorOffset = 0x4BB8A;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(mirrorOffset), 5);
        RepairChecksum(bytes);
        using var inconsistent = Open(WithBytes(bytes));
        Assert.True(Quantity(inconsistent, "ArrowNormal").Spec.ReadOnly);
        Assert.False(inconsistent.ApplyEdits([Edit(ArrowId, 1)]).Succeeded);
        Assert.Equal(bytes, Assert.Single(inconsistent.Serialize()).Content.ToArray());
    }

    [Fact]
    public void PaddingAndReturnedMemoryStayIsolated()
    {
        var bytes = Slot("suckerpunchUser1").ReadAllBytes();
        bytes[^1] = 0xA5;
        using var document = Open(WithBytes(bytes));
        var blob = document.ReadBlob("content", 0, bytes.Length);
        Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(blob, out var array));
        array.Array![array.Offset] = 0;
        Assert.True(document.ApplyEdits([Edit(SuppliesId, 86)]).Succeeded);
        var output = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.Equal(0xA5, output[^1]);
        Assert.Equal((uint)0x14E, Word(output, 0));
        output[0] = 0;
        Assert.Equal((uint)0x14E, Word(Assert.Single(document.Serialize()).Content.ToArray(), 0));
    }
}
