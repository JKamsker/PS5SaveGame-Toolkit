using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class FallbackNameTests
{
    [Theory]
    [InlineData("PPSA01911", 1U, "Sonon Kusakabe")]
    [InlineData("PPSA08664", 100U, "Cait Sith")]
    [InlineData("PPSA18936", 100U, "Cait Sith")]
    public void LegacyNamesAndCurrentPackFallbackKeepSeparateDomains(string title, uint potion, string seventhCharacter)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        var references = new ReferenceData(IReferenceContext.Empty, schema);
        Assert.Equal("Potion", Ff7Catalog.Item(schema, potion));
        Assert.Equal("Fire", Ff7Catalog.Materia(schema, 10004));
        Assert.Equal("Unknown ID 0xFFFFFFFF", Ff7Catalog.Item(schema, uint.MaxValue));
        Assert.Equal("Unknown ID 0xFFFFFFFF", Ff7Catalog.Materia(schema, uint.MaxValue));
        string[] characters = ["Cloud Strife", "Barret Wallace", "Tifa Lockhart", "Aerith Gainsborough", "Red XIII", "Yuffie Kisaragi", seventhCharacter];
        Assert.Equal(characters, Enumerable.Range(0, 7).Select(index => Ff7Catalog.Character(schema, index)));
        Assert.Equal(characters, Enumerable.Range(0, 7).Select(references.Character));
        Assert.Equal("Character -1", Ff7Catalog.Character(schema, -1));
        Assert.Equal("Character 7", Ff7Catalog.Character(schema, 7));
        Assert.Equal("Buster Sword", references.Weapon(1001));
        Assert.Equal("Unknown weapon 0xFFFFFFFF", references.Weapon(uint.MaxValue));
        Assert.Equal(EmbeddedCatalogs.Attribution, references.CharacterSource(6));
        Assert.Contains("MIT", references.Source("ff7r-weapons", 1001));
    }
}
