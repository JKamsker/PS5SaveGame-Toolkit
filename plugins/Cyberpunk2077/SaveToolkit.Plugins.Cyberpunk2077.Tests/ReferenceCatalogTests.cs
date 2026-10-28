using System.Globalization;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class ReferenceCatalogTests
{
    [Theory]
    [InlineData("items", 5248)]
    [InlineData("facts", 19357)]
    [InlineData("vehicles", 105)]
    [InlineData("travel", 232)]
    public void EmbeddedCatalogHasPinnedHashesAndCompleteAttribution(string id, int count)
    {
        var (_, _, context) = ReferencePackFixture.Embedded();
        var catalog = Assert.IsAssignableFrom<IReferenceCatalog>(context.Catalog(id, "2210"));
        Assert.Equal(count, catalog.Count);
        Assert.Equal("1.0.0", catalog.Pack.Version);
        Assert.Empty(catalog.Pack.GameBuilds);
        Assert.Null(catalog.Pack.Locale); // These identifiers are not localization.
        Assert.Equal("MIT", catalog.Dataset.Attribution.License);
        Assert.Contains("c1fd00305e7db905a4e7de8cfa360d4f4efee3b2", catalog.Dataset.Attribution.SourceUrl);
        var records = ReferencePackFixture.All(catalog).ToArray();
        Assert.Equal(count, records.Length);
        Assert.Equal(count, records.Select(record => record.Key).Distinct(StringComparer.Ordinal).Count());
        foreach (var record in records)
        {
            Assert.Null(record.AssetId);
            Assert.Null(record.Limit);
            Assert.Null(record.Description);
            Assert.Contains("DeweySalt", record.AttributionText);
            var expected = id == "facts" ? CyberpunkReferences.FactHash(record.Attributes["canonicalName"])
                : ItemNames.TweakHash(record.Name);
            Assert.Equal(expected.ToString(CultureInfo.InvariantCulture), record.Key);
        }
    }

    [Theory]
    [InlineData("", 2166136261U)]
    [InlineData("a", 3826002220U)]
    [InlineData("foobar", 3214735720U)]
    [InlineData(" q000_done", 2357536991U)]
    public void FactHashMatchesIndependentVectorsWithoutTrimming(string name, uint hash) => Assert.Equal(hash, CyberpunkReferences.FactHash(name));

    [Fact]
    public void CanonicalWhitespaceIsPreservedAndInvalidNamesAreNotResolved()
    {
        var (_, store, context) = ReferencePackFixture.Embedded();
        var references = new CyberpunkReferences(context, 2210);
        Assert.Equal(" q000_done", references.Fact(2357536991)!.Attributes["canonicalName"]);
        var catalog = Assert.IsAssignableFrom<IReferenceCatalog>(context.Catalog("facts", "2210"));
        var whitespace = ReferencePackFixture.All(catalog).Where(record => record.Attributes["canonicalName"] != record.Attributes["canonicalName"].Trim()).ToArray();
        Assert.Equal(9, whitespace.Length);
        Assert.All(whitespace, record => Assert.NotEqual(CyberpunkReferences.FactHash(record.Attributes["canonicalName"].Trim()),
            CyberpunkReferences.FactHash(record.Attributes["canonicalName"])));
        store.Import(ReferencePackFixture.UserPack("facts", [new { id = "2357536991", name = "q000_done" }]), "invalid-canonical.zip");
        var invalid = new CyberpunkReferences(store.Pin(new Cyberpunk2077Plugin(), "PPSA04027"), 2210);
        Assert.Null(invalid.Fact(2357536991));
        Assert.Null(references.Fact(uint.MaxValue));
    }

    [Fact]
    public void ConflictingNamesForOneHashAreRejectedRatherThanSilentlyWinning()
    {
        var (_, store, _) = ReferencePackFixture.Embedded();
        var bytes = ReferencePackFixture.UserPack("facts", [new { id = "42", name = "first candidate" }, new { id = "42", name = "second candidate" }]);
        Assert.Throws<ReferencePackException>(() => store.Import(bytes, "conflicting-facts.zip"));
    }

    [Fact]
    public void PickerInspectionIsReadOnlyAndRejectsUnknownIds()
    {
        var (_, _, context) = ReferencePackFixture.Embedded();
        var references = new CyberpunkReferences(context, 2210);
        var outcome = references.Inspect("inspect-catalog/items", new Dictionary<string, FieldValue>
            { ["record"] = FieldValue.Of(CyberpunkReferences.ItemKey(InventoryReader.MoneyTweakId)) });
        Assert.Contains("Items.money", outcome.Message);
        Assert.Contains("MIT", outcome.Message);
        Assert.Empty(outcome.Changes);
        Assert.True(outcome.Undoable);
        Assert.False(outcome.StructureChanged);
        Assert.Throws<ArgumentException>(() => references.Inspect("inspect-catalog/items", new Dictionary<string, FieldValue> { ["record"] = FieldValue.Of("missing") }));
    }
}
