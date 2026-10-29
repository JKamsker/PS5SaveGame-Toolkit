using SaveToolkit.Core.References;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseFixedInspectionTests
{
    [Theory]
    [InlineData("items")]
    [InlineData("artes")]
    [InlineData("skills")]
    public void FixedCatalogAction_ResolvesOnlyItsOwnRecordOnAFreshDocument(string catalogId)
    {
        var plugin = new TalesOfArisePlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, AriseTestContainer.TitleId);
        var bytes = new AriseTestContainer().Encrypt();
        var file = MemorySaveSet.SingleFile(AriseTestContainer.FileName, bytes, AriseTestContainer.TitleId);
        using var document = plugin.Open(file, context);
        var page = Assert.IsAssignableFrom<IQueryableDocument>(document).Query(catalogId, new() { Limit = 2 });
        var action = Assert.IsType<ActionNode>(page.Rows[0].Cells["inspect"]);
        var other = Assert.IsType<ActionNode>(page.Rows[1].Cells["inspect"]);
        Assert.NotEqual(action.ActionId, other.ActionId);
        using var fresh = plugin.Open(file, context); // Inspection dispatch must not depend on remembered query rows.
        var parameters = new Dictionary<string, FieldValue> { ["record"] = FieldValue.Of(page.Rows[0].Key) };
        Assert.Contains(context.Catalog(catalogId)!.Find(page.Rows[0].Key)!.Name,
            fresh.InvokeAction(action.ActionId, parameters).Message);
        parameters["record"] = FieldValue.Of(page.Rows[1].Key);
        Assert.Throws<ArgumentException>(() => fresh.InvokeAction(action.ActionId, parameters));
        Assert.Equal(bytes, Assert.Single(fresh.Serialize()).Content.ToArray());
    }

    [Fact]
    public void InventoryAction_RejectsAnotherExistingItemAndPreservesBytes()
    {
        var plugin = new TalesOfArisePlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, AriseTestContainer.TitleId);
        var bytes = new AriseTestContainer().Encrypt();
        using var document = plugin.Open(MemorySaveSet.SingleFile(
            AriseTestContainer.FileName, bytes, AriseTestContainer.TitleId), context);
        var inventory = document.LoadInventory();
        var row = Assert.Single(inventory.Rows, row => row.Key == "inventory/0/quantity");
        var action = Assert.IsType<ActionNode>(row.Cells["inspect"]);
        Assert.True(Assert.IsType<ReferenceSpec>(Assert.Single(action.Parameters).Spec).ReadOnly);
        var valid = new Dictionary<string, FieldValue> { ["record"] = FieldValue.Of("1") };
        Assert.Contains("Apple Gel", document.InvokeAction(action.ActionId, valid).Message);
        var changed = new Dictionary<string, FieldValue> { ["record"] = FieldValue.Of("2") };
        Assert.Throws<ArgumentException>(() => document.InvokeAction(action.ActionId, changed));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void CatalogPicker_AllowsOtherPinnedRecordsButRejectsExtraParameters()
    {
        var references = new AriseReferences(new ReferencePackStore([new TalesOfArisePlugin()])
            .Pin(new TalesOfArisePlugin(), AriseTestContainer.TitleId));
        var section = Assert.IsType<SectionNode>(references.CatalogSection("items"));
        var action = Assert.Single(section.Children.OfType<ActionNode>());
        Assert.Equal("references/inspect/items", action.ActionId);
        var parameters = new Dictionary<string, FieldValue> { ["record"] = FieldValue.Of("2") };
        Assert.Contains("Peach Gel", references.Inspect(action.ActionId, parameters).Message);
        parameters["unexpected"] = FieldValue.Of("1");
        Assert.Throws<ArgumentException>(() => references.Inspect(action.ActionId, parameters));
    }
}
