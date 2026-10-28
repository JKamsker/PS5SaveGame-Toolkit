using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseReferenceTests
{
    [Fact]
    public void EmbeddedCatalogsArePinnedAndAttributed()
    {
        var store = new ReferencePackStore([new TalesOfArisePlugin()]);
        Assert.Empty(store.LoadErrors);
        var context = store.Pin(new TalesOfArisePlugin(), "PPSA02745");
        var items = Assert.IsAssignableFrom<IReferenceCatalog>(context.Catalog("items"));
        var equipment = Assert.IsAssignableFrom<IReferenceCatalog>(context.Catalog("equipment"));
        Assert.Equal(13, items.Count);
        Assert.Equal(14, equipment.Count);
        Assert.Equal("1.0.0", items.Pack.Version);
        Assert.Equal("CC-BY-SA-3.0", items.Dataset.Attribution.License);
        Assert.All(items.Search(new ReferenceQuery { Limit = 50 }).Items, item => Assert.Contains("aselia.fandom.com", item.AttributionText));
        Assert.Equal("Apple Gel", items.Find("1")!.Name);
        Assert.Equal("Alphen", equipment.Find("weapon:alphen:dull-sword")!.Attributes["character"]);
    }

    [Fact]
    public void CatalogQueriesSupportTextCategoryAndCharacterFilters()
    {
        var context = new ReferencePackStore([new TalesOfArisePlugin()]).Pin(new TalesOfArisePlugin(), "PPSA02745");
        using var document = new TalesOfArisePlugin().Open(AriseSampleTests.GameplayFiles().First(f => f.FileName == "SaveData_002.sav"), context);
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        Assert.Equal(13, query.Collections.Single(c => c.Id == "items").TotalCount);
        var apple = query.Query("items", new QueryableCollectionQuery { Text = "1" });
        Assert.Contains(apple.Rows, row => row.Key == "1");
        var alphen = query.Query("equipment", new QueryableCollectionQuery
        {
            Filters = new Dictionary<string, string> { ["category"] = "Weapon", ["character"] = "Alphen" },
        });
        Assert.Equal(3, alphen.Rows.Count);
        Assert.All(alphen.Rows, row => Assert.Equal("Alphen", Assert.IsType<TextNode>(row.Cells["character"]).Text));
        var view = Assert.IsType<TabsNode>(document.BuildView());
        var inventory = Assert.Single(document.ExpandNode("inventory")).AsTable();
        var known = Assert.Single(inventory.Rows, row => row.Cells["id"] is FieldNode field && field.Value.AsString() == "1");
        var id = Assert.IsType<FieldNode>(known.Cells["id"]);
        Assert.IsType<ReferenceSpec>(id.Spec);
        Assert.Equal(Confidence.Inferred, id.Confidence);
        Assert.Contains("numeric", id.Description!, StringComparison.OrdinalIgnoreCase);
        _ = view;
    }

    [Fact]
    public void ReferenceInspectionIsReadOnlyAndUnknownIdsStayCustom()
    {
        var context = new ReferencePackStore([new TalesOfArisePlugin()]).Pin(new TalesOfArisePlugin(), "PPSA02745");
        using var document = new TalesOfArisePlugin().Open(AriseSampleTests.GameplayFiles().First(), context);
        var result = document.InvokeAction("references/inspect/items", new Dictionary<string, FieldValue>
        {
            ["record"] = FieldValue.Of("1"),
        });
        Assert.Contains("Apple Gel", result.Message);
        Assert.Empty(result.Changes);
        Assert.True(result.Undoable);
        Assert.Throws<ArgumentException>(() => document.InvokeAction("references/inspect/items", new Dictionary<string, FieldValue>
        {
            ["record"] = FieldValue.Of("9999"),
        }));
        Assert.Equal(AriseSampleTests.GameplayFiles().First().ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }
}

internal static class ViewNodeTestExtensions
{
    public static TableNode AsTable(this ViewNode node) => Assert.IsType<TableNode>(node);
}
