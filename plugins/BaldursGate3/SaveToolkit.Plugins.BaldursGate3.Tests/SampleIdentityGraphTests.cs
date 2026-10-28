namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class SampleIdentityGraphTests
{
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
