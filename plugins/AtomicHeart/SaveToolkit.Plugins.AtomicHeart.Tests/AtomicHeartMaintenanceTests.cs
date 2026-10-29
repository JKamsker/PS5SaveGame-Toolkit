using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartMaintenanceTests
{
    [Fact]
    public void RepeatedRecordNamesKeepBothClassStringsOccurrenceIdsTagsAndNativeTails()
    {
        var plain = PlainFixture.Create(nativeTail: true, secondClassPath: true, repeatedRecords: 2);
        var name = Encoding.UTF8.GetBytes("SyntheticSubsystem1");
        Encoding.UTF8.GetBytes("SyntheticSubsystem0").CopyTo(plain, plain.AsSpan().IndexOf(name));
        var index = new RecordReader(plain).Read();

        const string identity = "SlotData/subsystem/None|%2FScript%2FTest.Subsystem/SyntheticSubsystem0";
        Assert.Equal([identity, identity + "#2"], index.Objects.Select(record => record.Id));
        Assert.All(index.Objects, record =>
        {
            Assert.Equal("SlotData", record.Owner);
            Assert.Equal("None", record.ClassPath);
            Assert.Equal("/Script/Test.Subsystem", record.ClassSubPath);
            Assert.Equal(["SyntheticTag"], record.Tags);
            Assert.Empty(record.PrefixIds);
            Assert.Equal(["Amount", "Other"], record.Properties.Select(property => property.Name));
            Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, plain.AsSpan(record.UnknownTail.Offset, record.UnknownTail.Length).ToArray());
        });
    }

    [Fact]
    public void InventoryIndexRetainsNativeOrderIdentityLimitsAndCurrentValues()
    {
        var bytes = InventoryFixture.Create();
        var index = SlotDataDecoder.Decode(bytes, SaveExtensionFile.Parse(bytes));
        var state = new InventoryState(index, null);
        Assert.Equal([1L, 2L], state.Items.Select(item => item.InstanceId));
        for (var ordinal = 0; ordinal < state.Items.Count; ordinal++)
        {
            var item = state.Items[ordinal];
            Assert.Equal(item.ContainerId + "/item/" + (ordinal + 1), item.Key);
            Assert.Equal("Inventory/ResourcesList", item.Container);
            Assert.True(state.TryGetOrdinal(item.Key, out var actual));
            Assert.Equal(ordinal, actual);
            Assert.Equal(4, state.Limit(item));
            Assert.True(state.CanSetAmount(item));
            Assert.False(state.CanReplace(item));
            Assert.Equal(item.Quantity.Value, state.Current(item.Quantity));
            Assert.Equal(item.RawAsset, state.Asset(item));
        }
        Assert.Empty(state.Edits());
    }

    [Fact]
    public void OverlappingPatchesKeepTheirExactError()
    {
        var first = new TaggedProperty("First", "IntProperty", "", 0, default, new(4, 4), FieldValue.Of(0));
        var second = first with { Name = "Second" };
        var index = new RecordIndex(new byte[8], [], [], []);
        var edits = new Dictionary<TaggedProperty, FieldValue> { [first] = FieldValue.Of(1), [second] = FieldValue.Of(2) };
        var error = Assert.Throws<SaveFormatException>(() => InventoryPatcher.Apply(index, edits));
        Assert.Equal("Overlapping inventory patches.", error.Message);
    }

    [Theory]
    [InlineData("spatial/unknown/0")]
    [InlineData("spatial/map/-1")]
    [InlineData("spatial/map/1")]
    [InlineData("spatial/skills/1")]
    [InlineData("spatial/weapons/1")]
    [InlineData("spatial/details/1")]
    [InlineData("spatial/detail-pages/1")]
    public void SpatialDispatchKeepsExactRejectedIds(string lazyId)
    {
        var bytes = SlotFixture.Create();
        using var document = new AtomicHeartPlugin().Open(SaveToolkit.Testing.MemorySaveSet.SingleFile("slot", bytes));
        var error = Assert.Throws<KeyNotFoundException>(() => document.ExpandNode(lazyId));
        Assert.Equal(lazyId, error.Message);
        Assert.Empty(document.ExpandNode("spatial/details/0"));
        Assert.Empty(document.ExpandNode("spatial/detail-pages/0"));
    }
}
