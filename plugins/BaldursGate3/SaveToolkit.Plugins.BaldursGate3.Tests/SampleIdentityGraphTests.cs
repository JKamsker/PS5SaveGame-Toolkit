namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class SampleIdentityGraphTests
{
    [Fact]
    public void UnownedInventoryRetainsTheNativeSlotOrderAfterOwnedTraversal()
    {
        var source = SampleLiveNewAgeTests.Source(SampleLiveNewAgeTests.Save().ReadAllBytes());
        var entries = Assert.IsType<LsmfInventoryGraph>(source.Inventory).Entries;
        var unowned = entries.Where(entry => entry.OwnerBodyEntityRow is null).ToArray();
        Assert.Equal(636, unowned.Length);
        Assert.All(entries.Take(2), entry => Assert.NotNull(entry.OwnerBodyEntityRow));
        var unownedKeys = unowned.Select(entry => (entry.ContainerRow, entry.ItemEntityRow)).ToHashSet();
        var containerEntities = source.Access.Rows("game.inventory.v1.ContainerComponent", 32)
            .ToDictionary(row => row.Row, row => row.EntityRow);
        var expected = source.Slots.Where(slot => unownedKeys.Contains((slot.ContainerRow, slot.EntityRow)))
            .Select(slot => new LsmfInventoryEntry(slot.ContainerRow, containerEntities[slot.ContainerRow]!.Value,
                slot.EntityRow, slot.Slot, null, $"container/{slot.ContainerRow}/slot/{slot.Index}", false));
        Assert.Equal(expected, unowned);
    }

    [Fact]
    public void NewAgeJoinsUseMeasuredIdentityBridgesAcrossTheSample()
    {
        var source = SampleLiveNewAgeTests.Source(SampleLiveNewAgeTests.Save().ReadAllBytes());
        Assert.NotNull(source.Identity);
        var identity = source.Identity!;

        Assert.Equal(3, identity.Party.Count);
        Assert.Equal([9426, 9427, 9428], identity.Party.Select(member => member.BodyEntityRow).OrderBy(row => row));
        Assert.All(identity.Party, member =>
        {
            Assert.False(member.IsAmbiguous);
            Assert.NotNull(member.TemplateId);
            Assert.NotNull(member.StateEntityRow);
            Assert.NotNull(member.StatsRow);
            Assert.NotNull(member.HealthRow);
            Assert.NotNull(member.SpellBookRow);
            Assert.NotNull(member.ClassesRow);
            Assert.NotNull(member.PrimaryInventoryEntityRow);
        });
        Assert.Equal(3, identity.Party.Select(member => member.StateEntityRow).Distinct().Count());

        Assert.NotNull(source.Inventory);
        var inventory = source.Inventory!;
        Assert.Equal(638, inventory.Entries.Count);
        Assert.Equal(2, inventory.Entries.Count(entry => entry.OwnerBodyEntityRow is not null));
        Assert.All(inventory.Entries, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Path)));
        Assert.Empty(inventory.Gold);
        Assert.Contains(inventory.Diagnostics, message => message.Contains("Gold", StringComparison.Ordinal));
    }
}
