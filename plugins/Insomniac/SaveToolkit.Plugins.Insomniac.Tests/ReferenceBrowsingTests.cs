using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class ReferenceBrowsingTests
{
    [Theory]
    [InlineData("rift-weapons", "weapon label candidate", "Storm", "Blackhole Storm")]
    [InlineData("rift-armor", "Galactic Ranger", "Head", "Galactic Ranger · Head")]
    [InlineData("rift-world", "location", "Megalopolis", "Megalopolis")]
    [InlineData("rift-schema", "schema field", "CareerBolts", "CareerBolts")]
    [InlineData("rift-item-types", "serialized item type", "Swingshot", "GadgetSwingshotItemPrius")]
    public void AllFiveEmbeddedCataloguesUseFrameworkFilteringSortingAndPaging(
        string id, string category, string text, string expectedName)
    {
        var plugin = new InsomniacPlugin();
        var catalog = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01474").Catalog(id)!;
        Assert.Equal(Enum.GetValues<ReferenceSortKey>(), catalog.SortKeys);
        var match = catalog.Search(new()
        {
            Text = text, Category = category.ToUpperInvariant(), SortBy = ReferenceSortKey.Name, Limit = 1,
        });
        Assert.Equal(1, match.Total);
        Assert.Equal(expectedName, Assert.Single(match.Items).Name);
        Assert.NotNull(match.Items[0].Dataset.Attribution);
        Assert.Empty(catalog.Search(new() { Text = text, Category = "absent category" }).Items);

        foreach (var key in catalog.SortKeys)
        foreach (var descending in new[] { false, true })
        {
            var query = new ReferenceQuery { Category = category, SortBy = key, Descending = descending, Limit = 1 };
            var first = catalog.Search(query);
            var second = catalog.Search(query with { Offset = 1 });
            Assert.True(first.Total > 1);
            Assert.Equal(first.Total, second.Total);
            Assert.Single(first.Items);
            Assert.Single(second.Items);
            Assert.NotEqual(first.Items[0].Key, second.Items[0].Key);
            Assert.Equal(first.Items, catalog.Search(query).Items);
        }
        var alphabetical = catalog.Search(new() { Category = category, SortBy = ReferenceSortKey.Name, Limit = 200 });
        Assert.Equal(alphabetical.Items.Select(record => record.Name).Order(StringComparer.OrdinalIgnoreCase),
            alphabetical.Items.Select(record => record.Name));
    }

    [Fact]
    public void BrowseOnlyLoadoutPickerReturnsFilteredAttributedCandidatesAndCannotEdit()
    {
        var plugin = new InsomniacPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01474");
        var file = Assert.IsType<DiskSaveFile>(PlayerCorpusTests.Find("i29slot0-s-autosave", 0));
        using var document = plugin.Open(file, context);
        using var session = new DocumentSession("browse-only", file, plugin, document, references: context);
        var tabs = Assert.IsType<TabsNode>(session.BuildView());
        var field = Assert.Single(tabs.Tabs, tab => tab.Title == "Loadout").Children.OfType<FieldNode>().First();
        Assert.True(Assert.IsType<ReferenceSpec>(field.Spec).ReadOnly);
        var before = session.State;
        var original = Assert.Single(document.Serialize()).Content.ToArray();
        var page = session.SearchReferences(field.ValueId, new()
        {
            Text = "Storm", Category = "weapon label candidate", SortBy = ReferenceSortKey.Name, Descending = true,
        }, before.Revision);
        Assert.Equal(Enum.GetValues<ReferenceSortKey>(), page.SortKeys);
        var candidate = Assert.Single(page.Items);
        Assert.Equal("Blackhole Storm", candidate.Label);
        Assert.Equal("CC-BY-SA-3.0", candidate.Dataset!.Attribution.License);
        Assert.Equal(candidate, session.FindReference(field.ValueId, candidate.RawId, before.Revision));
        var result = session.ApplyEdits([new(field.ValueId, JsonSerializer.SerializeToElement(candidate.RawId))]);
        Assert.False(result.Ok);
        Assert.Equal(before, session.State);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void PlayTimeDurationHintRetainsFloat32AndTheStoredFractionalSeconds()
    {
        var file = PlayerCorpusTests.Find("i29slot0-s-snapshot-1992350878", 0);
        using var document = new InsomniacPlugin().Open(file);
        var player = Assert.Single(Assert.IsType<TabsNode>(document.BuildView()).Tabs, tab => tab.Title == "Player");
        var field = Assert.Single(player.Children.OfType<SectionNode>().SelectMany(section => section.Children)
            .OfType<FieldNode>(), node => node.ValueId == "player/Playtime");
        var spec = Assert.IsType<FloatSpec>(field.Spec);
        Assert.Equal(FloatFormat.Duration, spec.Format);
        Assert.Equal(1, spec.UnitsPerSecond);
        Assert.True(spec.SinglePrecision);
        Assert.True(spec.ReadOnly);
        Assert.Equal(3839.539306640625, field.Value.AsDouble());
        Assert.Equal(field.Value, document.GetValue(field.ValueId));
        Assert.Contains("inferred", field.Warning);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }
}
