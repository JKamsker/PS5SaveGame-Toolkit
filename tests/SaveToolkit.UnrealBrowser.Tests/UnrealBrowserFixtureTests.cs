using SaveToolkit.Plugins.UnrealGvas;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.UnrealBrowser;

namespace SaveToolkit.UnrealBrowser.Tests;

public sealed class UnrealBrowserFixtureTests
{
    [Fact]
    public void SettingsUsesTitleReferencesAndOnlyPatchesTheIntendedFloat()
    {
        var original = UnrealBrowserFixtures.Settings();
        using var document = Open(original, UnrealBrowserFixtures.JediTitle);
        Assert.True(document.CanEdit);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        foreach (var collection in new[] { "gvas/properties", "jedi/settings" })
            Assert.Equal("property/0", Assert.IsType<FieldReferenceNode>(Assert.Single(queryable.Query(collection, new()).Rows).Cells["value"]).TargetValueId);
        var metadata = Assert.Single(Assert.IsAssignableFrom<IFieldMetadataDocument>(document).ResolveFieldMetadata(["property/0"]));
        Assert.False(metadata.Spec.ReadOnly);
        Assert.Equal(Confidence.Verified, metadata.Presentation?.Confidence);
        Assert.Contains("Not validated on console", metadata.Presentation?.Warning, StringComparison.Ordinal);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(document.ApplyEdits([new ValueEdit("property/0", FieldValue.Of((double)UnrealBrowserFixtures.EditedBrightness))]).Succeeded);
        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.Equal(UnrealBrowserFixtures.Settings(UnrealBrowserFixtures.EditedBrightness), edited);
        var differences = original.Zip(edited).Select((pair, index) => (pair, index)).Where(item => item.pair.First != item.pair.Second).ToArray();
        Assert.Single(differences); // 1.25f -> 1.75f changes exactly one of the four payload bytes.
    }

    [Fact]
    public void SackboyRetainsContainerOpaqueSummaryAndProtectedChecksum()
    {
        using var document = Open(UnrealBrowserFixtures.Sackboy(), UnrealBrowserFixtures.SackboyTitle);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var rows = queryable.Query("gvas/properties", new() { Limit = 200 }).Rows;
        Assert.Contains("children", Assert.IsType<TextNode>(rows.Single(row => row.Key == "property/0").Cells["value"]).Text, StringComparison.Ordinal);
        Assert.NotEmpty(Assert.IsType<TextNode>(rows.Single(row => row.Key == "property/1").Cells["value"]).Text);
        var metadata = Assert.IsAssignableFrom<IFieldMetadataDocument>(document).ResolveFieldMetadata(["property/0/0", "property/2"]);
        Assert.False(metadata.Single(field => field.ValueId == "property/0/0").Spec.ReadOnly);
        Assert.True(metadata.Single(field => field.ValueId == "property/2").Spec.ReadOnly);
        Assert.All(metadata, field => Assert.Equal(Confidence.Inferred, field.Presentation?.Confidence));
    }

    [Fact]
    public void WideTitleValuesStayTextButRemainFullySearchable()
    {
        using var document = Open(UnrealBrowserFixtures.WideSettings(), UnrealBrowserFixtures.JediTitle);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        foreach (var collection in new[] { "gvas/properties", "jedi/settings" })
        {
            var page = queryable.Query(collection, new() { Text = UnrealBrowserFixtures.WideTail, Limit = 200 });
            Assert.Equal(UnrealBrowserFixtures.WideRowCount, page.Rows.Count);
            Assert.All(page.Rows, row => Assert.Equal(new string('雪', 192) + "…", Assert.IsType<TextNode>(row.Cells["value"]).Text));
        }
        var metadata = Assert.IsAssignableFrom<IFieldMetadataDocument>(document).ResolveFieldMetadata(["property/0"]);
        Assert.Equal("property/0", Assert.Single(metadata).ValueId);
        Assert.Equal(UnrealBrowserFixtures.WideValue, document.GetValue("property/0").AsString());
    }

    [Fact]
    public void ActorObservationsAndIntegrityMetadataStayReadOnly()
    {
        var bytes = UnrealBrowserFixtures.Actors();
        using var document = Open(bytes, UnrealBrowserFixtures.JediTitle);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        Assert.IsType<TextNode>(Assert.Single(queryable.Query("jedi/actor-tags", new()).Rows).Cells["value"]);
        var metadata = Assert.IsAssignableFrom<IFieldMetadataDocument>(document).ResolveFieldMetadata(["property/1", "property/2"]);
        Assert.Equal(2, metadata.Count);
        Assert.All(metadata, field => Assert.True(field.Spec.ReadOnly));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static ISaveDocument Open(byte[] bytes, string titleId) => new UnrealGvasPlugin().Open(
        MemorySaveSet.SingleFile("synthetic.sav", bytes, titleId));
}
