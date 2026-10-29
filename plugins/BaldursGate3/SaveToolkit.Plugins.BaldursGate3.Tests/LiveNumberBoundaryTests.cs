using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class LiveNumberBoundaryTests
{
    [Theory]
    [InlineData("Health/0/current", 9, 0, 0, 8, 10)]
    [InlineData("Health/0/temporary", 9, 8, 0, 1, 2)]
    [InlineData("Health/1/current", 9, 32, 0, 8, 10)]
    [InlineData("Health/1/temporary", 9, 40, 0, 1, 2)]
    [InlineData("Stats/0/Strength", 10, 8, 1, 10, 10)]
    [InlineData("Stats/0/Dexterity", 10, 12, 1, 10, 10)]
    [InlineData("Stats/0/Constitution", 10, 16, 1, 10, 10)]
    [InlineData("Stats/0/Intelligence", 10, 20, 1, 10, 10)]
    [InlineData("Stats/0/Wisdom", 10, 24, 1, 10, 10)]
    [InlineData("Stats/0/Charisma", 10, 28, 1, 10, 10)]
    [InlineData("Stats/1/Strength", 10, 44, 1, 10, 10)]
    [InlineData("Stats/1/Dexterity", 10, 48, 1, 10, 10)]
    [InlineData("Stats/1/Constitution", 10, 52, 1, 10, 10)]
    [InlineData("Stats/1/Intelligence", 10, 56, 1, 10, 10)]
    [InlineData("Stats/1/Wisdom", 10, 60, 1, 10, 10)]
    [InlineData("Stats/1/Charisma", 10, 64, 1, 10, 10)]
    public void ScalarEndpointsAndImportedValueHaveIndependentByteOracles(
        string key, int column, int relativeOffset, long minimum, long imported, long maximum)
    {
        var original = LiveNewAgeFixture.Create();
        var source = LiveNewAgeFixture.Source(original);
        var number = source.Numbers.Single(number => number.Key == key);
        var offset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(
            original.AsSpan(LsmfFixture.Descriptor(original, column) + 40)) + 48 + relativeOffset);
        Assert.Equal([offset], number.Offsets);
        Assert.Equal((minimum, imported, maximum), (number.Min, number.Original, number.Max));
        foreach (var target in new[] { minimum, imported, maximum })
        {
            var expected = original.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(offset), (uint)target);
            source.Set(number, FieldValue.Of(target));
            Assert.Equal(target, source.Get(number));
            var written = source.Write();
            Assert.Equal(expected, written);
            Assert.Equal(target != imported, source.HasEdits);
            var reopened = LiveNewAgeFixture.Source(written);
            Assert.Empty(reopened.Diagnostics);
            Assert.Equal(target, reopened.Numbers.Single(value => value.Key == key).Original);
        }

        // Rejection must preserve a previous successful edit, rather than resetting to input.
        source.Set(number, FieldValue.Of(minimum));
        var edited = source.Write();
        foreach (var invalid in new[] { minimum - 1, maximum + 1, long.MaxValue })
        {
            Assert.Throws<SaveFormatException>(() => source.Set(number, FieldValue.Of(invalid)));
            Assert.Equal(minimum, source.Get(number));
            Assert.Equal(edited, source.Write());
        }
        source.Set(number, FieldValue.Of(imported));
        Assert.False(source.HasEdits);
        Assert.Equal(original, source.Write());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedBatchPreservesExistingPatchesInEitherEditOrder(bool invalidFirst)
    {
        var original = LsmfFixture.Resource(LiveNewAgeFixture.Create());
        using var document = Assert.IsType<BaldursGate3Document>(new BaldursGate3Plugin().Open(
            MemorySaveSet.SingleFile("Globals.lsf", original)));
        const string health = "live/0/0/Health/0/current";
        const string quantity = "live/0/0/stack/0/member/0/quantity";
        _ = document.BuildView();
        _ = document.Query("live/0/0/values", new() { Limit = 200 });
        var prior = document.ApplyEdits([new(health, FieldValue.Of(7L))]);
        Assert.True(prior.Succeeded);
        Assert.Equal(new ValueChange(health, FieldValue.Of(8L), FieldValue.Of(7L)), Assert.Single(prior.Changes));
        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.NotEqual(original, edited);

        foreach (var invalid in new[] { FieldValue.Of(1L), FieldValue.Of(6L), FieldValue.Of(long.MaxValue),
            FieldValue.Of("2"), FieldValue.Of(2.0), FieldValue.Null })
        {
            ValueEdit validEdit = new(health, FieldValue.Of(0L));
            ValueEdit invalidEdit = new(quantity, invalid);
            var outcome = document.ApplyEdits(invalidFirst ? [invalidEdit, validEdit] : [validEdit, invalidEdit]);
            Assert.False(outcome.Succeeded);
            Assert.Equal(quantity, Assert.Single(outcome.Errors).ValueId);
            Assert.Empty(outcome.Changes);
            Assert.Equal(7, document.GetValue(health).AsInt32());
            Assert.Equal(5, document.GetValue(quantity).AsInt32());
            Assert.Equal(edited, Assert.Single(document.Serialize()).Content.ToArray());
        }

        Assert.True(document.ApplyEdits([new(health, FieldValue.Of(8L))]).Succeeded);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
