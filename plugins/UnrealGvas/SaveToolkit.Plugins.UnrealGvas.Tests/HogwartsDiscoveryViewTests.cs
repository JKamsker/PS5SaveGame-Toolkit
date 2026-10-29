using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class HogwartsDiscoveryViewTests
{
    private static readonly string[] SyntheticPoints = ["4", "99"];
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SyntheticGvasSqlite_DiscoveredCellsAreInferredReadOnlyAndNoOpIsExact(bool withCatalog)
    {
        var sample = HogwartsDiscoveryFixture.Create();
        var plugin = new UnrealGvasPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01603");
        using var document = withCatalog ? plugin.Open(sample, references) : plugin.Open(sample);
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        Assert.True(document.CanEdit); // Existing unrelated cells retain their Experimental writer.
        var points = query.Query(HogwartsDiscoveryFixture.Prefix + "MiscDataDynamic", new()
        { Filters = new Dictionary<string, string> { ["DataName"] = "PerkPoints" } });
        var pointFields = points.Rows.Select(row => Assert.IsType<FieldNode>(row.Cells["DataValue"])).ToArray();
        Assert.Equal(SyntheticPoints, pointFields.Select(field => field.Value.AsString()));
        Assert.Contains("Unspent talent points", pointFields[0].Label);
        Assert.Contains("unmapped owner", pointFields[1].Description);
        var otherImage = query.Query("hogwarts/RawExclusiveImage/MiscDataDynamic", new());
        Assert.Equal("8", Assert.IsType<FieldNode>(Assert.Single(otherImage.Rows).Cells["DataValue"]).Value.AsString());

        var discovered = new List<FieldNode>(pointFields);
        var pages = query.Query(HogwartsDiscoveryFixture.Prefix + "MiscDataDynamic", new() { Text = "PageCollected" });
        discovered.Add(Assert.IsType<FieldNode>(Assert.Single(pages.Rows).Cells["DataValue"]));
        foreach (string table in new[] { "PerkDynamic", "CollectionDynamic", "MapLocationDataDynamic", "AchievementDynamic" })
        {
            var page = query.Query(HogwartsDiscoveryFixture.Prefix + table, new());
            discovered.AddRange(page.Rows.SelectMany(row => row.Cells.Values).OfType<FieldNode>());
            Assert.NotEmpty(page.Rows.SelectMany(row => row.Cells.Values).OfType<FieldNode>());
        }
        foreach (var field in discovered)
        {
            Assert.True(field.Spec.ReadOnly);
            Assert.Equal(Confidence.Inferred, field.Confidence);
            Assert.Contains("https://github.com/", field.Description);
            Assert.Equal(field.Value, document.GetValue(field.ValueId));
            Assert.False(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
        }
        var collection = query.Query(HogwartsDiscoveryFixture.Prefix + "CollectionDynamic", new() { Limit = 1 });
        var second = query.Query(collection.CollectionId, new() { Offset = 1, Limit = 1 });
        Assert.NotEqual(Assert.Single(collection.Rows).Key, Assert.Single(second.Rows).Key);
        Assert.StartsWith("rowid:", Assert.Single(collection.Rows).Key);
        var unknown = query.Query(collection.CollectionId, new() { Text = "UnmappedState" });
        Assert.Equal("UnmappedState", Assert.IsType<FieldNode>(Assert.Single(unknown.Rows).Cells["ItemState"]).Value.AsString());
        Assert.Equal(sample.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void RejectedMixedBatchCannotChangeTheOrdinaryWriterOrEitherImage()
    {
        var sample = HogwartsDiscoveryFixture.Create();
        using var document = new UnrealGvasPlugin().Open(sample);
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var page = query.Query(HogwartsDiscoveryFixture.Prefix + "MiscDataDynamic", new());
        var ordinary = Assert.IsType<FieldNode>(page.Rows[^1].Cells["DataValue"]);
        var points = Assert.IsType<FieldNode>(page.Rows[0].Cells["DataValue"]);
        Assert.False(ordinary.Spec.ReadOnly);
        Assert.False(document.ApplyEdits([new(ordinary.ValueId, FieldValue.Of("8")), new(points.ValueId, FieldValue.Of("5"))]).Succeeded);
        Assert.Equal(ordinary.Value, document.GetValue(ordinary.ValueId));
        Assert.Equal(sample.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }
}
