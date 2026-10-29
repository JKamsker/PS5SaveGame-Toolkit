using System.Globalization;
using SaveToolkit.Sdk.References;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class FallbackNameTests
{
    [Theory]
    [InlineData("PPSA01911", 1U, "Sonon Kusakabe", "en-US", "Character -1")]
    [InlineData("PPSA08664", 100U, "Cait Sith", "en-US", "Character -1")]
    [InlineData("PPSA18936", 100U, "Cait Sith", "en-US", "Character -1")]
    [InlineData("PPSA01911", 1U, "Sonon Kusakabe", "sv-SE", "Character \u22121")]
    [InlineData("PPSA08664", 100U, "Cait Sith", "sv-SE", "Character \u22121")]
    [InlineData("PPSA18936", 100U, "Cait Sith", "sv-SE", "Character \u22121")]
    public void LegacyNamesAndCurrentPackFallbackKeepSeparateDomains(string title, uint potion, string seventhCharacter,
        string cultureName, string negativeCharacterName)
    {
        using var culture = new TestCulture(CultureInfo.GetCultureInfo(cultureName));
        var schema = Ff7Schema.ForTitle(title)!;
        var references = new ReferenceData(IReferenceContext.Empty, schema);
        Assert.Equal("Potion", Ff7Catalog.Item(schema, potion));
        Assert.Equal("Fire", Ff7Catalog.Materia(schema, 10004));
        Assert.Equal("Unknown ID 0xFFFFFFFF", Ff7Catalog.Item(schema, uint.MaxValue));
        Assert.Equal("Unknown ID 0xFFFFFFFF", Ff7Catalog.Materia(schema, uint.MaxValue));
        string[] characters = ["Cloud Strife", "Barret Wallace", "Tifa Lockhart", "Aerith Gainsborough", "Red XIII", "Yuffie Kisaragi", seventhCharacter];
        Assert.Equal(characters, Enumerable.Range(0, 7).Select(index => Ff7Catalog.Character(schema, index)));
        Assert.Equal(characters, Enumerable.Range(0, 7).Select(references.Character));
        Assert.Equal(negativeCharacterName, Ff7Catalog.Character(schema, -1));
        Assert.Equal("Character 7", Ff7Catalog.Character(schema, 7));
        Assert.Equal("Buster Sword", references.Weapon(1001));
        Assert.Equal("Unknown weapon 0xFFFFFFFF", references.Weapon(uint.MaxValue));
        Assert.Equal(EmbeddedCatalogs.Attribution, references.CharacterSource(6));
        Assert.Contains("MIT", references.Source("ff7r-weapons", 1001));
    }
}
