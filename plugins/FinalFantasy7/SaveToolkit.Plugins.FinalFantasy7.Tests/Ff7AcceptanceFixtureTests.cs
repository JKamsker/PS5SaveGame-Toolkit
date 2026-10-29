using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class Ff7AcceptanceFixtureTests
{
    [Theory]
    [InlineData("PPSA01911", "ff7remake000", "ff7remakecommon")]
    [InlineData("PPSA08664", "ff7rebirth000", "ff7rebirthcommon")]
    [InlineData("PPSA18936", "ff7rebirth000", "ff7rebirthdemocommon")]
    public void PublicFixtureUsesProductionProbeAndOpenGates(string titleId, string gameplayName, string commonName)
    {
        var fixture = Ff7AcceptanceFixtures.Create(titleId);
        Assert.Equal(gameplayName, fixture.GameplayName);
        Assert.Equal(commonName, fixture.CommonName);
        var plugin = new FinalFantasy7Plugin();
        var gameplay = MemorySaveSet.SingleFile(fixture.GameplayName, fixture.Gameplay.ToArray(), titleId);
        var common = MemorySaveSet.SingleFile(fixture.CommonName, fixture.Common.ToArray(), titleId);
        Assert.True(plugin.Probe(gameplay).IsMatch);
        Assert.True(plugin.Probe(common).IsMatch);
        using var document = plugin.Open(gameplay);
        Assert.Equal(fixture.Gameplay.ToArray(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void PublicFixturePublishesCatalogIdsAndCanonicalOffsetDomains()
    {
        var fixture = Ff7AcceptanceFixtures.Create("PPSA08664");
        Assert.Equal(100U, fixture.ItemType);
        Assert.Equal(10020U, fixture.MateriaType);
        Assert.Equal(300U, fixture.MateriaTargetAp);
        Assert.Equal(300U, fixture.MateriaSecondTierAp);
        Assert.Contains("inventory/200/quantity", fixture.FieldOffsets.Keys);
        Assert.Contains("materia/5/level", fixture.FieldOffsets.Keys);
        Assert.Contains("character/1/hp", fixture.FieldOffsets.Keys);
        Assert.Contains("owned/0/instance", fixture.FieldOffsets.Keys);
        Assert.Contains("detail/5/owner", fixture.FieldOffsets.Keys);
    }
}
