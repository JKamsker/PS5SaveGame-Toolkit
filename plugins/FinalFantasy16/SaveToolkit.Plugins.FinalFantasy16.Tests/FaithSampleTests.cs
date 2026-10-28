using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithSampleTests
{
    private readonly FinalFantasy16Plugin _plugin = new();

    [Theory]
    [InlineData("PPSA10665", 60)]
    [InlineData("PPSA13803", 12)]
    public void EveryArchiveRecordValidatesAndPreservesAllBytes(string title, int expectedRecords)
    {
        var count = 0;
        foreach (var file in FaithSamples.Records(title))
        {
            var original = file.ReadAllBytes();
            Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(4)),
                FaithCrc32.Compute(original.AsSpan(FaithContainer.HeaderSize)));
            var parsed = FaithContainer.Parse(original);
            Assert.Equal(original.Length - sizeof(uint), parsed.TrailerOffset);
            Assert.True(_plugin.Probe(file).IsMatch);
            using var document = _plugin.Open(file);
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
            count++;
        }

        Assert.Equal(expectedRecords, count);
    }

    [Theory]
    [InlineData("PPSA10665", "FAITHDATAAUTO", "game-save-data00", 42, 13891, 104594, 5942, 597253)]
    [InlineData("PPSA10665", "FAITHDATA0001", "game-save-data", 10, 0, 16, 222, 5735)]
    [InlineData("PPSA10665", "FAITHDATA0004", "game-save-data", 19, 189, 14397, 2364, 194436)]
    [InlineData("PPSA13803", "FAITHDATAAUTO", "game-save-data00", 10, 0, 16, 234, 88270)]
    public void KnownSnapshotValuesMatchTheResearchedSchema(string title, string directory, string name,
        int level, int xp, int gil, int abilityPoints, int playTime)
    {
        using var document = _plugin.Open(FaithSamples.Snapshot(title, directory, name));
        _ = document.BuildView();
        Assert.Equal(level, document.GetValue("player/level").AsInt32());
        Assert.Equal(xp, document.GetValue("player/xp").AsInt32());
        Assert.Equal(gil, document.GetValue("inventory/0/quantity").AsInt32());
        Assert.Equal(abilityPoints, document.GetValue("inventory/1/quantity").AsInt32());
        Assert.Equal(playTime, document.GetValue("user/playTime").AsInt32());
    }

    [Fact]
    public void RepeatedRetailPathContainsDistinctPreservedSnapshots()
    {
        var first = FaithSamples.Snapshot("PPSA10665");
        var second = FaithSamples.Snapshot("PPSA10665", occurrence: 1);
        Assert.NotEqual(first.ReadAllBytes(), second.ReadAllBytes());
        using var before = _plugin.Open(first);
        using var after = _plugin.Open(second);
        _ = before.BuildView();
        _ = after.BuildView();
        Assert.True(before.CanEdit);
        Assert.True(after.CanEdit); // Empty main inventory still has independently gated context fields.
        Assert.Equal(0, after.GetValue("user/playTime").AsInt32());
        Assert.Equal(second.ReadAllBytes(), Assert.Single(after.Serialize()).Content.ToArray());
    }

    [Fact]
    public void DemoPlayTimeDeltasMatchTimestampSecondsAcrossAllSnapshots()
    {
        const int SavedTimeOffset = 88;
        var differences = new HashSet<long>();
        foreach (var file in FaithSamples.Records("PPSA13803").Where(file => file.FileName.StartsWith("game-", StringComparison.Ordinal)))
        {
            var bytes = file.ReadAllBytes();
            var user = Assert.Single(FaithContainer.Parse(bytes).Blocks, FaithSchema.SupportsUser);
            var playTime = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(user.PayloadOffset));
            var timestamp = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(user.PayloadOffset + SavedTimeOffset));
            differences.Add((long)timestamp - playTime);
        }

        Assert.Equal(1700236450, Assert.Single(differences));
    }

    [Theory]
    [InlineData("PPSA10665", 49)]
    [InlineData("PPSA13803", 11)]
    public void EditingEveryPopulatedSnapshotChangesOnlyStackAndCrc(string title, int expectedEdited)
    {
        var edited = 0;
        foreach (var file in FaithSamples.Records(title))
        {
            using var document = _plugin.Open(file);
            if (FaithInventory.Parse(file.ReadAllBytes(), FaithContainer.Parse(file.ReadAllBytes())).Items.Count == 0)
            {
                continue;
            }

            _ = document.BuildView();
            var original = file.ReadAllBytes();
            var inventory = FaithInventory.Parse(original, FaithContainer.Parse(original));
            var gil = Assert.Single(inventory.Items, item => item.Id == 1);
            var next = FieldValue.Of(document.GetValue(gil.QuantityId).AsInt64() + 257);
            var outcome = document.ApplyEdits([new ValueEdit(gil.QuantityId, next)]);
            Assert.True(outcome.Succeeded);
            Assert.Contains(outcome.Changes, change => change.ValueId == "integrity/crc32");
            var output = Assert.Single(document.Serialize()).Content.ToArray();
            Assert.Equal(original.Length, output.Length);
            for (var offset = 0; offset < original.Length; offset++)
            {
                if (offset is >= 4 and < 8 || offset >= gil.Offset + 4 && offset < gil.Offset + 8)
                {
                    continue;
                }

                Assert.Equal(original[offset], output[offset]);
            }

            using var reopened = _plugin.Open(FaithSamples.WithBytes(file, output));
            _ = reopened.BuildView();
            Assert.Equal(next, reopened.GetValue(gil.QuantityId));
            Assert.Equal(output, Assert.Single(reopened.Serialize()).Content.ToArray());
            Assert.Equal(original, file.ReadAllBytes());
            edited++;
        }

        Assert.Equal(expectedEdited, edited);
    }

    [Fact]
    public void NonResourceStackEditsKeepTheItemIdentityAndAllocator()
    {
        var file = FaithSamples.Snapshot("PPSA10665");
        var original = file.ReadAllBytes();
        var inventory = FaithInventory.Parse(original, FaithContainer.Parse(original));
        var item = inventory.Items.First(item => item.Id > 3 &&
            BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(item.Offset + 4)) > 0);
        using var document = _plugin.Open(file);
        _ = document.BuildView();
        Assert.True(document.ApplyEdits([new ValueEdit(item.QuantityId, FieldValue.Of(0L))]).Succeeded);
        var output = Assert.Single(document.Serialize()).Content.ToArray();
        var editedInventory = FaithInventory.Parse(output, FaithContainer.Parse(output));
        Assert.Equal(inventory.Items, editedInventory.Items);
        using var reopened = _plugin.Open(FaithSamples.WithBytes(file, output));
        _ = reopened.BuildView();
        Assert.Equal(0, reopened.GetValue(item.QuantityId).AsInt32());
    }

    [Fact]
    public void EditsAreAtomicIsolatedAndReversibleIncludingCrc()
    {
        var file = FaithSamples.Snapshot("PPSA10665");
        using var document = _plugin.Open(file);
        using var independent = _plugin.Open(file);
        _ = document.BuildView();
        _ = independent.BuildView();
        var original = file.ReadAllBytes();
        var gil = document.GetValue("inventory/0/quantity");
        var next = new ValueEdit("inventory/0/quantity", FieldValue.Of(gil.AsInt64() + 1));
        var rejected = document.ApplyEdits([next, new ValueEdit("inventory/1/quantity", FieldValue.Of(-1L))]);
        Assert.False(rejected.Succeeded);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.False(document.ApplyEdits([next, new ValueEdit("player/level", FieldValue.Of(100L))]).Succeeded);
        var accepted = document.ApplyEdits([next]);
        Assert.True(accepted.Succeeded);
        Assert.Equal(gil, independent.GetValue(next.ValueId));
        Assert.True(document.ApplyEdits(accepted.Changes.Where(change => change.ValueId != "integrity/crc32")
            .Select(change => new ValueEdit(change.ValueId, change.OldValue)).ToArray()).Succeeded);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
